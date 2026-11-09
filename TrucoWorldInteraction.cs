using System;
using System.Collections.Generic;
using System.Reflection;
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
        private const string DeckPropModelName = "p_cardssplit01x_val";
        private const string DeckFallbackModelName = "p_cards01x";
        private const string HiddenCardPropModelName = "p_crd_joker01x_val";
        private const string GenericCardPropModelName = "p_cs_pokercard01x";
        private const float TableHeading = 90.0f;
        private const float PlayerChairHeading = 90.0f;
        private const float OpponentChairHeading = 270.0f;
        private const float PlayerHeading = 270.0f;
        private const float OpponentHeading = 90.0f;

        private const float InteractionRadius = 4.5f;
        private const double SetupRetrySeconds = 3.0;
        private const double ScenarioWarmupSeconds = 1.5;
        // Let the player's chair-entry animation finish before any poker pose or deal gesture.
        private const double PlayerSeatingWarmupSeconds = 3.0;

        // Standalone copy of enRAGE's MakePrompt setup sequence.
        private const ulong PromptSetText = 0x5DD02A8318420DD7;
        private const ulong PromptVarString = 0xFA925AC00EB830B9;
        private const ulong PromptIsValid = 0x347469FBDD1589A9;
        private const ulong PromptDelete = 0x00EDE88D4D13CF59;
        private const uint PromptControlHash = 1980406895; // eInputType.InteractOption1
        private const uint StandardizedHoldMode = 1704213876; // enRAGE standardized hold mode

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
        // Verified RDR2 natives: clear only the secondary task; keep the NPC seat scenario.
        private const ulong ClearPedSecondaryTaskNative = 0x176CECF6F920D707;
        private const ulong GetEntityBoneIndexByNameNative = 0xBACA8FE9C76C124E;

        private readonly uint _seatScenario = Game.Joaat("GENERIC_SEAT_CHAIR_TABLE_SCENARIO");

        private Vector3 _playerSeatPosition;
        private Vector3 _opponentSeatPosition;
        private Prop _tableProp;
        private Prop _playerChairProp;
        private Prop _opponentChairProp;
        private Prop _deckProp;
        private readonly List<Prop> _playerHandCardProps = new List<Prop>();
        private readonly List<Prop> _opponentHandCardProps = new List<Prop>();
        private Ped _opponent;

        private int _interactionPrompt;
        private int _interactionPromptGroup;
        private bool _interactionPromptArmed;
        private bool _opponentSpawnAttempted;
        private bool _opponentScenarioStarted;
        private bool _worldSetupComplete;
        private bool _sessionActive;
        private const string DealAnimationDictionary = "mini_games@poker_mg@deal";
        private const string PokerBaseAnimationDictionary = "mini_games@poker_mg@base";
        // Deal/pickup gestures play once. Idle hand poses loop while the cards are held.
        private const eScriptedAnimFlags DealAnimationFlags =
            eScriptedAnimFlags.Upperbody | eScriptedAnimFlags.Secondary;
        private const eScriptedAnimFlags IdleAnimationFlags =
            eScriptedAnimFlags.Looping | eScriptedAnimFlags.Upperbody | eScriptedAnimFlags.Secondary;

        private sealed class PendingPedAnimation
        {
            public int PedHandle;
            public string Dictionary;
            public string Clip;
            public int DurationMilliseconds;
            public eScriptedAnimFlags Flags;
            public DateTime ExpiresUtc;
        }

        private readonly Queue<PendingPedAnimation> _pendingPedAnimations =
            new Queue<PendingPedAnimation>();
        private bool _hasOriginalPlayerTransform;
        private Vector3 _originalPlayerPosition;
        private float _originalPlayerHeading;
        private DateTime _opponentScenarioStartedUtc;
        private DateTime _playerScenarioStartedUtc;
        private DateTime _nextSetupAttemptUtc;
        private DateTime _nextDeckPropAttemptUtc;

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
        public bool IsPlayerSeatedReady
        {
            get
            {
                return _sessionActive && _playerScenarioStartedUtc != DateTime.MinValue &&
                       (DateTime.UtcNow - _playerScenarioStartedUtc).TotalSeconds >=
                       PlayerSeatingWarmupSeconds;
            }
        }
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
            EnsureDeckProp();
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
            _pendingPedAnimations.Clear();

            // The Harmony filter remains harmless after shutdown because handle 0 is never
            // treated as the excluded ped. A later Truco script instance can set a new handle.
            _companionExcludedPedHandle = 0;

            ClearPhysicalHandCards();
            DeleteProp(ref _deckProp);
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
        /// <summary>
        /// Plays the appropriate deal gesture on the dealer, regardless of whether
        /// the dealer is the player or the NPC. 1to4 gives to the other seat;
        /// 2to1 deals to the dealer's own hand.
        /// </summary>
        public void PlayCardDealAnimation(bool playerDeals, bool dealingToOther)
        {
            Ped ped = playerDeals ? Game.Player.Character : _opponent;
            string clip = dealingToOther ? "1to4" : "2to1";
            QueuePedAnimation(ped, DealAnimationDictionary, clip, 450, DealAnimationFlags);
        }

        /// <summary>Neutral seated idle before the hand starts.</summary>
        public void PlayNoCardsIdle()
        {
            QueuePedAnimation(Game.Player.Character, PokerBaseAnimationDictionary,
                "no_cards_idle_a", -1, IdleAnimationFlags);
            QueuePedAnimation(_opponent, PokerBaseAnimationDictionary,
                "no_cards_idle_a", -1, IdleAnimationFlags);
        }

        /// <summary>Loop the poker hand-holding idle on both seated players.</summary>
        public void PlayHoldingCardsIdle()
        {
            QueuePedAnimation(Game.Player.Character, PokerBaseAnimationDictionary,
                "hold_cards_idle_a", -1, IdleAnimationFlags);
            QueuePedAnimation(_opponent, PokerBaseAnimationDictionary,
                "hold_cards_idle_a", -1, IdleAnimationFlags);
        }

        /// <summary>
        /// Displays the real 3D card objects in each seated player's left hand.
        /// The opponent's cards remain face down; the player's cards use the closest
        /// matching Valentine poker-card models available in the base game.
        /// </summary>
        public void SetPhysicalHandCards(
            IList<TrucoCard> playerCards, IList<TrucoCard> opponentCards)
        {
            Ped player = Game.Player.Character;
            SyncHandCardProps(_playerHandCardProps, player, playerCards, false);
            SyncHandCardProps(_opponentHandCardProps, _opponent, opponentCards, true);
        }

        /// <summary>Removes only the cards attached to the hands, leaving the table deck.</summary>
        public void ClearPhysicalHandCards()
        {
            DeleteHandCardProps(_playerHandCardProps);
            DeleteHandCardProps(_opponentHandCardProps);
        }

        /// <summary>
        /// Ends the table session without deleting or restarting the rival's seated scenario.
        /// </summary>
        public void CloseTableSession()
        {
            if (_sessionActive)
                EndSession();
            else
            {
                _pendingPedAnimations.Clear();
                ClearPhysicalHandCards();
                ClearOpponentSecondaryAnimation();
            }
        }

        private void SyncHandCardProps(
            List<Prop> currentProps, Ped ped, IList<TrucoCard> cards, bool faceDown)
        {
            int desiredCount = cards == null ? 0 : Math.Min(3, cards.Count);
            while (currentProps.Count > desiredCount)
            {
                int last = currentProps.Count - 1;
                DeleteHandCardProp(currentProps[last]);
                currentProps.RemoveAt(last);
            }

            if (desiredCount == 0)
                return;

            if (ped == null || !ped.Exists() || ped.IsDeadOrDying)
            {
                DeleteHandCardProps(currentProps);
                return;
            }

            int boneIndex = Function.Call<int>(
                (Hash)GetEntityBoneIndexByNameNative,
                ped.Handle,
                "SKEL_L_Finger13");
            if (boneIndex < 0)
            {
                DeleteHandCardProps(currentProps);
                return;
            }

            while (currentProps.Count < desiredCount)
                currentProps.Add(null);

            for (int i = 0; i < desiredCount; i++)
            {
                string modelName = faceDown
                    ? HiddenCardPropModelName
                    : GetPhysicalCardModelName(cards[i]);
                int modelHash = new Model(modelName).Hash;
                Prop existing = currentProps[i];
                bool alreadyAttached = false;
                try
                {
                    alreadyAttached = existing != null && existing.Exists() &&
                                      existing.Model.Hash == modelHash &&
                                      existing.IsAttachedTo(ped);
                }
                catch
                {
                    alreadyAttached = false;
                }

                if (alreadyAttached)
                    continue;

                DeleteHandCardProp(existing);
                currentProps[i] = CreateAttachedCardProp(modelName, ped, boneIndex, i);
            }
        }

        private Prop CreateAttachedCardProp(
            string modelName, Ped ped, int boneIndex, int cardIndex)
        {
            Prop prop = CreateVisualProp(modelName, ped.Position, Vector3.Zero, true, false);
            if (prop == null && !string.Equals(
                modelName, GenericCardPropModelName, StringComparison.Ordinal))
            {
                prop = CreateVisualProp(
                    GenericCardPropModelName, ped.Position, Vector3.Zero, true, false);
            }

            if (prop == null)
                return null;

            // Three cards fan across the left fingers, following the working poker prop layout.
            float offsetX = 0.040f + cardIndex * 0.010f;
            float offsetY = cardIndex * 0.0034f;
            float rotationX = -95.0f + cardIndex * 10.0f;
            float rotationY = cardIndex == 0 ? -20.0f : (cardIndex == 1 ? 0.0f : 20.0f);
            Vector3 offset = new Vector3(offsetX, offsetY, 0.0019f);
            Vector3 rotation = new Vector3(rotationX, rotationY, -150.0f);

            try
            {
                prop.AttachTo(ped, boneIndex, offset, rotation);
                if (prop.IsAttachedTo(ped))
                    return prop;
            }
            catch
            {
                // Delete the card if the skeleton does not accept this attachment.
            }

            DeleteHandCardProp(prop);
            return null;
        }

        private static string GetPhysicalCardModelName(TrucoCard card)
        {
            if (card == null)
                return HiddenCardPropModelName;

            string rank;
            switch (card.Rank)
            {
                case 1: rank = "a"; break;
                case 2: rank = "2"; break;
                case 3: rank = "3"; break;
                case 4: rank = "4"; break;
                case 5: rank = "5"; break;
                case 6: rank = "6"; break;
                case 7: rank = "7"; break;
                case 10: rank = "j"; break;
                case 11: rank = "q"; break;
                case 12: rank = "k"; break;
                default: return GenericCardPropModelName;
            }

            // The native Valentine card prop naming uses c/d/h/s, not full suit names.
            // Map the four Spanish suits to the closest traditional poker-suit artwork.
            string suit;
            switch (card.Suit)
            {
                case SpanishSuit.Bastos: suit = "c"; break;
                case SpanishSuit.Copas: suit = "h"; break;
                case SpanishSuit.Espadas: suit = "s"; break;
                case SpanishSuit.Oros: suit = "d"; break;
                default: return GenericCardPropModelName;
            }

            return "p_crd_" + rank + "_" + suit + "01x_val";
        }

        private void EnsureDeckProp()
        {
            if (_tableProp == null || !_tableProp.Exists() ||
                (_deckProp != null && _deckProp.Exists()) ||
                DateTime.UtcNow < _nextDeckPropAttemptUtc)
                return;

            _nextDeckPropAttemptUtc = DateTime.UtcNow.AddSeconds(SetupRetrySeconds);
            Vector3 deckPosition = _tableProp.GetOffsetPosition(
                new Vector3(0.18f, -0.18f, 0.72f));
            Vector3 deckRotation = new Vector3(0.0f, 0.0f, TableHeading - 15.0f);

            _deckProp = CreateVisualProp(
                DeckPropModelName, deckPosition, deckRotation, false, true);
            if (_deckProp == null)
            {
                _deckProp = CreateVisualProp(
                    DeckFallbackModelName, deckPosition, deckRotation, false, true);
            }
        }

        private static Prop CreateVisualProp(
            string modelName, Vector3 position, Vector3 rotation,
            bool dynamic, bool freeze)
        {
            try
            {
                Model model = new Model(modelName);
                if (!model.IsValid || !model.IsInCdImage || !model.Request(1200))
                    return null;

                Prop prop = World.CreatePropNoOffset(model, position, rotation, dynamic);
                if (prop == null || !prop.Exists())
                    return null;

                prop.IsMissionEntity = true;
                prop.HasCollision = false;
                ENTITY.FREEZE_ENTITY_POSITION(prop.Handle, freeze);
                return prop;
            }
            catch
            {
                return null;
            }
        }

        private static void DeleteHandCardProps(List<Prop> props)
        {
            for (int i = props.Count - 1; i >= 0; i--)
                DeleteHandCardProp(props[i]);
            props.Clear();
        }

        private static void DeleteHandCardProp(Prop prop)
        {
            try
            {
                if (prop != null && prop.Exists())
                {
                    if (prop.IsAttached)
                        prop.Detach();
                    prop.Delete();
                }
            }
            catch
            {
                // Keep cleanup safe when a card prop was already removed by the game.
            }
        }

        private void QueuePedAnimation(Ped ped, string dictionary, string clip,
            int durationMilliseconds, eScriptedAnimFlags flags)
        {
            if (ped == null || !ped.Exists() || ped.IsDeadOrDying)
                return;

            try
            {
                STREAMING.REQUEST_ANIM_DICT(dictionary);
                _pendingPedAnimations.Enqueue(new PendingPedAnimation
                {
                    PedHandle = ped.Handle,
                    Dictionary = dictionary,
                    Clip = clip,
                    DurationMilliseconds = durationMilliseconds,
                    Flags = flags,
                    ExpiresUtc = DateTime.UtcNow.AddSeconds(12.0)
                });
            }
            catch
            {
                // Missing dictionaries never interrupt the active seating scenario.
            }
        }

        private void ProcessPendingCardAnimation()
        {
            // StartScenario begins the chair-entry transition asynchronously. Keep every
            // queued poker animation (player and rival) behind the player's seating warm-up.
            if (_sessionActive && !IsPlayerSeatedReady)
                return;

            // Rotate requests whose dictionaries are still streaming instead of letting one
            // missing dictionary block an animation from a different dictionary.
            int queuedCount = _pendingPedAnimations.Count;
            for (int i = 0; i < queuedCount; i++)
            {
                PendingPedAnimation pending = _pendingPedAnimations.Dequeue();
                if (DateTime.UtcNow > pending.ExpiresUtc)
                    continue;

                try
                {
                    STREAMING.REQUEST_ANIM_DICT(pending.Dictionary);
                    if (!STREAMING.HAS_ANIM_DICT_LOADED(pending.Dictionary))
                    {
                        _pendingPedAnimations.Enqueue(pending);
                        continue;
                    }

                    Ped ped = null;
                    Ped player = Game.Player.Character;
                    if (player != null && player.Exists() && player.Handle == pending.PedHandle)
                        ped = player;
                    else if (_opponent != null && _opponent.Exists() &&
                             _opponent.Handle == pending.PedHandle)
                        ped = _opponent;

                    if (ped == null || !ped.Exists() || ped.IsDeadOrDying)
                        continue;

                    // Only start one clip per frame. Secondary keeps the table-seating scenario.
                    ped.Task.PlayAnimation(
                        pending.Dictionary,
                        pending.Clip,
                        8.0f,
                        -8.0f,
                        pending.DurationMilliseconds,
                        pending.Flags);
                    return;
                }
                catch
                {
                    // Unknown clips or streaming failures never break the hand or seating.
                }
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
                string promptText = Function.Call<string>(
                    (Hash)PromptVarString, 10, "LITERAL_STRING", "Jugar al truco");
                Function.Call((Hash)PromptSetText, prompt, promptText);
                HUD._UI_PROMPT_SET_STANDARDIZED_HOLD_MODE(prompt, StandardizedHoldMode);
                HUD._UI_PROMPT_SET_GROUP(prompt, group, 0);
                HUD._UI_PROMPT_REGISTER_END(prompt);
                HUD._UI_PROMPT_SET_VISIBLE(prompt, false);
                HUD._UI_PROMPT_SET_ENABLED(prompt, false);

                if (!Function.Call<bool>((Hash)PromptIsValid, prompt))
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
                _playerScenarioStartedUtc = DateTime.UtcNow;

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
                    Function.Call<bool>((Hash)IsEntityDead, ped) ||
                    Function.Call<bool>((Hash)IsEntityOnFire, ped) ||
                    Function.Call<bool>((Hash)IsPedInjured, ped) ||
                    Function.Call<bool>((Hash)IsPedDeadOrDying, ped, true) ||
                    Function.Call<bool>((Hash)IsPedFleeing, ped) ||
                    Function.Call<bool>((Hash)IsPedInCombat, ped, playerHandle) ||
                    Function.Call<bool>((Hash)IsPedShooting, ped) ||
                    Function.Call<bool>((Hash)IsPedFalling, ped) ||
                    Function.Call<bool>((Hash)IsPedBeingStunned, ped, 0) ||
                    Function.Call<bool>((Hash)IsPedRagdoll, ped) ||
                    Function.Call<bool>((Hash)IsPedHogtied, ped) ||
                    Function.Call<bool>((Hash)IsPedFallingOver, ped) ||
                    Function.Call<bool>((Hash)IsPedGettingUp, ped))
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
                    !Function.Call<bool>((Hash)PromptIsValid, _interactionPrompt))
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
                    Function.Call((Hash)PromptDelete, _interactionPrompt);
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
            _playerScenarioStartedUtc = DateTime.MinValue;
            _pendingPedAnimations.Clear();
            ClearPhysicalHandCards();
            ClearOpponentSecondaryAnimation();

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

        private void ClearOpponentSecondaryAnimation()
        {
            try
            {
                if (_opponent != null && _opponent.Exists() && !_opponent.IsDeadOrDying)
                {
                    Function.Call((Hash)ClearPedSecondaryTaskNative, _opponent.Handle);
                }
            }
            catch
            {
                // The native clears only the NPC's secondary animation task, not its seat scenario.
            }
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
