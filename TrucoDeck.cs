using System;
using System.Collections.Generic;

namespace TrucoRDR2
{
    /// <summary>Creates and shuffles the 40-card Spanish deck used by Argentine truco.</summary>
    public static class TrucoDeck
    {
        private static readonly int[] Ranks = { 1, 2, 3, 4, 5, 6, 7, 10, 11, 12 };

        public static List<TrucoCard> CreateShuffled(Random random)
        {
            if (random == null)
                throw new ArgumentNullException("random");

            List<TrucoCard> cards = new List<TrucoCard>(40);
            foreach (int rank in Ranks)
            {
                foreach (SpanishSuit suit in Enum.GetValues(typeof(SpanishSuit)))
                    cards.Add(new TrucoCard(rank, suit));
            }

            // Fisher-Yates: every permutation is equally likely when Random is uniform.
            for (int i = cards.Count - 1; i > 0; i--)
            {
                int swapIndex = random.Next(i + 1);
                TrucoCard temporary = cards[i];
                cards[i] = cards[swapIndex];
                cards[swapIndex] = temporary;
            }

            return cards;
        }

        public static List<TrucoCard> CreateShuffled()
        {
            return CreateShuffled(new Random());
        }
    }
}
