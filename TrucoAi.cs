using System;
using System.Collections.Generic;

namespace TrucoRDR2
{
    /// <summary>
    /// Deterministic, testable decision policy for the single-player opponent.
    /// It uses only the opponent's own cards, public trick state and current score.
    /// </summary>
    internal static class TrucoAi
    {
        public static TrucoCall? ChooseEnvidoCall(int points, int ownScore, int otherScore, int targetPoints)
        {
            if (points < 29)
                return null;

            bool matchIsClose = Math.Max(ownScore, otherScore) >= targetPoints - 5;
            if (points >= 32 && matchIsClose)
                return TrucoCall.FaltaEnvido;
            if (points >= 32)
                return TrucoCall.RealEnvido;
            return TrucoCall.Envido;
        }

        public static bool ShouldCallTruco(int handStrength, int ownScore, int otherScore, int targetPoints)
        {
            int threshold = 28;
            int difference = ownScore - otherScore;
            if (difference <= -5)
                threshold -= 3;
            else if (difference >= 5)
                threshold += 3;

            if (ownScore >= targetPoints - 3 && ownScore > otherScore)
                threshold += 3;
            else if (otherScore >= targetPoints - 3 && ownScore < otherScore)
                threshold -= 1;

            return handStrength >= threshold;
        }

        public static bool ShouldFold(int handStrength, int stake, int ownScore, int otherScore, int targetPoints)
        {
            if (stake < 2 || handStrength > 10)
                return false;

            // A player who is behind should preserve the chance to recover the hand.
            // Conceding is reserved for a weak hand while protecting a meaningful lead.
            return ownScore >= otherScore + 4 && ownScore >= targetPoints - 8;
        }

        public static bool ShouldAcceptEnvido(int points, int pendingValue,
            int ownScore, int otherScore, int targetPoints)
        {
            int threshold = pendingValue <= 2 ? 23 : pendingValue <= 5 ? 28 : 31;
            int difference = ownScore - otherScore;

            if (difference <= -5)
                threshold -= 3;
            else if (difference >= 5)
                threshold += 2;

            if (ownScore >= targetPoints - 3 && ownScore > otherScore)
                threshold += 2;

            threshold = Math.Max(18, Math.Min(33, threshold));
            return points >= threshold;
        }

        public static bool ShouldAcceptFlor(int florPoints, int pendingValue,
            int ownScore, int otherScore, int targetPoints)
        {
            int threshold = pendingValue >= targetPoints - Math.Max(ownScore, otherScore) ? 31 : 28;
            if (ownScore < otherScore - 4)
                threshold -= 2;
            else if (ownScore > otherScore + 4)
                threshold += 2;
            return florPoints >= threshold;
        }

        public static bool ShouldRaiseEnvido(int points, TrucoCall pending)
        {
            if (pending == TrucoCall.Envido)
                return points >= 31;
            if (pending == TrucoCall.RealEnvido)
                return points >= 33;
            return false;
        }

        public static bool ShouldRaiseFlor(int florPoints, TrucoCall pending)
        {
            if (pending == TrucoCall.Flor)
                return florPoints >= 30;
            if (pending == TrucoCall.ContraFlor)
                return florPoints >= 32;
            return false;
        }

        public static bool ShouldRaiseTruco(int handStrength, int ownScore, int otherScore, int targetPoints)
        {
            int threshold = 34;
            if (ownScore < otherScore - 4)
                threshold -= 3;
            else if (ownScore > otherScore + 4)
                threshold += 2;

            if (ownScore >= targetPoints - 3 && ownScore > otherScore)
                threshold += 2;
            return handStrength >= threshold;
        }

        public static bool ShouldAcceptTruco(int handStrength, TrucoCall pending,
            int ownScore, int otherScore, int targetPoints)
        {
            int threshold = pending == TrucoCall.Truco ? 21 :
                            pending == TrucoCall.Retruco ? 29 : 36;
            int difference = ownScore - otherScore;

            if (difference <= -5)
                threshold -= 3;
            else if (difference >= 5)
                threshold += 2;

            if (ownScore >= targetPoints - 3 && ownScore > otherScore)
                threshold += 2;

            threshold = Math.Max(16, Math.Min(40, threshold));
            return handStrength >= threshold;
        }

        public static int ChooseCard(TrucoRound round, IList<TrucoCard> cards)
        {
            if (round == null)
                throw new ArgumentNullException("round");
            if (cards == null || cards.Count == 0)
                throw new ArgumentException("La mano no tiene cartas.", "cards");

            int weakestIndex = 0;
            int weakestPower = int.MaxValue;
            int strongestIndex = 0;
            int strongestPower = int.MinValue;
            int winningIndex = -1;
            int winningPower = int.MaxValue;
            int tyingIndex = -1;
            int tyingPower = int.MaxValue;

            int pendingPower = round.PendingCard == null
                ? int.MinValue
                : TrucoRules.TrucoPower(round.PendingCard);

            for (int i = 0; i < cards.Count; i++)
            {
                int power = TrucoRules.TrucoPower(cards[i]);
                if (power < weakestPower)
                {
                    weakestPower = power;
                    weakestIndex = i;
                }

                if (power > strongestPower)
                {
                    strongestPower = power;
                    strongestIndex = i;
                }

                if (round.PendingCard != null)
                {
                    if (power > pendingPower && power < winningPower)
                    {
                        winningPower = power;
                        winningIndex = i;
                    }
                    else if (power == pendingPower && power < tyingPower)
                    {
                        tyingPower = power;
                        tyingIndex = i;
                    }
                }
            }

            if (round.PendingCard != null)
            {
                if (WouldPardaWinHand(round))
                    if (tyingIndex >= 0)
                        return tyingIndex;

                return winningIndex >= 0 ? winningIndex : weakestIndex;
            }

            return MustWinNextTrick(round) ? strongestIndex : weakestIndex;
        }

        private static bool WouldPardaWinHand(TrucoRound round)
        {
            if (round.Tricks.Count == 1)
                return round.Tricks[0].Winner == TrucoPlayer.Opponent;

            if (round.Tricks.Count == 2)
            {
                TrucoTrick first = round.Tricks[0];
                TrucoTrick second = round.Tricks[1];

                if (first.Winner == TrucoPlayer.Opponent)
                    return true;

                return first.IsParda && second.IsParda && round.Mano == TrucoPlayer.Opponent;
            }

            return false;
        }

        private static bool MustWinNextTrick(TrucoRound round)
        {
            if (round.Tricks.Count < 2)
                return false;

            TrucoTrick first = round.Tricks[0];
            TrucoTrick second = round.Tricks[1];

            if (first.Winner == TrucoPlayer.Player && second.Winner == TrucoPlayer.Opponent)
                return true;

            return first.IsParda && second.IsParda && round.Mano != TrucoPlayer.Opponent;
        }
    }
}