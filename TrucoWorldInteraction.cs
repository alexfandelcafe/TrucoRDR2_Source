using System;
using RDR2;
using RDR2.Math;
using RDR2.Native;
using RDR2.UI;

namespace TrucoRDR2
{
    /// <summary>
    /// World interaction prototype for one fixed table in the Valentine saloon.
    /// Spawns a real table and two chairs before seating the player and the opponent.
    /// The exact placement should be calibrated in-game.
    /// </summary>
    internal sealed class TrucoWorldInteraction
    {
        // Valentine Saloon interaction point. Furniture and seat offsets are kept here
        // so their placement can be calibrated without changing the match/UI code.
        private static readonly Vector3 LocationCenter =
            new Vector3(-307.7149f, 802.8571f, 118.9794f);
        private static readonly Vector3 TablePosition = LocationCenter;
        private static readonly Vector3 PlayerSeat =
            new Vector3(-309.05f, 802.8571f, 118.9794f);
        private static readonly Vector3 OpponentSeat =
            new Vector3(-306.38f, 802.8571f, 118.9794f);

        // Valid RDR2 object model names: a plain wooden table and two matching chairs.
        private const string TableModelName = "p_table01x";
        private const string ChairModelName = "p_chair02x";

        private const float TableHeading = 90.0f;
        private const float PlayerHeading = 270.0f;
        private const float OpponentHeading = 90.0f;
        private const float InteractionRadius = 3.2f;
        private const double SeatPreparationSeconds = 1.8;
        private const double PlayerScenarioFallbackSeconds = 0.75;
        private const double PromptIntervalSeconds = 2.5;

        private readonly uint _playerSeatScenario = Game.Joaat("PROP_PLAYER_DYNAMIC_SEAT_CHAIR_TABLE");
        private readonly uint _genericSeatScenario = Game.Joaat("GENERIC_SEAT_CHAIR_TABLE_SCENARIO");

        private Vector3 _playerSeatPosition = PlayerSeat;
        private Vector3 _opponentSeatPosition = OpponentSeat;
        private Prop _tableProp;
        private Prop _playerChairProp;
        private Prop _opponentChairProp;
        private Ped _opponent;
        private Vector3 _originalPlayerPosition;
        private float _originalPlayerHeading;
        private bool _hasOriginalPlayerTransform;
        private bool _sessionActive;
        private bool _preparing;
        private bool _playerScenarioFallbackAttempted;
        private DateTime _preparationStartedUtc;
        private DateTime _nextPromptUtc;

        public bool IsNearSaloon { get; private set; }
        public bool IsPreparing { get { return _preparing; } }
        public bool IsSessionActive { get { return _sessionActive; } }
        public string LastError { get; private set; }

        /// <summary>
        /// Updates the proximity prompt and any pending seat preparation.
        /// Returns true once the seating delay has elapsed and the table UI should open.
        /// </summary>
        public bool Update(bool tableVisible)
        {
            if (_sessionActive)
            {
                if (_preparing)
                {
                    DisableControlsDuringPreparation();
                    TryPlayerScenarioFallback();

                    if ((DateTime.UtcNow - _preparationStartedUtc).TotalSeconds >= SeatPreparationSeconds)
                    {
                        _preparing = false;
                        return true;
                    }

                    return false;
                }

                if (!tableVisible)
                    EndSession();

                IsNearSaloon = false;
                return false;
            }

            if (tableVisible)
            {
                IsNearSaloon = false;
                return false;
            }

            UpdateProximityPrompt();
            return false;
        }

        public bool TryBegin()
        {
            LastError = null;
            if (_sessionActive || _preparing)
                return false;

            Ped player = Game.Player.Character;
            if (player == null || !player.Exists() || player.IsDeadOrDying)
                return false;

            Vector3 position = player.Position;
            if (!IsInsideInteractionRange(position))
                return false;

            if (player.IsOnMount || player.IsInCombat)
            {
                Screen.PrintSubtitle("Bajate del caballo y alejate del combate para jugar al truco.");
                return false;
            }

            try
            {
                _originalPlayerPosition = player.Position;
                _originalPlayerHeading = player.Heading;
                _hasOriginalPlayerTransform = true;

                // Create real world furniture first. Do not start any sitting animation
                // unless all three props exist, otherwise the characters can appear to sit
                // in mid-air as in the previous prototype.
                _tableProp = CreateFurnitureProp(
                    TableModelName, TablePosition, TableHeading);
                _playerChairProp = CreateFurnitureProp(
                    ChairModelName, PlayerSeat, PlayerHeading);
                _opponentChairProp = CreateFurnitureProp(
                    ChairModelName, OpponentSeat, OpponentHeading);

                // PLACE_ENTITY_ON_GROUND_PROPERLY may adjust each prop's Z coordinate.
                // Use the grounded chair coordinates for the seating scenarios as well.
                Vector3 playerChairPosition = _playerChairProp.Position;
                Vector3 opponentChairPosition = _opponentChairProp.Position;
                _playerSeatPosition = new Vector3(PlayerSeat.X, PlayerSeat.Y, playerChairPosition.Z);
                _opponentSeatPosition = new Vector3(OpponentSeat.X, OpponentSeat.Y, opponentChairPosition.Z);

                // Spawn a dedicated opponent for this location rather than taking control
                // of an ambient citizen. It is removed again when the player leaves the table.
                _opponent = World.CreatePed(PedHash.a_m_m_valtownfolk_01, _opponentSeatPosition, OpponentHeading);
                if (_opponent == null || !_opponent.Exists())
                    throw new InvalidOperationException("No se pudo crear al rival en el salón de Valentine.");

                _opponent.BlockPermanentEvents = true;
                _opponent.NeverLeavesGroup = true;
                _opponent.Heading = OpponentHeading;
                _opponent.Task.StartScenario(
                    _genericSeatScenario, _opponentSeatPosition, -1, true, true);

                player.Task.ClearTasks(true);
                player.PositionNoOffset = _playerSeatPosition;
                player.Heading = PlayerHeading;
                player.Task.StartScenario(
                    _playerSeatScenario, _playerSeatPosition, -1, true, true);

                _sessionActive = true;
                _preparing = true;
                _playerScenarioFallbackAttempted = false;
                _preparationStartedUtc = DateTime.UtcNow;
                IsNearSaloon = false;
                Screen.PrintSubtitle("Preparando la mesa y tomando asiento...");
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                EndSession();
                Screen.PrintSubtitle("No se pudo preparar la mesa de truco. Revisa scripts\\TrucoRDR2.log.");
                return false;
            }
        }

        public void CancelPreparation()
        {
            if (!_preparing)
                return;

            EndSession();
            Screen.PrintSubtitle("Se canceló la partida de truco.");
        }

        public void Dispose()
        {
            EndSession();
        }

        private static Prop CreateFurnitureProp(string modelName, Vector3 position, float heading)
        {
            Model model = new Model(Game.Joaat(modelName));
            Prop prop = World.CreatePropNoOffset(
                model,
                position,
                new Vector3(0.0f, 0.0f, heading),
                false);

            if (prop == null || !prop.Exists())
                throw new InvalidOperationException(
                    "No se pudo crear el objeto '" + modelName + "'. Comprueba el modelo y la ubicación.");

            prop.IsMissionEntity = true;

            // Props spawned inside an interior do not reliably settle just from their input Z.
            // Snap their collision bounds to the actual floor before using their position for seating.
            ENTITY.PLACE_ENTITY_ON_GROUND_PROPERLY(prop.Handle, true);
            return prop;
        }

        private void UpdateProximityPrompt()
        {
            try
            {
                Ped player = Game.Player.Character;
                if (player == null || !player.Exists() || player.IsDeadOrDying)
                {
                    IsNearSaloon = false;
                    return;
                }

                IsNearSaloon = IsInsideInteractionRange(player.Position);
                if (IsNearSaloon && DateTime.UtcNow >= _nextPromptUtc)
                {
                    Screen.PrintSubtitle("Salón de Valentine: pulsa E para crear la mesa y jugar al truco.");
                    _nextPromptUtc = DateTime.UtcNow.AddSeconds(PromptIntervalSeconds);
                }
            }
            catch
            {
                IsNearSaloon = false;
            }
        }

        private static bool IsInsideInteractionRange(Vector3 position)
        {
            float dx = position.X - LocationCenter.X;
            float dy = position.Y - LocationCenter.Y;
            float dz = position.Z - LocationCenter.Z;
            return Math.Abs(dz) <= 3.0f &&
                   dx * dx + dy * dy + dz * dz <= InteractionRadius * InteractionRadius;
        }

        private static bool IsWithinSessionArea(Vector3 position)
        {
            float dx = position.X - LocationCenter.X;
            float dy = position.Y - LocationCenter.Y;
            float dz = position.Z - LocationCenter.Z;
            return Math.Abs(dz) <= 6.0f && dx * dx + dy * dy + dz * dz <= 144.0f;
        }

        private void DisableControlsDuringPreparation()
        {
            try
            {
                PAD.DISABLE_ALL_CONTROL_ACTIONS(0);
                PAD.DISABLE_ALL_CONTROL_ACTIONS(1);
                PAD.DISABLE_ALL_CONTROL_ACTIONS(2);
            }
            catch
            {
                // The preparation timeout still lets the player continue if control suppression fails.
            }
        }

        private void TryPlayerScenarioFallback()
        {
            if (_playerScenarioFallbackAttempted ||
                (DateTime.UtcNow - _preparationStartedUtc).TotalSeconds < PlayerScenarioFallbackSeconds)
                return;

            _playerScenarioFallbackAttempted = true;
            try
            {
                Ped player = Game.Player.Character;
                if (player != null && player.Exists() && !player.IsUsingScenario)
                {
                    // Some chair models expose only the generic scenario node. Try the
                    // generic chair/table scenario once if the player-specific one did not start.
                    player.Task.StartScenario(
                        _genericSeatScenario, _playerSeatPosition, -1, true, true);
                }
            }
            catch
            {
                // Continue to the existing table UI even if the sitting animation is unavailable.
            }
        }

        private void EndSession()
        {
            _preparing = false;
            _sessionActive = false;
            _playerScenarioFallbackAttempted = false;

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
                // Cleanup of furniture and the opponent still proceeds if the player ped is unavailable.
            }

            _hasOriginalPlayerTransform = false;

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
                // An already unloaded/deleted ped does not block session cleanup.
            }

            _opponent = null;
            IsNearSaloon = false;
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
                // Keep deleting the remaining props even if one object is already invalid.
            }
            finally
            {
                prop = null;
            }
        }
    }
}
