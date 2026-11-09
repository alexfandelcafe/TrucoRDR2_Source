using System;

namespace TrucoRDR2
{
    /// <summary>Tracks a complete match to 15 or 30 points and alternates the mano.</summary>
    public sealed class TrucoMatch
    {
        private readonly Random _random;
        private readonly bool _florEnabled;

        public int TargetPoints { get; private set; }
        public int PlayerScore { get; private set; }
        public int OpponentScore { get; private set; }
        public TrucoPlayer Mano { get; private set; }
        public TrucoRound Round { get; private set; }
        public bool IsComplete { get; private set; }
        public TrucoPlayer? Winner { get; private set; }
        public string LastScoreEvent { get; private set; }

        public TrucoMatch(int targetPoints, bool florEnabled, Random random)
        {
            if (targetPoints != 15 && targetPoints != 30)
                throw new ArgumentOutOfRangeException("targetPoints");
            if (random == null) throw new ArgumentNullException("random");

            TargetPoints = targetPoints;
            _florEnabled = florEnabled;
            _random = random;
            Mano = TrucoPlayer.Player;
            StartRound();
        }

        public void StartNextRound()
        {
            if (IsComplete) throw new InvalidOperationException("La partida ya terminó.");
            if (Round == null || !Round.IsComplete)
                throw new InvalidOperationException("La mano actual todavía no terminó.");

            Mano = Mano == TrucoPlayer.Player ? TrucoPlayer.Opponent : TrucoPlayer.Player;
            StartRound();
        }

        public void Restart()
        {
            PlayerScore = 0;
            OpponentScore = 0;
            IsComplete = false;
            Winner = null;
            LastScoreEvent = "Nueva partida.";
            Mano = TrucoPlayer.Player;
            StartRound();
        }

        public string ScorePhase
        {
            get
            {
                if (TargetPoints == 15) return "PARTIDA A 15";
                return Math.Max(PlayerScore, OpponentScore) >= 15 ? "BUENAS" : "MALAS";
            }
        }

        private void StartRound()
        {
            Round = new TrucoRound(Mano, _random, TargetPoints, PlayerScore, OpponentScore, _florEnabled);
            Round.PointsAwarded += OnPointsAwarded;
            Round.Initialize();
        }

        private void OnPointsAwarded(TrucoPlayer player, int points, string reason)
        {
            if (player == TrucoPlayer.Player)
                PlayerScore += points;
            else
                OpponentScore += points;

            LastScoreEvent = (player == TrucoPlayer.Player ? "Vos" : "El rival") +
                             " sumás " + points + " punto(s): " + reason + ".";

            if (PlayerScore >= TargetPoints)
            {
                IsComplete = true;
                Winner = TrucoPlayer.Player;
            }
            else if (OpponentScore >= TargetPoints)
            {
                IsComplete = true;
                Winner = TrucoPlayer.Opponent;
            }
        }
    }
}