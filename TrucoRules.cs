using System;
using System.Collections.Generic;

namespace TrucoRDR2
{
    /// <summary>Pure rules helpers; independent from ScriptHook and game objects.</summary>
    public static class TrucoRules
    {
        /// <summary>Returns Argentine truco strength from 1 (weakest) to 14 (strongest).</summary>
        public static int TrucoPower(TrucoCard card)
        {
            if (card == null) throw new ArgumentNullException("card");

            if (card.Rank == 1 && card.Suit == SpanishSuit.Espadas) return 14;
            if (card.Rank == 1 && card.Suit == SpanishSuit.Bastos) return 13;
            if (card.Rank == 7 && card.Suit == SpanishSuit.Espadas) return 12;
            if (card.Rank == 7 && card.Suit == SpanishSuit.Oros) return 11;
            if (card.Rank == 3) return 10;
            if (card.Rank == 2) return 9;
            if (card.Rank == 1) return 8;
            if (card.Rank == 12) return 7;
            if (card.Rank == 11) return 6;
            if (card.Rank == 10) return 5;
            if (card.Rank == 7) return 4;
            if (card.Rank == 6) return 3;
            if (card.Rank == 5) return 2;
            return 1;
        }

        /// <summary>
        /// Calculates envido for the best two cards of one suit. If no suit is paired,
        /// the highest single card value is used. Figures (10, 11, 12) count as zero.
        /// </summary>
        public static int EnvidoPoints(IList<TrucoCard> hand)
        {
            if (hand == null) throw new ArgumentNullException("hand");
            if (hand.Count == 0) return 0;

            int best = 0;
            for (int i = 0; i < hand.Count; i++)
            {
                if (hand[i] == null) continue;
                for (int j = i + 1; j < hand.Count; j++)
                {
                    if (hand[j] == null || hand[i].Suit != hand[j].Suit) continue;

                    int candidate = 20 + EnvidoCardValue(hand[i].Rank) + EnvidoCardValue(hand[j].Rank);
                    if (candidate > best) best = candidate;
                }
            }

            if (best > 0) return best;

            for (int i = 0; i < hand.Count; i++)
            {
                if (hand[i] == null) continue;
                int value = EnvidoCardValue(hand[i].Rank);
                if (value > best) best = value;
            }

            return best;
        }

        public static bool HasFlor(IList<TrucoCard> hand)
        {
            if (hand == null) throw new ArgumentNullException("hand");
            if (hand.Count != 3 || hand[0] == null || hand[1] == null || hand[2] == null)
                return false;

            return hand[0].Suit == hand[1].Suit && hand[1].Suit == hand[2].Suit;
        }

        /// <summary>Flor is 20 plus the envido values of all three cards of the same suit.</summary>
        public static int FlorPoints(IList<TrucoCard> hand)
        {
            if (!HasFlor(hand)) return 0;
            return 20 +
                   EnvidoCardValue(hand[0].Rank) +
                   EnvidoCardValue(hand[1].Rank) +
                   EnvidoCardValue(hand[2].Rank);
        }

        public static TrucoPlayer CompareEnvido(IList<TrucoCard> player, IList<TrucoCard> opponent, TrucoPlayer mano)
        {
            int playerPoints = EnvidoPoints(player);
            int opponentPoints = EnvidoPoints(opponent);
            if (playerPoints > opponentPoints) return TrucoPlayer.Player;
            if (opponentPoints > playerPoints) return TrucoPlayer.Opponent;
            return mano;
        }

        public static TrucoPlayer CompareFlor(IList<TrucoCard> player, IList<TrucoCard> opponent, TrucoPlayer mano)
        {
            int playerPoints = FlorPoints(player);
            int opponentPoints = FlorPoints(opponent);
            if (playerPoints > opponentPoints) return TrucoPlayer.Player;
            if (opponentPoints > playerPoints) return TrucoPlayer.Opponent;
            return mano;
        }

        public static int EnvidoCardValue(int rank)
        {
            return rank >= 10 ? 0 : rank;
        }
    }
}