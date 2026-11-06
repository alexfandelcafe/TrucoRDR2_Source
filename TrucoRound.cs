using System;
using System.Collections.Generic;

namespace TrucoRDR2
{
    public enum TrucoPlayer
    {
        Player,
        Opponent
    }

    public enum TrucoCall
    {
        None,
        Envido,
        RealEnvido,
        FaltaEnvido,
        Truco,
        Retruco,
        ValeCuatro,
        Flor,
        ContraFlor,
        ContraFlorAlResto
    }

    /// <summary>A completed trick (baza) in a two-player hand.</summary>
    public sealed class TrucoTrick
    {
        public int Number { get; private set; }
        public TrucoPlayer FirstPlayer { get; private set; }
        public TrucoPlayer SecondPlayer { get; private set; }
        public TrucoCard FirstCard { get; private set; }
        public TrucoCard SecondCard { get; private set; }
        public TrucoPlayer? Winner { get; private set; }
        public bool IsParda { get { return !Winner.HasValue; } }

        internal TrucoTrick(int number, TrucoPlayer firstPlayer, TrucoCard firstCard,
            TrucoPlayer secondPlayer, TrucoCard secondCard)
        {
            Number = number;
            FirstPlayer = firstPlayer;
            SecondPlayer = secondPlayer;
            FirstCard = firstCard;
            SecondCard = secondCard;

            int firstPower = TrucoRules.TrucoPower(firstCard);
            int secondPower = TrucoRules.TrucoPower(secondCard);
            if (firstPower > secondPower) Winner = firstPlayer;
            else if (secondPower > firstPower) Winner = secondPlayer;
            else Winner = null;
        }
    }

    /// <summary>
    /// One Argentine truco mano a mano hand, including envido, optional flor,
    /// truco betting, trick resolution and point awards.
    /// </summary>
    public sealed class TrucoRound
    {
        private readonly List<TrucoCard> _playerCards = new List<TrucoCard>(3);
        private readonly List<TrucoCard> _opponentCards = new List<TrucoCard>(3);
        private readonly List<TrucoTrick> _tricks = new List<TrucoTrick>(3);
        private readonly int _targetPoints;
        private readonly int _playerScoreAtStart;
        private readonly int _opponentScoreAtStart;

        private TrucoPlayer _pendingFirstPlayer;
        private bool _initialized;
        private bool _envidoResolved;
        private bool _florResolved;
        private bool _envidoClosed;
        private int _envidoBasePoints;
        private int _envidoTotal;
        private TrucoPlayer _envidoBaseWinner;
        private int _florBasePoints;
        private int _florTotal;
        private TrucoPlayer _florBaseWinner;
        private int _pendingTrucoLevel;
        private int _trucoLevel;
        private TrucoPlayer? _trucoRaisePlayer;

        public event Action<TrucoPlayer, int, string> PointsAwarded;

        public TrucoPlayer Mano { get; private set; }
        public TrucoPlayer CurrentPlayer { get; private set; }
        public bool IsComplete { get; private set; }
        public TrucoPlayer? Winner { get; private set; }
        public int PointsAwardedForHand { get; private set; }
        public int TrucoStake { get { return _trucoLevel + 1; } }
        public int TrucoLevel { get { return _trucoLevel; } }
        public bool FlorEnabled { get; private set; }
        public bool EnvidoResolved { get { return _envidoResolved; } }
        public bool FlorResolved { get { return _florResolved; } }
        public int CardsPlayed { get { return _tricks.Count * 2 + (PendingCard == null ? 0 : 1); } }
        public bool HasPendingResponse { get { return PendingCall != TrucoCall.None; } }
        public TrucoCall PendingCall { get; private set; }
        public TrucoPlayer? PendingCaller { get; private set; }
        public TrucoPlayer? PendingResponder { get; private set; }
        public int PendingValue { get; private set; }
        public string LastAnnouncement { get; private set; }

        public IList<TrucoCard> PlayerCards { get { return _playerCards.AsReadOnly(); } }
        public IList<TrucoCard> OpponentCards { get { return _opponentCards.AsReadOnly(); } }
        public IList<TrucoTrick> Tricks { get { return _tricks.AsReadOnly(); } }

        public TrucoTrick LastTrick
        {
            get { return _tricks.Count == 0 ? null : _tricks[_tricks.Count - 1]; }
        }

        public TrucoCard PendingCard { get; private set; }
        public TrucoPlayer? PendingPlayer { get; private set; }

        public TrucoRound(TrucoPlayer mano, Random random, int targetPoints,
            int playerScore, int opponentScore, bool florEnabled)
        {
            if (random == null) throw new ArgumentNullException("random");
            if (targetPoints != 15 && targetPoints != 30)
                throw new ArgumentOutOfRangeException("targetPoints", "La partida debe ser a 15 o 30 puntos.");

            Mano = mano;
            CurrentPlayer = mano;
            _targetPoints = targetPoints;
            _playerScoreAtStart = playerScore;
            _opponentScoreAtStart = opponentScore;
            FlorEnabled = florEnabled;

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
            LastAnnouncement = "Se repartieron tres cartas a cada jugador.";
        }

        /// <summary>Called by TrucoMatch after subscribing to PointsAwarded.</summary>
        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            if (!FlorEnabled) return;

            bool playerHasFlor = TrucoRules.HasFlor(_playerCards);
            bool opponentHasFlor = TrucoRules.HasFlor(_opponentCards);

            if (playerHasFlor && opponentHasFlor)
            {
                _florBasePoints = 0;
                _florBaseWinner = Mano;
                _florTotal = 3;
                SetPending(TrucoCall.Flor, Mano, OtherPlayer(Mano), 3);
                _envidoClosed = true;
                LastAnnouncement = PlayerName(Mano) + " canta Flor. El rival debe responder.";
            }
            else if (playerHasFlor)
            {
                _florResolved = true;
                _envidoClosed = true;
                Award(TrucoPlayer.Player, 3, "Flor no disputada");
                LastAnnouncement = "Tenés Flor: ganás 3 puntos.";
            }
            else if (opponentHasFlor)
            {
                _florResolved = true;
                _envidoClosed = true;
                Award(TrucoPlayer.Opponent, 3, "Flor no disputada");
                LastAnnouncement = "El rival tiene Flor y gana 3 puntos.";
            }
        }

        public bool CanCallEnvido(TrucoPlayer player)
        {
            if (IsComplete || HasPendingResponse || player != CurrentPlayer) return false;
            if (CardsPlayed != 0 || _envidoClosed || _envidoResolved || _trucoLevel != 0) return false;
            if (FlorEnabled && (TrucoRules.HasFlor(_playerCards) || TrucoRules.HasFlor(_opponentCards))) return false;
            return true;
        }

        public bool CanRaiseEnvido(TrucoPlayer player, TrucoCall call)
        {
            if (IsComplete || !HasPendingResponse || !PendingResponder.HasValue ||
                PendingResponder.Value != player) return false;
            if (!IsEnvidoCall(call)) return false;

            if (PendingCall == TrucoCall.Envido)
                return call == TrucoCall.Envido || call == TrucoCall.RealEnvido || call == TrucoCall.FaltaEnvido;
            if (PendingCall == TrucoCall.RealEnvido)
                return call == TrucoCall.FaltaEnvido;
            return false;
        }

        public bool CallEnvido(TrucoPlayer player, TrucoCall call)
        {
            if (!IsEnvidoCall(call) || IsComplete) return false;

            if (HasPendingResponse)
            {
                if (!CanRaiseEnvido(player, call)) return false;
                _envidoBasePoints = _envidoTotal;
                _envidoBaseWinner = PendingCaller.Value;
            }
            else
            {
                if (!CanCallEnvido(player)) return false;
                _envidoBasePoints = 0;
                _envidoBaseWinner = player;
            }

            int callValue = GetEnvidoCallValue(call);
            _envidoTotal = _envidoBasePoints + callValue;
            SetPending(call, player, OtherPlayer(player), _envidoTotal);
            LastAnnouncement = PlayerName(player) + " canta " + CallName(call) + ".";
            return true;
        }

        public bool CanCallTruco(TrucoPlayer player)
        {
            if (IsComplete || HasPendingResponse || player != CurrentPlayer || _trucoLevel >= 3)
                return false;

            return !_trucoRaisePlayer.HasValue || _trucoRaisePlayer.Value == player;
        }

        public bool CallTruco(TrucoPlayer player)
        {
            if (!CanCallTruco(player)) return false;
            int nextLevel = _trucoLevel + 1;
            SetTrucoOffer(player, nextLevel);
            LastAnnouncement = PlayerName(player) + " canta " + CallName(PendingCall) + ".";
            return true;
        }

        /// <summary>The responder may answer a Truco with Retruco or a Retruco with Vale Cuatro.</summary>
        public bool RaiseTruco(TrucoPlayer player)
        {
            if (IsComplete || !HasPendingResponse || !PendingResponder.HasValue ||
                PendingResponder.Value != player || !IsTrucoCall(PendingCall) ||
                _pendingTrucoLevel >= 3) return false;

            int nextLevel = _pendingTrucoLevel + 1;
            SetTrucoOffer(player, nextLevel);
            LastAnnouncement = PlayerName(player) + " sube a " + CallName(PendingCall) + ".";
            return true;
        }

        public bool CanRaiseFlor(TrucoPlayer player, TrucoCall call)
        {
            if (IsComplete || !HasPendingResponse || !PendingResponder.HasValue ||
                PendingResponder.Value != player || !TrucoRules.HasFlor(GetMutableHand(player)))
                return false;

            if (PendingCall == TrucoCall.Flor)
                return call == TrucoCall.ContraFlor;
            if (PendingCall == TrucoCall.ContraFlor)
                return call == TrucoCall.ContraFlorAlResto;
            return false;
        }

        public bool RaiseFlor(TrucoPlayer player, TrucoCall call)
        {
            if (!CanRaiseFlor(player, call)) return false;

            _florBasePoints = _florTotal;
            _florBaseWinner = PendingCaller.Value;
            _florTotal = call == TrucoCall.ContraFlor
                ? 6
                : CalculateFaltaPoints();
            SetPending(call, player, OtherPlayer(player), _florTotal);
            LastAnnouncement = PlayerName(player) + " canta " + CallName(call) + ".";
            return true;
        }

        public bool RespondPending(TrucoPlayer player, bool accepted)
        {
            if (IsComplete || !HasPendingResponse || !PendingResponder.HasValue ||
                PendingResponder.Value != player) return false;

            if (IsEnvidoCall(PendingCall))
                return RespondEnvido(accepted);
            if (IsFlorCall(PendingCall))
                return RespondFlor(accepted);
            if (IsTrucoCall(PendingCall))
                return RespondTruco(accepted);

            return false;
        }

        public TrucoTrick PlayCard(TrucoPlayer player, int cardIndex)
        {
            if (IsComplete) throw new InvalidOperationException("La mano ya terminó.");
            if (HasPendingResponse) throw new InvalidOperationException("Primero hay que responder el canto.");
            if (player != CurrentPlayer) throw new InvalidOperationException("No es el turno de ese jugador.");

            List<TrucoCard> hand = GetMutableHand(player);
            if (cardIndex < 0 || cardIndex >= hand.Count)
                throw new ArgumentOutOfRangeException("cardIndex");

            _envidoClosed = true;
            TrucoCard card = hand[cardIndex];
            hand.RemoveAt(cardIndex);

            if (PendingCard == null)
            {
                PendingCard = card;
                PendingPlayer = player;
                _pendingFirstPlayer = player;
                CurrentPlayer = OtherPlayer(player);
                LastAnnouncement = PlayerName(player) + " juega una carta.";
                return null;
            }

            TrucoTrick trick = new TrucoTrick(
                _tricks.Count + 1, _pendingFirstPlayer, PendingCard, player, card);
            _tricks.Add(trick);
            PendingCard = null;
            PendingPlayer = null;

            LastAnnouncement = trick.IsParda
                ? "La baza " + trick.Number + " terminó en parda."
                : "La baza " + trick.Number + " la ganó " + PlayerName(trick.Winner.Value) + ".";

            ResolveHandIfPossible();
            if (!IsComplete)
                CurrentPlayer = trick.Winner.HasValue ? trick.Winner.Value : trick.FirstPlayer;

            return trick;
        }

        public int TricksWonBy(TrucoPlayer player)
        {
            int wins = 0;
            foreach (TrucoTrick trick in _tricks)
                if (trick.Winner.HasValue && trick.Winner.Value == player) wins++;
            return wins;
        }

        public int EnvidoFor(TrucoPlayer player)
        {
            return TrucoRules.EnvidoPoints(GetMutableHand(player));
        }

        public int FlorFor(TrucoPlayer player)
        {
            return TrucoRules.FlorPoints(GetMutableHand(player));
        }

        public int HandStrength(TrucoPlayer player)
        {
            IList<TrucoCard> hand = GetMutableHand(player);
            int score = 0;
            foreach (TrucoCard card in hand) score += TrucoRules.TrucoPower(card);
            return score;
        }

        public TrucoCard GetCard(TrucoPlayer player, int index)
        {
            IList<TrucoCard> hand = GetMutableHand(player).AsReadOnly();
            return index < 0 || index >= hand.Count ? null : hand[index];
        }

        private bool RespondEnvido(bool accepted)
        {
            TrucoPlayer caller = PendingCaller.Value;
            if (accepted)
            {
                TrucoPlayer winner = TrucoRules.CompareEnvido(_playerCards, _opponentCards, Mano);
                int playerPoints = TrucoRules.EnvidoPoints(_playerCards);
                int opponentPoints = TrucoRules.EnvidoPoints(_opponentCards);
                Award(winner, _pendingValue, "Envido ganado (" + playerPoints + " a " + opponentPoints + ")");
                LastAnnouncement = "Envido querido: " + playerPoints + " a " + opponentPoints +
                                    ". Gana " + PlayerName(winner) + " (" + _pendingValue + " puntos).";
            }
            else
            {
                int points = _envidoBasePoints > 0 ? _envidoBasePoints : 1;
                TrucoPlayer winner = _envidoBasePoints > 0 ? _envidoBaseWinner : caller;
                Award(winner, points, "Envido no querido");
                LastAnnouncement = "Envido no querido: " + PlayerName(winner) + " gana " + points + " punto(s).";
            }

            _envidoResolved = true;
            _envidoClosed = true;
            ClearPending();
            return true;
        }

        private bool RespondFlor(bool accepted)
        {
            TrucoPlayer caller = PendingCaller.Value;
            if (accepted)
            {
                TrucoPlayer winner = TrucoRules.CompareFlor(_playerCards, _opponentCards, Mano);
                int playerPoints = TrucoRules.FlorPoints(_playerCards);
                int opponentPoints = TrucoRules.FlorPoints(_opponentCards);
                Award(winner, _pendingValue, "Flor ganada (" + playerPoints + " a " + opponentPoints + ")");
                LastAnnouncement = "Flor querida: " + playerPoints + " a " + opponentPoints +
                                   ". Gana " + PlayerName(winner) + " (" + _pendingValue + " puntos).";
            }
            else
            {
                int points = _florBasePoints > 0 ? _florBasePoints : 3;
                TrucoPlayer winner = _florBasePoints > 0 ? _florBaseWinner : caller;
                Award(winner, points, "Flor no querida");
                LastAnnouncement = "Flor no querida: " + PlayerName(winner) + " gana " + points + " punto(s).";
            }

            _florResolved = true;
            _envidoClosed = true;
            ClearPending();
            return true;
        }

        private bool RespondTruco(bool accepted)
        {
            TrucoPlayer caller = PendingCaller.Value;
            if (accepted)
            {
                _trucoLevel = _pendingTrucoLevel;
                _trucoRaisePlayer = PendingResponder.Value;
                LastAnnouncement = "Se quiso " + CallName(PendingCall) + ". La mano sigue por " + TrucoStake + " puntos.";
                ClearPending();
            }
            else
            {
                int points = _pendingTrucoLevel;
                LastAnnouncement = CallName(PendingCall) + " no querido: " + PlayerName(caller) +
                                   " gana " + points + " punto(s).";
                Complete(caller, points, "Canto de truco no querido");
            }
            return true;
        }

        private void ResolveHandIfPossible()
        {
            if (_tricks.Count < 2) return;

            TrucoTrick first = _tricks[0];
            TrucoTrick second = _tricks[1];

            if (first.Winner.HasValue)
            {
                if (!second.Winner.HasValue || second.Winner.Value == first.Winner.Value)
                {
                    Complete(first.Winner.Value, TrucoStake, "Mano ganada");
                    return;
                }

                if (_tricks.Count >= 3)
                {
                    TrucoTrick third = _tricks[2];
                    Complete(third.Winner ?? first.Winner.Value, TrucoStake, "Mano ganada en tercera baza");
                }
                return;
            }

            if (second.Winner.HasValue)
            {
                Complete(second.Winner.Value, TrucoStake, "Segunda baza después de parda");
                return;
            }

            if (_tricks.Count >= 3)
            {
                TrucoTrick third = _tricks[2];
                Complete(third.Winner ?? Mano, TrucoStake, "Tres pardas o tercera baza");
            }
        }

        private void Complete(TrucoPlayer winner, int points, string reason)
        {
            if (IsComplete) return;
            IsComplete = true;
            Winner = winner;
            PointsAwardedForHand = points;
            Award(winner, points, reason);
        }

        private void Award(TrucoPlayer player, int points, string reason)
        {
            if (points <= 0) return;
            Action<TrucoPlayer, int, string> handler = PointsAwarded;
            if (handler != null) handler(player, points, reason);
        }

        private int GetEnvidoCallValue(TrucoCall call)
        {
            if (call == TrucoCall.Envido) return 2;
            if (call == TrucoCall.RealEnvido) return 3;
            if (call == TrucoCall.FaltaEnvido) return CalculateFaltaPoints();
            throw new ArgumentOutOfRangeException("call");
        }

        private int CalculateFaltaPoints()
        {
            int leaderScore = Math.Max(_playerScoreAtStart, _opponentScoreAtStart);
            return Math.Max(1, _targetPoints - leaderScore);
        }

        private void SetTrucoOffer(TrucoPlayer player, int level)
        {
            _pendingTrucoLevel = level;
            TrucoCall call = level == 1 ? TrucoCall.Truco :
                             level == 2 ? TrucoCall.Retruco : TrucoCall.ValeCuatro;
            SetPending(call, player, OtherPlayer(player), level);
        }

        private void SetPending(TrucoCall call, TrucoPlayer caller, TrucoPlayer responder, int value)
        {
            PendingCall = call;
            PendingCaller = caller;
            PendingResponder = responder;
            PendingValue = value;
        }

        private void ClearPending()
        {
            PendingCall = TrucoCall.None;
            PendingCaller = null;
            PendingResponder = null;
            PendingValue = 0;
            _pendingTrucoLevel = 0;
        }

        private static bool IsEnvidoCall(TrucoCall call)
        {
            return call == TrucoCall.Envido || call == TrucoCall.RealEnvido || call == TrucoCall.FaltaEnvido;
        }

        private static bool IsFlorCall(TrucoCall call)
        {
            return call == TrucoCall.Flor || call == TrucoCall.ContraFlor || call == TrucoCall.ContraFlorAlResto;
        }

        private static bool IsTrucoCall(TrucoCall call)
        {
            return call == TrucoCall.Truco || call == TrucoCall.Retruco || call == TrucoCall.ValeCuatro;
        }

        public static string CallName(TrucoCall call)
        {
            switch (call)
            {
                case TrucoCall.Envido: return "Envido";
                case TrucoCall.RealEnvido: return "Real Envido";
                case TrucoCall.FaltaEnvido: return "Falta Envido";
                case TrucoCall.Truco: return "Truco";
                case TrucoCall.Retruco: return "Retruco";
                case TrucoCall.ValeCuatro: return "Vale Cuatro";
                case TrucoCall.Flor: return "Flor";
                case TrucoCall.ContraFlor: return "Contra Flor";
                case TrucoCall.ContraFlorAlResto: return "Contra Flor al Resto";
                default: return string.Empty;
            }
        }

        private static string PlayerName(TrucoPlayer player)
        {
            return player == TrucoPlayer.Player ? "Vos" : "El rival";
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