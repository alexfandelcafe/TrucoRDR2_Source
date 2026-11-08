using System;
using RDR2;
using RDR2.Math;
using RDR2.Native;
using RDR2.UI;

namespace TrucoRDR2
{
    /// <summary>
    /// One fixed world interaction in the Valentine saloon.
    /// Spawns a table and two chairs, then walks both characters to the seats before
    /// asking the game to run the sitting scenarios. The player is never teleported
    /// to the seat position.
    /// </summary>
    internal sealed class TrucoWorldInteraction
    {
        private static readonly Vector3 LocationCenter =
            new Vector3(-307.7149f, 802.8571f, 118.9794f);
        private static readonly Vector3 TablePosition = LocationCenter;
        private static readonly Vector3 PlayerSeat =
            new Vector3(-309.05f, 802.8571f, 118.9794f);
        private static readonly Vector3 OpponentSeat =
            new Vector3(-306.38f, 802.8571f, 118.9794f);

        // Furniture orientations are intentionally independent from character headings.
        // These are reversed relative to the previous build because the chair meshes
        // were facing away from the table even though the opponent's pose was correct.
        private const string TableModelName = "p_table01x";
        private const string ChairModelName = "p_chair02x";
        private const float TableHeading = 90.0f;
        private const float PlayerChairHeading = 90.0f;
        private const float OpponentChairHeading = 270.0f;

        // Keep character headings that were reported to look correct.
        private const float PlayerHeading = 270.0f;
        private const float OpponentHeading = 90.0f;

        private const float InteractionRadius = 3.2f;
        private const float WalkSpeed = 1.0f;
        private const float ApproachDistance = 0.85f;
        private const double MinimumWalkSeconds = 0.8;
        private const double WalkTimeoutSeconds = 7.0;
        private const double SeatPreparationSeconds = 2.0;
        private const double PromptIntervalSeconds = 2.5;

        private readonly uint _playerSeatScenario = Game.Joaat("PROP_HUMAN_SEAT_CHAIR");
        private readonly uint _opponentSeatScenario = Game.Joaat("PROP_HUMAN_SEAT_CHAIR");

        private Vector3 _playerSeatPosition;
        private Vector3 _opponentSeatPosition;
        private Vector3 _playerApproachPosition;
        private Vector3 _opponentApproachPosition;
        private Vector3 _opponentSpawnPosition;
        private Prop _tableProp;
        private Prop _playerChairProp;
        private Prop _opponentChairProp;
        private Ped _opponent;
        private Vector3 _originalPlayerPosition;
        private float _originalPlayerHeading;
        private bool _hasOriginalPlayerTransform;
        private bool _sessionActive;
        private bool _preparing;
        private bool _walkingToSeats;
        private DateTime _preparationStartedUtc;
        private DateTime _nextPromptUtc;

        public bool IsNearSaloon { get; private set; }
        public bool IsPreparing { get { return _preparing; } }
        public bool IsSessionActive { get { return _sessionActive; } }
        public string LastError { get; private set; }

        /// <summary>
        /// Returns true when the characters have had time to approach and sit,
        /// indicating that the existing truco UI may be opened.
        /// </summary>
        public bool Update(bool tableVisible)
        {
            if (_sessionActive)
            {
                if (_preparing)
                {
                    DisableControlsDuringPreparation();

                    if (_walkingToSeats)
                    {
                        bool walkTimedOut =
                            (DateTime.UtcNow - _preparationStartedUtc).TotalSeconds >= WalkTimeoutSeconds;
                        bool bothAtApproach = AreBothCharactersNearApproach();
                        bool minimumWalkElapsed =
                            (DateTime.UtcNow - _preparationStartedUtc).TotalSeconds >= MinimumWalkSeconds;

                        if (walkTimedOut || (bothAtApproach && minimumWalkElapsed))
                        {
                            try
                            {
                                BeginSittingScenarios();
                                _walkingToSeats = false;
                                _preparationStartedUtc = DateTime.UtcNow;
                            }
                            catch (Exception ex)
                            {
                                LastError = ex.Message;
                                EndSession();
                                Screen.PrintSubtitle("No se pudieron iniciar las animaciones de sentado. Revisa scripts\\TrucoRDR2.log.");
                            }
                        }

                        return false;
                    }

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

            Vector3 playerPosition = player.Position;
            if (!IsInsideInteractionRange(playerPosition))
                return false;

            if (player.IsOnMount || player.IsInCombat)
            {
                Screen.PrintSubtitle("Bajate del caballo y alejate del combate para jugar al truco.");
                return false;
            }

            try
            {
                _originalPlayerPosition = playerPosition;
                _originalPlayerHeading = player.Heading;
                _hasOriginalPlayerTransform = true;

                // Use the world floor at the interaction point for furniture, then derive
                // each seat anchor from the actual spawned chair transform. The chair model
                // origin is at floor level; the scenario anchor sits above it at seat height.
                float floorZ = playerPosition.Z;
                Vector3 tableSpawn = new Vector3(TablePosition.X, TablePosition.Y, floorZ);
                Vector3 playerChairSpawn = new Vector3(PlayerSeat.X, PlayerSeat.Y, floorZ);
                Vector3 opponentChairSpawn = new Vector3(OpponentSeat.X, OpponentSeat.Y, floorZ);

                // Spawn furniture independently from the seating coordinates. The mesh origin
                // is not a safe height for a character's feet, especially for interior props.
                _tableProp = CreateFurnitureProp(TableModelName, tableSpawn, TableHeading);
                _playerChairProp = CreateFurnitureProp(ChairModelName, playerChairSpawn, PlayerChairHeading);
                _opponentChairProp = CreateFurnitureProp(ChairModelName, opponentChairSpawn, OpponentChairHeading);

                // A chair's scenario point is not its mesh origin. Use a local position
                // near the seat surface, transformed by the chair's own rotation.
                _playerSeatPosition = _playerChairProp.GetOffsetPosition(new Vector3(0.0f, -0.05f, 0.50f));
                _opponentSeatPosition = _opponentChairProp.GetOffsetPosition(new Vector3(0.0f, -0.05f, 0.50f));
                _playerApproachPosition = _playerChairProp.GetOffsetPosition(new Vector3(0.0f, -0.85f, 0.0f));
                _opponentApproachPosition = _opponentChairProp.GetOffsetPosition(new Vector3(0.0f, -0.85f, 0.0f));
                _opponentSpawnPosition = new Vector3(OpponentSeat.X + 1.45f, OpponentSeat.Y + 0.25f, floorZ);

                // Spawn the rival nearby and let navigation move him to the approach point.
                _opponent = World.CreatePed(
                    PedHash.a_m_m_valtownfolk_01, _opponentSpawnPosition, OpponentHeading);
                if (_opponent == null || !_opponent.Exists())
                    throw new InvalidOperationException("No se pudo crear al rival en el salón de Valentine.");

                _opponent.BlockPermanentEvents = true;
                _opponent.NeverLeavesGroup = true;
                _opponent.Heading = OpponentHeading;
                _opponent.Task.GoTo(_opponentApproachPosition, WalkSpeed, 7000);

                // Do not set player.Position or PositionNoOffset here. Give control to the
                // navigation task so the player takes actual steps toward the chair.
                player.Task.ClearTasks(true);
                player.Task.GoTo(_playerApproachPosition, WalkSpeed, 7000);

                _sessionActive = true;
                _preparing = true;
                _walkingToSeats = true;
                _preparationStartedUtc = DateTime.UtcNow;
                IsNearSaloon = false;
                Screen.PrintSubtitle("Acercándose a los asientos...");
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

        private void BeginSittingScenarios()
        {
            Ped player = Game.Player.Character;
            if (player == null || !player.Exists() || _opponent == null || !_opponent.Exists())
                throw new InvalidOperationException("Uno de los personajes dejó de estar disponible al llegar a la mesa.");

            // Character facing is not the same setting as the chair mesh heading.
            // Ask the game to approach the scenario position instead of teleporting there.
            player.Heading = PlayerHeading;
            _opponent.Heading = OpponentHeading;

            // TaskInvoker.StartScenario parameters are (teleportToScenario, isSittingScenario).
            // The player-specific scenario is important for the player ped; do not teleport
            // either character when the scenario starts because they have already walked there.
            player.Task.StartScenario(
                _playerSeatScenario, _playerSeatPosition, -1, false, true);
            _opponent.Task.StartScenario(
                _opponentSeatScenario, _opponentSeatPosition, -1, false, true);
        }

        private bool AreBothCharactersNearApproach()
        {
            try
            {
                Ped player = Game.Player.Character;
                if (player == null || !player.Exists() || _opponent == null || !_opponent.Exists())
                    return false;

                return DistanceSquared(player.Position, _playerApproachPosition) <= ApproachDistance * ApproachDistance &&
                       DistanceSquared(_opponent.Position, _opponentApproachPosition) <= ApproachDistance * ApproachDistance;
            }
            catch
            {
                return false;
            }
        }

        private static float DistanceSquared(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            float dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
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

            // Also ask the engine to settle the object's collision against the local floor.
            // Never use this adjusted object origin as a character's seat Z coordinate.
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
                // The task timeout will still let the interaction continue if suppression fails.
            }
        }

        private void EndSession()
        {
            _preparing = false;
            _sessionActive = false;
            _walkingToSeats = false;

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
