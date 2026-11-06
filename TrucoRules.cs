using System;
using System.Collections.Generic;

namespace TrucoRDR2
{
    /// <summary>Pure rules helpers; deliberately independent from ScriptHook/game objects.</summary>
    public static class TrucoRules
    {
        /// <summary>Returns the Argentine truco strength, from 1 (weakest) to 14 (strongest).</summary>
        public static int TrucoPower(TrucoCard card)
        {
            if (card == null) throw new ArgumentNullException("card");

            if (card.Rank == 1 && card.Suit == SpanishSuit.Espadas) return 14; // ancho de espadas
            if (card.Rank == 1 && card.Suit == SpanishSuit.Bastos) return 13;  // ancho de bastos
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
            return 1; // 4
        }

        /// <summary>
        /// Calculates the player's envido points for a three-card hand.
        /// It returns the best pair in one suit (20 + card values), or the highest value if no pair shares a suit.
        /// Face cards 10, 11 and 12 count as zero.
        /// </summary>
        public static int EnvidoPoints(IList<TrucoCard> hand)
        {
            if (hand == null) throw new ArgumentNullException("hand");
            if (hand.Count == 0) return 0;

            int best = 0;
            for (int i = 0; i < hand.Count; i++)
            {
                for (int j = i + 1; j < hand.Count; j++)
                {
                    if (hand[i].Suit == hand[j].Suit)
                    {
                        int candidate = 20 + EnvidoCardValue(hand[i].Rank) + EnvidoCardValue(hand[j].Rank);
                        if (candidate > best) best = candidate;
                    }
                }
            }

            if (best > 0) return best;

            for (int i = 0; i < hand.Count; i++)
            {
                int value = EnvidoCardValue(hand[i].Rank);
                if (value > best) best = value;
            }

            return best;
        }

        private static int EnvidoCardValue(int rank)
        {
            return rank >= 10 ? 0 : rank;
        }
    }
}
