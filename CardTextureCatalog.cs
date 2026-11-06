using System.Collections.Generic;
using System.Globalization;

namespace TrucoRDR2
{
    /// <summary>
    /// Texture names mirror the PNG filenames in mcmd/playingcards.io-spanish.playing.cards/img,
    /// excluding the .png extension. Ranks 8 and 9 are intentionally excluded from an Argentine truco deck.
    /// </summary>
    internal static class CardTextureCatalog
    {
        private static readonly int[] TrucoRanks = { 1, 2, 3, 4, 5, 6, 7, 10, 11, 12 };
        private static readonly string[] Suits = { "bastos", "copas", "espadas", "oros" };

        public static string[] CreateTrucoDeckTextureNames()
        {
            List<string> names = new List<string>(40);
            foreach (int rank in TrucoRanks)
            {
                foreach (string suit in Suits)
                {
                    names.Add(rank.ToString("00", CultureInfo.InvariantCulture) + "-" + suit);
                }
            }

            return names.ToArray();
        }
    }
}
