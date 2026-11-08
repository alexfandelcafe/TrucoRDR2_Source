using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using RDR2;
using RDR2.Math;
using RDR2.Native;
using RDR2.UI;

namespace TrucoRDR2
{
    /// <summary>
    /// Persistent truco table in the Valentine saloon.
    /// The furniture and seated rival are created when the script starts.
    /// The player joins directly through the rival's contextual E prompt.
    /// </summary>
    internal sealed class TrucoWorldInteraction
    {
        private static readonly Vector3 LocationCenter =
            new Vector3(-307.7149f, 802.8571f, 118.9794f);
        private static readonly Vector3 TablePosition = LocationCenter;
        // Move both Windsor chairs inward to 1.0 m from the table center. Keep them at
        // the known interior floor Z; grounding them can snap them onto tabletop collision.
        private static readonly Vector3 PlayerSeat =
            new Vector3(-308.7149f, 802.8571f, 118.9794f);
        private static readonly Vector3 OpponentSeat =
            new Vector3(-306.7149f, 802.8571f, 118.9794f);

        private const string TableModelName = "p_table06x";
        private const string ChairModelName = "p_windsorchair03x";
        private const float TableHeading = 90.0f;
        private const float PlayerChairHeading = 90.0f;
        private const float OpponentChairHeading = 270.0f;
        private const float PlayerHeading = 270.0f;
        private const float OpponentHeading = 90.0f;

        private const float InteractionRadius = 4.5f;
        private const double SetupRetrySeconds = 3.0;
        private const double ScenarioWarmupSeconds = 1.5;

        // Standalone copy of enRAGE's MakePrompt setup sequence.
        private const ulong PromptSetText = 0x5DD02A8318420DD7;
        private const ulong PromptVarString = 0xFA925AC00EB830B9;
        private const ulong PromptIsValid = 0x347469FBDD1589A9;
        private const ulong PromptDelete = 0x00EDE88D4D13CF59;
        private const uint PromptControlHash = 1980406895; // eInputType.InteractOption1
        private const uint StandardizedHoldMode = 1704213876; // enRAGE standardized hold mode
        private static readonly Dictionary<string, IntPtr> PromptPersistentStrings =
            new Dictionary<string, IntPtr>(StringComparer.Ordinal);

        private const ulong IsEntityDead = 0x7D5B1F88E7504BBA;
        private const ulong IsEntityOnFire = 0x1BD7C371CE257C3E;
        private const ulong IsPedInjured = 0x84A2DD9AC37C35C1;
        private const ulong IsPedDeadOrDying = 0x3317DEDB88C95038;
        private const ulong IsPedInCombat = 0x4859F1FC66A6278E;
        private const ulong IsPedShooting = 0x34616828CD07F1A1;
        private const ulong IsPedFalling = 0xFB92A102F1C4DFA3;
        private const ulong IsPedBeingStunned = 0x4FBACCE3B4138EE8;
        private const ulong IsPedFleeing = 0xBBCCE00B381F8482;
        private const ulong IsPedRagdoll = 0x47E4E977581C5B55;
        private const ulong IsPedHogtied = 0x3AA24CCC0D451379;
        private const ulong IsPedFallingOver = 0x3E592D0486DEC0F6;
        private const ulong IsPedGettingUp = 0x2A74E1D5F2F00EEC;

        private readonly uint _seatScenario = Game.Joaat("GENERIC_SEAT_CHAIR_TABLE_SCENARIO");

        private Vector3 _playerSeatPosition;
        private Vector3 _opponentSeatPosition;
        private Prop _tableProp;
        private Prop _playerChairProp;
        private Prop _opponentChairProp;
        private Ped _opponent;

        private int _interactionPrompt;
        private int _interactionPromptGroup;
        private bool _interactionPromptArmed;
        private bool _opponentSpawnAttempted;
        private bool _opponentScenarioStarted;
        private bool _worldSetupComplete;
        private bool _sessionActive;
        private const string DealAnimationDictionary = "mini_games@poker_mg@deal";
        private const ulong RequestAnimationDictionary = 0xD3BD40951412FEF6;
        private const ulong HasAnimationDictionaryLoaded = 0xD031A9162D01088C;
        private const ulong TaskPlayAnimation = 0xEA47FE3719165B94;
        private const int SecondaryAnimationFlag = 32; // AF_SECONDARY; preserves the seated scenario
        private int _pendingAnimationPedHandle;
        private string _pendingAnimationClip;
        private DateTime _pendingAnimationExpiresUtc;
        private bool _hasOriginalPlayerTransform;
        private Vector3 _originalPlayerPosition;
        private float _originalPlayerHeading;
        private DateTime _opponentScenarioStartedUtc;
        private DateTime _nextSetupAttemptUtc;

        // Optional runtime bridge: exclude only our rival from Companion Extension recruitment.
        private static bool _companionEligibilityPatchInstalled;
        private static bool _companionSocialPromptPatchInstalled;
        private static DateTime _nextCompanionPatchAttemptUtc;
        private static int _companionExcludedPedHandle;
        private static MethodInfo _companionSocialTargetMethod;
        private static MethodInfo _companionTuplePedMethod;
        private static MethodInfo _companionHideSocialPromptMethod;
        private string _companionPromptIntegrationStatus = "Esperando la extensión Companion/EnRAGE.";

        public bool IsSessionActive { get { return _sessionActive; } }
        public bool SessionWasInterrupted { get; private set; }
        public string LastError { get; private set; }
        public string CompanionPromptIntegrationStatus { get { return _companionPromptIntegrationStatus; } }

        /// <summary>
        /// Returns true on the frame the contextual prompt starts a new game.
        /// </summary>
        public bool Update(bool tableVisible)
        {
            SessionWasInterrupted = false;
            EnsureWorldSetup();
            EnsureCompanionRecruitmentExclusion();
            ProcessPendingCardAnimation();

            if (_sessionActive)
            {
                if (tableVisible && !IsOpponentAvailable())
                {
                    SessionWasInterrupted = true;
                    EndSession();
                    return false;
                }

                if (!tableVisible)
                    EndSession();

                return false;
            }

            if (tableVisible || !_worldSetupComplete)
            {
                SetPromptEnabled(false);
                return false;
            }

            Ped player = Game.Player.Character;
            if (!CanPlayerInteract(player) || !IsOpponentAvailable())
            {
                SetPromptEnabled(false);
                return false;
            }

            SetPromptEnabled(true);

            // Use the same standardized hold-mode behavior as enRAGE's MakePrompt.
            // The prompt is owned by Truco and works whether Companion is installed or not.
            bool holdCompleted = false;
            try { holdCompleted = HUD._UI_PROMPT_HAS_HOLD_MODE_COMPLETED(_interactionPrompt); }
            catch { }

            if (!holdCompleted)
            {
                _interactionPromptArmed = true;
                return false;
            }

            if (!_interactionPromptArmed)
                return false;

            _interactionPromptArmed = false;
            return TryBegin(player);
        }

        public void Dispose()
        {
            EndSession();
            SetPromptEnabled(false);
            DeletePrompt();

            // The Harmony filter remains harmless after shutdown because handle 0 is never
            // treated as the excluded ped. A later Truco script instance can set a new handle.
            _companionExcludedPedHandle = 0;

            DeleteProp(ref _playerChairProp);
            DeleteProp(ref _opponentChairProp);
            DeleteProp(ref _tableProp);

            try
            {
                if (_opponent != null && _opponent.Exists())
                {
                    _opponent.Task.ClearTasks(true);
                    _opponent.Delete();
                }
            }
            catch
            {
                // Continue cleanup even if the NPC was already removed by the game.
            }

            _opponent = null;
            _worldSetupComplete = false;
        }

        /// <summary>
        /// Plays the poker deal clip on the seated ped using AF_SECONDARY so the
        /// GENERIC_SEAT_CHAIR_TABLE_SCENARIO remains the primary task.
        /// </summary>
        public void PlayCardDealAnimation(bool playerDeals)
        {
            Ped ped = playerDeals ? Game.Player.Character : _opponent;
            if (ped == null || !ped.Exists() || ped.IsDeadOrDying)
                return;

            _pendingAnimationPedHandle = ped.Handle;
            // The known dealer-seat mapping: 1to4 sends a card to the opposite seat.
            _pendingAnimationClip = playerDeals ? "1to4" : "4to1";
            _pendingAnimationExpiresUtc = DateTime.UtcNow.AddSeconds(2.0);

            try
            {
                Function.Call(RequestAnimationDictionary, DealAnimationDictionary);
            }
            catch
            {
                _pendingAnimationPedHandle = 0;
                _pendingAnimationClip = null;
            }
        }

        private void ProcessPendingCardAnimation()
        {
            if (_pendingAnimationPedHandle == 0 || string.IsNullOrEmpty(_pendingAnimationClip))
                return;

            if (DateTime.UtcNow > _pendingAnimationExpiresUtc)
            {
                _pendingAnimationPedHandle = 0;
                _pendingAnimationClip = null;
                return;
            }

            try
            {
                Function.Call(RequestAnimationDictionary, DealAnimationDictionary);
                if (!Function.Call<bool>(HasAnimationDictionaryLoaded, DealAnimationDictionary))
                    return;

                Ped ped = null;
                Ped player = Game.Player.Character;
                if (player != null && player.Exists() && player.Handle == _pendingAnimationPedHandle)
                    ped = player;
                else if (_opponent != null && _opponent.Exists() &&
                         _opponent.Handle == _pendingAnimationPedHandle)
                    ped = _opponent;

                string clip = _pendingAnimationClip;
                _pendingAnimationPedHandle = 0;
                _pendingAnimationClip = null;

                if (ped == null || !ped.Exists() || ped.IsDeadOrDying)
                    return;

                Function.Call(TaskPlayAnimation,
                    ped.Handle,
                    DealAnimationDictionary,
                    clip,
                    8.0f,
                    -8.0f,
                    450,
                    SecondaryAnimationFlag,
                    1.0f,
                    false,
                    0,
                    false,
                    0,
                    false);
            }
            catch
            {
                // A missing dictionary/clip must never break the sitting scenario or match.
                _pendingAnimationPedHandle = 0;
                _pendingAnimationClip = null;
            }
        }
        private void EnsureWorldSetup()
        {
            if (_worldSetupComplete || DateTime.UtcNow < _nextSetupAttemptUtc)
                return;

            _nextSetupAttemptUtc = DateTime.UtcNow.AddSeconds(SetupRetrySeconds);

            try
            {
                // Use the fixed interior floor height. Props are then settled against
                // collision, so setup does not depend on where the player spawned.
                if (_tableProp == null || !_tableProp.Exists())
                    _tableProp = CreateFurnitureProp(TableModelName, TablePosition, TableHeading);

                if (_playerChairProp == null || !_playerChairProp.Exists())
                    _playerChairProp = CreateFurnitureProp(ChairModelName, PlayerSeat, PlayerChairHeading);

                if (_opponentChairProp == null || !_opponentChairProp.Exists())
                    _opponentChairProp = CreateFurnitureProp(ChairModelName, OpponentSeat, OpponentChairHeading);

                // Keep the previously confirmed seat-height offset. Because chairs are not
                // passed through ground-placement, their floor-level world origins stay fixed.
                _playerSeatPosition =
                    _playerChairProp.GetOffsetPosition(new Vector3(0.0f, -0.05f, 0.50f));
                _opponentSeatPosition =
                    _opponentChairProp.GetOffsetPosition(new Vector3(0.0f, -0.05f, 0.50f));

                // Spawn the rival only once. A dead or removed rival is deliberately
                // not replaced, so the interaction stays unavailable after that event.
                if (!_opponentSpawnAttempted)
                {
                    // Spawn the rival at the final seat coordinate from the first frame;
                    // do not create it off to the side and depend on scenario teleport to move it.
                    Vector3 spawnPosition = _opponentSeatPosition;

                    _opponent = World.CreatePed(
                        PedHash.a_m_m_valtownfolk_01, spawnPosition, OpponentHeading);

                    if (_opponent == null || !_opponent.Exists())
                        throw new InvalidOperationException(
                            "No se pudo crear al rival en el salón de Valentine.");

                    _opponentSpawnAttempted = true;
                    _opponent.BlockPermanentEvents = false;
                    _opponent.NeverLeavesGroup = false;
                    _opponent.Heading = OpponentHeading;
                    ConfigureOpponentFocus();
                }

                if (_opponent != null && _opponent.Exists() && !_opponentScenarioStarted)
                {
                    // Use the neutral table-seating scenario so the rival does not start a smoking activity.
                    // Teleport is intentional: the rival is seated as part of world setup.
                    ConfigureOpponentFocus();
                    _opponent.Task.StartScenario(
                        _seatScenario, _opponentSeatPosition, -1, true, true);

                    _opponentScenarioStarted = true;
                    _opponentScenarioStartedUtc = DateTime.UtcNow;
                }

                if (_opponent != null && _opponent.Exists() && _interactionPrompt == 0)
                    CreateInteractionPrompt();

                _worldSetupComplete =
                    _tableProp != null && _tableProp.Exists() &&
                    _playerChairProp != null && _playerChairProp.Exists() &&
                    _opponentChairProp != null && _opponentChairProp.Exists() &&
                    _opponent != null && _opponent.Exists() &&
                    _opponentScenarioStarted &&
                    _interactionPrompt != 0;

                if (_worldSetupComplete)
                    LastError = null;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }

        private void EnsureCompanionRecruitmentExclusion()
        {
            if (_opponent == null || !_opponent.Exists())
            {
                _companionExcludedPedHandle = 0;
                return;
            }

            _companionExcludedPedHandle = _opponent.Handle;
            if ((_companionEligibilityPatchInstalled && _companionSocialPromptPatchInstalled) ||
                DateTime.UtcNow < _nextCompanionPatchAttemptUtc)
                return;

            _nextCompanionPatchAttemptUtc = DateTime.UtcNow.AddSeconds(2.0);

            try
            {
                Type hooksType = null;
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    hooksType = assemblies[i].GetType(
                        "EnRAGECompanionsExtension.EnRAGEHooks", false);
                    if (hooksType != null)
                        break;
                }

                if (hooksType == null)
                {
                    _companionPromptIntegrationStatus =
                        "Extensión Companion/EnRAGE no detectada; no se aplicó exclusión.";
                    return;
                }

                const BindingFlags staticPrivate =
                    BindingFlags.Static | BindingFlags.NonPublic;
                FieldInfo installedField = hooksType.GetField("_installed", staticPrivate);
                FieldInfo harmonyField = hooksType.GetField("_harmony", staticPrivate);
                object installed = installedField == null ? null : installedField.GetValue(null);
                object harmony = harmonyField == null ? null : harmonyField.GetValue(null);
                if (!(installed is bool) || !(bool)installed || harmony == null)
                {
                    _companionPromptIntegrationStatus =
                        "Extensión Companion detectada; esperando que termine de inicializar.";
                    return;
                }

                FieldInfo updateSocialField = hooksType.GetField("_updateSocial", staticPrivate);
                FieldInfo socialTargetField = hooksType.GetField("_socialTarget", staticPrivate);
                FieldInfo mainTypeField = hooksType.GetField("_mainType", staticPrivate);
                _companionSocialTargetMethod =
                    socialTargetField == null ? null : socialTargetField.GetValue(null) as MethodInfo;
                _companionTuplePedMethod = hooksType.GetMethod("TuplePed", staticPrivate);

                Type mainType = mainTypeField == null ? null : mainTypeField.GetValue(null) as Type;
                if (mainType != null)
                {
                    _companionHideSocialPromptMethod = mainType.GetMethod(
                        "HideSocialPrompt",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }

                MethodInfo eligibilityTarget = hooksType.GetMethod(
                    "IsEligibleGenericCandidate", staticPrivate);
                MethodInfo socialTarget =
                    updateSocialField == null ? null : updateSocialField.GetValue(null) as MethodInfo;

                if (eligibilityTarget == null || socialTarget == null ||
                    _companionSocialTargetMethod == null || _companionTuplePedMethod == null)
                {
                    _companionPromptIntegrationStatus =
                        "La versión de Companion no expone los métodos privados esperados; exclusión incompleta.";
                    return;
                }

                Type harmonyMethodType = harmony.GetType().Assembly.GetType(
                    "HarmonyLib.HarmonyMethod", false);
                if (harmonyMethodType == null)
                {
                    _companionPromptIntegrationStatus =
                        "No se encontró HarmonyLib.HarmonyMethod en Companion.";
                    return;
                }

                MethodInfo eligibilityPrefix = typeof(TrucoWorldInteraction).GetMethod(
                    "CompanionEligibilityPrefix",
                    BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo socialPrefix = typeof(TrucoWorldInteraction).GetMethod(
                    "CompanionSocialPrefix",
                    BindingFlags.Static | BindingFlags.NonPublic);

                ConstructorInfo harmonyMethodConstructor = null;
                ConstructorInfo[] constructors = harmonyMethodType.GetConstructors(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < constructors.Length; i++)
                {
                    ParameterInfo[] parameters = constructors[i].GetParameters();
                    if (parameters.Length == 1 &&
                        parameters[0].ParameterType.IsAssignableFrom(typeof(MethodInfo)))
                    {
                        harmonyMethodConstructor = constructors[i];
                        break;
                    }
                }

                if (eligibilityPrefix == null || socialPrefix == null ||
                    harmonyMethodConstructor == null)
                {
                    _companionPromptIntegrationStatus =
                        "No se pudo preparar el filtro Harmony para Companion.";
                    return;
                }

                MethodInfo patchMethod = null;
                MethodInfo[] harmonyMethods = harmony.GetType().GetMethods(
                    BindingFlags.Instance | BindingFlags.Public);
                for (int i = 0; i < harmonyMethods.Length; i++)
                {
                    if (!string.Equals(harmonyMethods[i].Name, "Patch", StringComparison.Ordinal))
                        continue;

                    ParameterInfo[] parameters = harmonyMethods[i].GetParameters();
                    if (parameters.Length >= 2 &&
                        parameters[0].ParameterType.IsAssignableFrom(typeof(MethodInfo)) &&
                        parameters[1].ParameterType == harmonyMethodType)
                    {
                        patchMethod = harmonyMethods[i];
                        break;
                    }
                }

                if (patchMethod == null)
                {
                    _companionPromptIntegrationStatus =
                        "La API Harmony cargada por Companion no ofrece la firma Patch esperada.";
                    return;
                }

                if (!_companionEligibilityPatchInstalled)
                {
                    object harmonyPrefix =
                        harmonyMethodConstructor.Invoke(new object[] { eligibilityPrefix });
                    InvokeHarmonyPrefix(patchMethod, harmony, eligibilityTarget, harmonyPrefix);
                    _companionEligibilityPatchInstalled = true;
                }

                if (!_companionSocialPromptPatchInstalled)
                {
                    object harmonyPrefix =
                        harmonyMethodConstructor.Invoke(new object[] { socialPrefix });
                    InvokeHarmonyPrefix(patchMethod, harmony, socialTarget, harmonyPrefix);
                    _companionSocialPromptPatchInstalled = true;
                }

                _companionPromptIntegrationStatus =
                    "Cabalga conmigo y los prompts sociales de Companion/EnRAGE excluidos únicamente para el NPC del truco. El prompt de Truco se registra de forma autónoma.";

                // If Companion had already created its prompt before these patches were installed,
                // remove that stale action now. The Harmony filters prevent it from returning.
                FieldInfo promptForField = hooksType.GetField("_genericPromptFor", staticPrivate);
                MethodInfo hidePrompt = hooksType.GetMethod(
                    "HideGenericRecruitPrompt", staticPrivate);
                if (promptForField != null && hidePrompt != null)
                {
                    object promptFor = promptForField.GetValue(null);
                    if (promptFor != null &&
                        Convert.ToInt32(promptFor) == _opponent.Handle)
                        hidePrompt.Invoke(null, null);
                }
            }
            catch (Exception ex)
            {
                _companionPromptIntegrationStatus =
                    "No se pudo excluir el NPC de Companion: " + ex.GetBaseException().Message;
            }
        }

        private static void InvokeHarmonyPrefix(
            MethodInfo patchMethod, object harmony, MethodInfo target, object harmonyPrefix)
        {
            ParameterInfo[] patchParameters = patchMethod.GetParameters();
            object[] patchArguments = new object[patchParameters.Length];
            patchArguments[0] = target;
            patchArguments[1] = harmonyPrefix;
            for (int i = 2; i < patchArguments.Length; i++)
                patchArguments[i] = null;

            patchMethod.Invoke(harmony, patchArguments);
        }

        // Excludes only the table rival from Companion Extension's generic recruitment candidates.
        private static bool CompanionEligibilityPrefix(Ped __2, ref bool __result)
        {
            if (__2 != null && __2.Exists() &&
                _companionExcludedPedHandle != 0 &&
                __2.Handle == _companionExcludedPedHandle)
            {
                __result = false;
                return false;
            }

            return true;
        }

        // Companion's base enRAGE social actions share the NPC focus group. Suppress those actions
        // only while this exact rival is the current social target, leaving all other peds untouched.
        private static bool CompanionSocialPrefix(object __instance, Ped __0)
        {
            if (_companionExcludedPedHandle == 0 || __0 == null || !__0.Exists() ||
                _companionSocialTargetMethod == null || _companionTuplePedMethod == null)
                return true;

            try
            {
                object tuple = _companionSocialTargetMethod.Invoke(
                    __instance, new object[] { __0 });
                Ped target = _companionTuplePedMethod.Invoke(null, new object[] { tuple }) as Ped;
                if (target == null || !target.Exists() ||
                    target.Handle != _companionExcludedPedHandle)
                    return true;

                try
                {
                    if (_companionHideSocialPromptMethod != null)
                        _companionHideSocialPromptMethod.Invoke(__instance, null);
                }
                catch
                {
                    // The social update itself is still suppressed for this exact target.
                }

                return false;
            }
            catch
            {
                // Never suppress social interactions for unrelated peds if target lookup fails.
                return true;
            }
        }

        private void ConfigureOpponentFocus()
        {
            if (_opponent == null || !_opponent.Exists())
                return;

            // Force the normal RDR2 interaction-lock-on path for this scenario ped.
            // These flags are also used by companion implementations when a ped must
            // keep its native focus/aim interaction available.
            PED.SET_PED_CONFIG_FLAG(_opponent.Handle, 297, true);
            PED.SET_PED_CONFIG_FLAG(_opponent.Handle, 301, false);
        }

        private void CreateInteractionPrompt()
        {
            // Standalone copy of enRAGE's MakePrompt sequence. It uses the same control,
            // persistent VAR_STRING text, standardized hold mode and entity-focus group,
            // without invoking NemesisMod or depending on Companion at runtime.
            int group = HUD._UI_PROMPT_GET_GROUP_ID_FOR_TARGET_ENTITY(_opponent.Handle);
            if (group == 0)
                throw new InvalidOperationException(
                    "RDR2 no devolvió un grupo de foco válido para el NPC del truco.");

            int prompt = HUD._UI_PROMPT_REGISTER_BEGIN();
            if (prompt == 0)
                throw new InvalidOperationException(
                    "No se pudo registrar el prompt nativo Jugar al truco.");

            try
            {
                HUD._UI_PROMPT_SET_CONTROL_ACTION(prompt, PromptControlHash);
                Function.Call(PromptSetText, prompt,
                    new InputArgument(PromptForeverVarString("Jugar al truco")));
                HUD._UI_PROMPT_SET_STANDARDIZED_HOLD_MODE(prompt, StandardizedHoldMode);
                HUD._UI_PROMPT_SET_GROUP(prompt, group, 0);
                HUD._UI_PROMPT_REGISTER_END(prompt);
                HUD._UI_PROMPT_SET_VISIBLE(prompt, false);
                HUD._UI_PROMPT_SET_ENABLED(prompt, false);

                if (!Function.Call<bool>(PromptIsValid, prompt))
                    throw new InvalidOperationException(
                        "RDR2 rechazó el prompt nativo después de registrarlo.");

                _interactionPrompt = prompt;
                _interactionPromptGroup = group;
                _interactionPromptArmed = false;
            }
            catch
            {
                try { HUD._UI_PROMPT_DELETE(prompt); } catch { }
                _interactionPrompt = 0;
                _interactionPromptGroup = 0;
                _interactionPromptArmed = false;
                throw;
            }
        }

        // Matches enRAGE UiDraw.ForeverString: native text pointers must remain valid
        // after the prompt is registered and rendered in later frames.
        private static ulong PromptForeverString(string text)
        {
            IntPtr pointer;
            if (!PromptPersistentStrings.TryGetValue(text, out pointer))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
                pointer = Marshal.AllocHGlobal(bytes.Length + 1);
                Marshal.Copy(bytes, 0, pointer, bytes.Length);
                Marshal.WriteByte(pointer, bytes.Length, 0);
                PromptPersistentStrings[text] = pointer;
            }

            return unchecked((ulong)pointer.ToInt64());
        }

        private static ulong PromptForeverVarString(string text)
        {
            return Function.Call<ulong>(PromptVarString, new InputArgument[3]
            {
                10,
                new InputArgument(PromptForeverString("LITERAL_STRING")),
                new InputArgument(PromptForeverString(text))
            });
        }

        private static Type FindCompanionHooksType()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(
                    "EnRAGECompanionsExtension.EnRAGEHooks", false);
                if (type != null)
                    return type;
            }
            return null;
        }

        private bool TryBegin(Ped player)
        {
            if (_sessionActive || !CanPlayerInteract(player) || !IsOpponentAvailable())
                return false;

            if (DistanceSquared(player.Position, _opponent.Position) >
                InteractionRadius * InteractionRadius)
                return false;

            try
            {
                _originalPlayerPosition = player.Position;
                _originalPlayerHeading = player.Heading;
                _hasOriginalPlayerTransform = true;

                player.Task.ClearTasks(true);
                player.Heading = PlayerHeading;

                // No approach task or walking phase: the player snaps directly into
                // the same table-seating scenario used by the seated opponent.
                player.Task.StartScenario(
                    _seatScenario, _playerSeatPosition, -1, true, true);

                _sessionActive = true;
                SetPromptEnabled(false);
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                EndSession();
                return false;
            }
        }

        private bool CanPlayerInteract(Ped player)
        {
            if (player == null || !player.Exists() || player.IsDeadOrDying ||
                player.IsOnMount || player.IsInCombat ||
                _opponent == null || !_opponent.Exists())
                return false;

            return DistanceSquared(player.Position, _opponent.Position) <=
                   InteractionRadius * InteractionRadius;
        }

        private bool IsOpponentAvailable()
        {
            try
            {
                if (_opponent == null || !_opponent.Exists() || !_opponentScenarioStarted)
                    return false;

                if ((DateTime.UtcNow - _opponentScenarioStartedUtc).TotalSeconds <
                    ScenarioWarmupSeconds)
                    return false;

                int ped = _opponent.Handle;
                Ped player = Game.Player.Character;
                int playerHandle = player != null && player.Exists() ? player.Handle : 0;

                if (_opponent.IsDeadOrDying ||
                    _opponent.IsInCombat ||
                    _opponent.IsCowering ||
                    _opponent.IsIntimidated ||
                    _opponent.IsRespondingToThreat ||
                    _opponent.IsIncapacitated ||
                    _opponent.IsWrithing ||
                    _opponent.IsDrunk ||
                    _opponent.IsBeingArrested ||
                    _opponent.IsExitingScenario ||
                    _opponent.IsGettingUp ||
                    _opponent.IsHogtying ||
                    _opponent.IsLassoed ||
                    Function.Call<bool>(IsEntityDead, ped) ||
                    Function.Call<bool>(IsEntityOnFire, ped) ||
                    Function.Call<bool>(IsPedInjured, ped) ||
                    Function.Call<bool>(IsPedDeadOrDying, ped, true) ||
                    Function.Call<bool>(IsPedFleeing, ped) ||
                    Function.Call<bool>(IsPedInCombat, ped, playerHandle) ||
                    Function.Call<bool>(IsPedShooting, ped) ||
                    Function.Call<bool>(IsPedFalling, ped) ||
                    Function.Call<bool>(IsPedBeingStunned, ped, 0) ||
                    Function.Call<bool>(IsPedRagdoll, ped) ||
                    Function.Call<bool>(IsPedHogtied, ped) ||
                    Function.Call<bool>(IsPedFallingOver, ped) ||
                    Function.Call<bool>(IsPedGettingUp, ped))
                    return false;

                // Scenario start was successfully requested during world setup.
                // Do not gate the prompt on IS_PED_USING_ANY_SCENARIO: RDR2 can report
                // false for a seated task even while its chair animation remains visible.
                // Explicit negative-state checks above remain authoritative.
                return _opponentScenarioStarted;
            }
            catch
            {
                return false;
            }
        }

        private void SetPromptEnabled(bool enabled)
        {
            try
            {
                if (_interactionPrompt == 0 ||
                    !Function.Call<bool>(PromptIsValid, _interactionPrompt))
                    return;

                if (!enabled)
                    _interactionPromptArmed = false;
                HUD._UI_PROMPT_SET_VISIBLE(_interactionPrompt, enabled);
                HUD._UI_PROMPT_SET_ENABLED(_interactionPrompt, enabled);
            }
            catch
            {
                // Prompt failures must not prevent world-object cleanup.
            }
        }

        private void DeletePrompt()
        {
            try
            {
                if (_interactionPrompt != 0 &&
                    Function.Call<bool>(PromptIsValid, _interactionPrompt))
                {
                    Function.Call(PromptDelete, _interactionPrompt);
                }
            }
            catch
            {
                // Prompt may already have been released by the game.
            }
            finally
            {
                _interactionPrompt = 0;
                _interactionPromptGroup = 0;
                _interactionPromptArmed = false;
            }
        }

        private void EndSession()
        {
            _sessionActive = false;

            try
            {
                Ped player = Game.Player.Character;
                if (_hasOriginalPlayerTransform &&
                    player != null && player.Exists() && !player.IsDeadOrDying &&
                    !player.IsInCombat &&
                    IsWithinSessionArea(player.Position))
                {
                    player.Task.ClearTasks(true);
                    player.PositionNoOffset = _originalPlayerPosition;
                    player.Heading = _originalPlayerHeading;
                }
            }
            catch
            {
                // The world scene remains in place even if the player ped is unavailable.
            }

            _hasOriginalPlayerTransform = false;
            SetPromptEnabled(false);
        }

        private static Prop CreateFurnitureProp(string modelName, Vector3 position, float heading)
        {
            Model model = new Model(Game.Joaat(modelName));
            Prop prop = World.CreateProp(
                model,
                position,
                new Vector3(0.0f, 0.0f, heading),
                false,
                true);

            if (prop == null || !prop.Exists())
                throw new InvalidOperationException(
                    "No se pudo crear el objeto '" + modelName + "'. Comprueba el modelo y la ubicación.");

            prop.IsMissionEntity = true;
            // The table is placed on the floor. Chairs use the fixed interior floor Z instead
            // of the ground-placement native, which can detect the table top as their support.
            if (!string.Equals(modelName, ChairModelName, StringComparison.OrdinalIgnoreCase))
                ENTITY.PLACE_ENTITY_ON_GROUND_PROPERLY(prop.Handle, true);
            // Keep the tabletop and both chairs at their authored world transforms.
            ENTITY.FREEZE_ENTITY_POSITION(prop.Handle, true);
            return prop;
        }

        private static float DistanceSquared(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            float dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        private static bool IsWithinSessionArea(Vector3 position)
        {
            float dx = position.X - LocationCenter.X;
            float dy = position.Y - LocationCenter.Y;
            float dz = position.Z - LocationCenter.Z;
            return Math.Abs(dz) <= 6.0f && dx * dx + dy * dy + dz * dz <= 144.0f;
        }

        private static void DeleteProp(ref Prop prop)
        {
            try
            {
                if (prop != null && prop.Exists())
                    prop.Delete();
            }
            catch
            {
                // Keep deleting the remaining objects even if one prop is already invalid.
            }
            finally
            {
                prop = null;
            }
        }
    }
}
