using System;
using System.Collections.Generic;

namespace TrucoRDR2
{
    public enum TrucoPlayer
    {
        Player,
        Opponent
    }

    /// <summary>A completed two-player trick (baza).</summary>
    public sealed class TrucoTrick
    {
        public int Number { get; private set; }
        public TrucoPlayer FirstPlayer { get; private set; }
        public TrucoPlayer SecondPlayer { get; private set; }
        public TrucoCard FirstCard { get; private set; }
        public TrucoCard SecondCard { get; private set; }
        public TrucoPlayer? Winner { get; private set; }
        public bool IsParda { get { return !Winner.HasValue; } }

        internal TrucoTrick(
            int number,
            TrucoPlayer firstPlayer,
            TrucoCard firstCard,
            TrucoPlayer secondPlayer,
            TrucoCard secondCard)
        {
            Number = number;
            FirstPlayer = firstPlayer;
            FirstCard = firstCard;
            SecondPlayer = secondPlayer;
            SecondCard = secondCard;

            int firstPower = TrucoRules.TrucoPower(firstCard);
            int secondPower = TrucoRules.TrucoPower(secondCard);

            if (firstPower > secondPower)
                Winner = firstPlayer;
            else if (secondPower > firstPower)
                Winner = secondPlayer;
            else
                Winner = null;
        }
    }

    /// <summary>
    /// Rules engine for one mano of Argentine two-player truco.
    /// It handles the 3-card deal, turn order, trick winners and parda resolution.
    /// Betting, envido calls and match scoring are intentionally handled by later layers.
    /// </summary>
    public sealed class TrucoRound
    {
        private readonly List<TrucoCard> _playerCards = new List<TrucoCard>(3);
        private readonly List<TrucoCard> _opponentCards = new List<TrucoCard>(3);
        private readonly List<TrucoTrick> _tricks = new List<TrucoTrick>(3);
        private TrucoPlayer _pendingFirstPlayer;

        public TrucoPlayer Mano { get; private set; }
        public TrucoPlayer CurrentPlayer { get; private set; }
        public bool IsComplete { get; private set; }
        public TrucoPlayer? Winner { get; private set; }

        /// <summary>The current hand of the human player, in display/play order.</summary>
        public IList<TrucoCard> PlayerCards { get { return _playerCards.AsReadOnly(); } }

        /// <summary>The opponent's cards. The view layer should keep these hidden.</summary>
        public IList<TrucoCard> OpponentCards { get { return _opponentCards.AsReadOnly(); } }

        public IList<TrucoTrick> Tricks { get { return _tricks.AsReadOnly(); } }

        public TrucoTrick LastTrick
        {
            get { return _tricks.Count == 0 ? null : _tricks[_tricks.Count - 1]; }
        }

        /// <summary>First card of a trick while waiting for the other player to respond.</summary>
        public TrucoCard PendingCard { get; private set; }

        public TrucoPlayer? PendingPlayer { get; private set; }

        public TrucoRound(TrucoPlayer mano)
            : this(mano, new Random())
        {
        }

        public TrucoRound(TrucoPlayer mano, Random random)
        {
            if (random == null)
                throw new ArgumentNullException("random");

            Mano = mano;
            CurrentPlayer = mano;

            List<TrucoCard> deck = TrucoDeck.CreateShuffled(random);
            for (int i = 0; i < 3; i++)
            {
                if (mano == TrucoPlayer.Player)
                {
                    _playerCards.Add(deck[i * 2]);
                    _opponentCards.Add(deck[i * 2 + 1]);
                }
                else
                {
                    _opponentCards.Add(deck[i * 2]);
                    _playerCards.Add(deck[i * 2 + 1]);
                }
            }
        }

        /// <summary>
        /// Plays one card from the current player's hand. Returns null after the first
        /// card of a trick and the completed trick after the response is played.
        /// </summary>
        public TrucoTrick PlayCard(TrucoPlayer player, int cardIndex)
        {
            if (IsComplete)
                throw new InvalidOperationException("La mano ya terminó.");

            if (player != CurrentPlayer)
                throw new InvalidOperationException("No es el turno de ese jugador.");

            List<TrucoCard> hand = GetMutableHand(player);
            if (cardIndex < 0 || cardIndex >= hand.Count)
                throw new ArgumentOutOfRangeException("cardIndex");

            TrucoCard card = hand[cardIndex];
            hand.RemoveAt(cardIndex);

            if (PendingCard == null)
            {
                PendingCard = card;
                PendingPlayer = player;
                _pendingFirstPlayer = player;
                CurrentPlayer = OtherPlayer(player);
                return null;
            }

            TrucoTrick trick = new TrucoTrick(
                _tricks.Count + 1,
                _pendingFirstPlayer,
                PendingCard,
                player,
                card);

            _tricks.Add(trick);
            PendingCard = null;
            PendingPlayer = null;

            ResolveHandIfPossible();

            if (!IsComplete)
                CurrentPlayer = trick.Winner.HasValue ? trick.Winner.Value : trick.FirstPlayer;

            return trick;
        }

        public int TricksWonBy(TrucoPlayer player)
        {
            int wins = 0;
            foreach (TrucoTrick trick in _tricks)
            {
                if (trick.Winner.HasValue && trick.Winner.Value == player)
                    wins++;
            }

            return wins;
        }

        private void ResolveHandIfPossible()
        {
            if (_tricks.Count < 2)
                return;

            TrucoTrick first = _tricks[0];
            TrucoTrick second = _tricks[1];

            if (first.Winner.HasValue)
            {
                // If the first trick was won, that lead survives a parda in the second.
                // Winning both first tricks also ends the mano immediately.
                if (!second.Winner.HasValue || second.Winner.Value == first.Winner.Value)
                {
                    Complete(first.Winner.Value);
                    return;
                }

                if (_tricks.Count >= 3)
                {
                    // If each player won one of the first two tricks, the third decides.
                    // A parda in the third goes to the winner of the first trick.
                    TrucoTrick third = _tricks[2];
                    Complete(third.Winner ?? first.Winner.Value);
                }

                return;
            }

            // If the first trick was parda, the second trick decides unless it is also parda.
            if (second.Winner.HasValue)
            {
                Complete(second.Winner.Value);
                return;
            }

            if (_tricks.Count >= 3)
            {
                // Three pardas: the player who was mano at the start wins.
                TrucoTrick third = _tricks[2];
                Complete(third.Winner ?? Mano);
            }
        }

        private void Complete(TrucoPlayer winner)
        {
            Winner = winner;
            IsComplete = true;
        }

        private List<TrucoCard> GetMutableHand(TrucoPlayer player)
        {
            return player == TrucoPlayer.Player ? _playerCards : _opponentCards;
        }

        private static TrucoPlayer OtherPlayer(TrucoPlayer player)
        {
            return player == TrucoPlayer.Player ? TrucoPlayer.Opponent : TrucoPlayer.Player;
        }
    }
}
