using System.Globalization;

namespace TrucoRDR2
{
    public enum SpanishSuit
    {
        Bastos,
        Copas,
        Espadas,
        Oros
    }

    /// <summary>Game-rule card model. Its TextureName follows the YTD texture names.</summary>
    public sealed class TrucoCard
    {
        public int Rank { get; private set; }
        public SpanishSuit Suit { get; private set; }

        public TrucoCard(int rank, SpanishSuit suit)
        {
            if (rank < 1 || rank > 12 || rank == 8 || rank == 9)
                throw new System.ArgumentOutOfRangeException("rank", "El truco argentino usa 1-7 y 10-12.");

            Rank = rank;
            Suit = suit;
        }

        public string TextureName
        {
            get
            {
                return Rank.ToString("00", CultureInfo.InvariantCulture) + "-" + Suit.ToString().ToLowerInvariant();
            }
        }
    }
}
