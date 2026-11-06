using System;
using System.Collections.Generic;
using System.Drawing;
using RDR2.Native;
using RDR2.UI;

namespace TrucoRDR2
{
    /// <summary>
    /// First in-game table prototype: a single mano against a simple random-play opponent.
    /// The opponent's remaining hand stays hidden. This view is separate from the F7 texture viewer.
    /// </summary>
    internal sealed class TrucoTablePreview
    {
        private const float CardWidth = 180.0f;
        private const float CardHeight = 270.0f;
        private const float HandTop = 690.0f;
        private const double MissingDictionaryGraceSeconds = 5.0;

        private readonly string _textureDictionary;
        private readonly Random _random = new Random();
        private readonly PointF[] _handPositions =
        {
            new PointF(670.0f, HandTop),
            new PointF(870.0f, HandTop),
            new PointF(1070.0f, HandTop)
        };
        private readonly PointF[] _playedPositions =
        {
            new PointF(740.0f, 330.0f),
            new PointF(1000.0f, 330.0f)
        };

        private readonly TextElement _header;
        private readonly TextElement _instructions;
        private readonly TextElement _status;
        private readonly TextElement _roundInfo;
        private readonly TextElement _handTitle;
        private readonly TextElement[] _handLabels = new TextElement[3];
        private readonly TextElement[] _playedLabels = new TextElement[2];
        private readonly Sprite[] _handSprites = new Sprite[3];
        private readonly Sprite[] _playedSprites = new Sprite[2];

        private TrucoRound _round;
        private bool _visible;
        private bool _textureRequestSent;
        private bool _spritesCreated;
        private bool _textureDictionaryLoaded;
        private int _selectedCardIndex;
        private DateTime _textureRequestTimeUtc;

        public bool IsVisible { get { return _visible; } }
        public bool IsRoundComplete { get { return _round != null && _round.IsComplete; } }

        public TrucoTablePreview(string textureDictionary)
        {
            if (string.IsNullOrEmpty(textureDictionary))
                throw new ArgumentException("El nombre del diccionario no puede estar vacío.", "textureDictionary");

            _textureDictionary = textureDictionary;

            _header = new TextElement(
                "TRUCO RDR2 - MANO DE PRUEBA",
                new PointF(960.0f, 80.0f), 0.65f, Color.Wheat,
                Alignment.Center, true, true);

            _instructions = new TextElement(
                "F8: cerrar mesa    FLECHAS: elegir carta    ENTER: jugar    ESC: cerrar",
                new PointF(960.0f, 125.0f), 0.40f, Color.WhiteSmoke,
                Alignment.Center, true, true);

            _roundInfo = new TextElement(
                string.Empty,
                new PointF(960.0f, 205.0f), 0.42f, Color.WhiteSmoke,
                Alignment.Center, true, true);

            _handTitle = new TextElement(
                "TUS CARTAS",
                new PointF(960.0f, 635.0f), 0.45f, Color.Wheat,
                Alignment.Center, true, true);

            _status = new TextElement(
                "Solicitando cartas...",
                new PointF(960.0f, 1000.0f), 0.40f, Color.WhiteSmoke,
                Alignment.Center, true, true);

            for (int i = 0; i < 3; i++)
            {
                _handLabels[i] = new TextElement(
                    string.Empty,
                    new PointF(_handPositions[i].X + CardWidth / 2.0f, 655.0f),
                    0.42f, Color.WhiteSmoke, Alignment.Center, true, true);
            }

            _playedLabels[0] = new TextElement(
                "JUGADOR",
                new PointF(_playedPositions[0].X + CardWidth / 2.0f, 300.0f),
                0.42f, Color.WhiteSmoke, Alignment.Center, true, true);

            _playedLabels[1] = new TextElement(
                "RIVAL",
                new PointF(_playedPositions[1].X + CardWidth / 2.0f, 300.0f),
                0.42f, Color.WhiteSmoke, Alignment.Center, true, true);
        }

        public void Start()
        {
            Close();
            _visible = true;
            _round = new TrucoRound(TrucoPlayer.Player, _random);
            _selectedCardIndex = 0;
            _textureRequestSent = false;
            _textureDictionaryLoaded = false;
            _textureRequestTimeUtc = DateTime.UtcNow;
            _spritesCreated = false;
            UpdateHandLabels();
            SetCaption(_status, "Preparando una mano de 3 cartas por jugador...");
        }

        public void Close()
        {
            _visible = false;
            DisposeSprites();
            _round = null;
            _textureRequestSent = false;
            _spritesCreated = false;
            _textureDictionaryLoaded = false;
        }

        public void HandleKeyDown(System.Windows.Forms.KeyEventArgs e)
        {
            if (!_visible || _round == null)
                return;

            if (e.KeyCode == System.Windows.Forms.Keys.Escape)
            {
                Close();
                e.Handled = true;
                return;
            }

            if (e.KeyCode == System.Windows.Forms.Keys.Left)
            {
                if (_textureDictionaryLoaded && !_round.IsComplete &&
                    _round.CurrentPlayer == TrucoPlayer.Player && _round.PlayerCards.Count > 0)
                {
                    _selectedCardIndex =
                        (_selectedCardIndex - 1 + _round.PlayerCards.Count) % _round.PlayerCards.Count;
                    UpdateHandLabels();
                }
                e.Handled = true;
            }
            else if (e.KeyCode == System.Windows.Forms.Keys.Right)
            {
                if (_textureDictionaryLoaded && !_round.IsComplete &&
                    _round.CurrentPlayer == TrucoPlayer.Player && _round.PlayerCards.Count > 0)
                {
                    _selectedCardIndex = (_selectedCardIndex + 1) % _round.PlayerCards.Count;
                    UpdateHandLabels();
                }
                e.Handled = true;
            }
            else if (e.KeyCode == System.Windows.Forms.Keys.Enter)
            {
                if (_textureDictionaryLoaded && !_round.IsComplete &&
                    _round.CurrentPlayer == TrucoPlayer.Player)
                    PlaySelectedCard();

                e.Handled = true;
            }
        }

        public void Draw()
        {
            if (!_visible || _round == null)
                return;

            _header.Draw();
            _instructions.Draw();

            bool dictionaryExists = false;
            bool dictionaryLoaded = false;
            try
            {
                if (!_textureRequestSent)
                {
                    TXD.REQUEST_STREAMED_TEXTURE_DICT(_textureDictionary, false);
                    _textureRequestSent = true;
                    _textureRequestTimeUtc = DateTime.UtcNow;
                }

                dictionaryExists = TXD.DOES_STREAMED_TEXTURE_DICT_EXIST(_textureDictionary);
                dictionaryLoaded = TXD.HAS_STREAMED_TEXTURE_DICT_LOADED(_textureDictionary);
            }
            catch (Exception ex)
            {
                _textureDictionaryLoaded = false;
                SetCaption(_status, "Error consultando el YTD (" + ex.GetType().Name + ").");
                _roundInfo.Draw();
                _status.Draw();
                DrawHandLabels();
                return;
            }

            _textureDictionaryLoaded = dictionaryLoaded;
            if (!dictionaryLoaded)
            {
                if (!dictionaryExists &&
                    (DateTime.UtcNow - _textureRequestTimeUtc).TotalSeconds >= MissingDictionaryGraceSeconds)
                {
                    SetCaption(_status,
                        "No se encuentra '" + _textureDictionary + "'. Revisa la instalación del YTD.");
                }
                else
                {
                    SetCaption(_status, "Esperando carga del diccionario: " + _textureDictionary);
                }

                UpdateRoundInfo();
                _roundInfo.Draw();
                _handTitle.Draw();
                _status.Draw();
                UpdateHandLabels();
                DrawHandLabels();
                return;
            }

            if (!_spritesCreated)
                RefreshSprites();

            UpdateRoundInfo();
            UpdateStatus();
            UpdateHandLabels();

            _roundInfo.Draw();
            _handTitle.Draw();
            _playedLabels[0].Draw();
            _playedLabels[1].Draw();

            for (int i = 0; i < _handSprites.Length; i++)
            {
                if (_handSprites[i] != null)
                    _handSprites[i].Draw();

                _handLabels[i].Draw();
            }

            for (int i = 0; i < _playedSprites.Length; i++)
            {
                if (_playedSprites[i] != null)
                    _playedSprites[i].Draw();
            }

            _status.Draw();
        }

        private void PlaySelectedCard()
        {
            if (_round.PlayerCards.Count == 0 ||
                _selectedCardIndex < 0 ||
                _selectedCardIndex >= _round.PlayerCards.Count)
                return;

            try
            {
                _round.PlayCard(TrucoPlayer.Player, _selectedCardIndex);
                PlayOpponentUntilPlayersTurn();
                _selectedCardIndex = Math.Min(_selectedCardIndex, Math.Max(0, _round.PlayerCards.Count - 1));
                RefreshSprites();
                UpdateHandLabels();
            }
            catch (Exception ex)
            {
                SetCaption(_status, "No se pudo jugar la carta: " + ex.Message);
            }
        }

        private void PlayOpponentUntilPlayersTurn()
        {
            while (!_round.IsComplete && _round.CurrentPlayer == TrucoPlayer.Opponent)
            {
                IList<TrucoCard> opponentCards = _round.OpponentCards;
                if (opponentCards.Count == 0)
                    return;

                // Temporary test opponent: chooses a random legal card. This is not the final AI.
                int cardIndex = _random.Next(opponentCards.Count);
                _round.PlayCard(TrucoPlayer.Opponent, cardIndex);
            }
        }

        private void RefreshSprites()
        {
            DisposeSprites();

            IList<TrucoCard> playerCards = _round.PlayerCards;
            for (int i = 0; i < playerCards.Count && i < _handSprites.Length; i++)
            {
                _handSprites[i] = new Sprite(
                    _textureDictionary,
                    playerCards[i].TextureName,
                    new SizeF(CardWidth, CardHeight),
                    _handPositions[i]);
            }

            RefreshPlayedSprites();
            _spritesCreated = true;
        }

        private void RefreshPlayedSprites()
        {
            for (int i = 0; i < _playedSprites.Length; i++)
            {
                if (_playedSprites[i] != null)
                {
                    _playedSprites[i].Dispose();
                    _playedSprites[i] = null;
                }
            }

            if (_round.PendingCard != null && _round.PendingPlayer.HasValue)
            {
                SetPlayedSprite(_round.PendingPlayer.Value, _round.PendingCard);
                return;
            }

            TrucoTrick lastTrick = _round.LastTrick;
            if (lastTrick == null)
                return;

            SetPlayedSprite(lastTrick.FirstPlayer, lastTrick.FirstCard);
            SetPlayedSprite(lastTrick.SecondPlayer, lastTrick.SecondCard);
        }

        private void SetPlayedSprite(TrucoPlayer player, TrucoCard card)
        {
            int slot = player == TrucoPlayer.Player ? 0 : 1;
            _playedSprites[slot] = new Sprite(
                _textureDictionary,
                card.TextureName,
                new SizeF(CardWidth, CardHeight),
                _playedPositions[slot]);
        }

        private void UpdateRoundInfo()
        {
            string first = _round.Tricks.Count > 0 ? FormatTrick(_round.Tricks[0]) : "-";
            string second = _round.Tricks.Count > 1 ? FormatTrick(_round.Tricks[1]) : "-";
            string third = _round.Tricks.Count > 2 ? FormatTrick(_round.Tricks[2]) : "-";

            SetCaption(_roundInfo,
                "Rival: " + _round.OpponentCards.Count + " cartas ocultas" +
                "    |    Bazas: " + first + " / " + second + " / " + third +
                "    |    Ganadas: vos " + _round.TricksWonBy(TrucoPlayer.Player) +
                " - rival " + _round.TricksWonBy(TrucoPlayer.Opponent));
        }

        private void UpdateStatus()
        {
            if (_round.IsComplete)
            {
                SetCaption(_status,
                    _round.Winner == TrucoPlayer.Player
                        ? "Ganaste la mano. F8 reparte otra; ESC cierra la mesa."
                        : "El rival ganó la mano. F8 reparte otra; ESC cierra la mesa.");
            }
            else if (_round.CurrentPlayer == TrucoPlayer.Player)
            {
                SetCaption(_status, "Tu turno: usa las flechas para elegir y ENTER para jugar.");
            }
            else
            {
                SetCaption(_status, "Turno del rival...");
            }
        }

        private void UpdateHandLabels()
        {
            IList<TrucoCard> cards = _round == null ? null : _round.PlayerCards;
            for (int i = 0; i < _handLabels.Length; i++)
            {
                string caption = string.Empty;
                if (cards != null && i < cards.Count)
                {
                    string prefix = i == _selectedCardIndex ? "> " : "  ";
                    caption = prefix + (i + 1) + ". " + cards[i].TextureName;
                }

                SetCaption(_handLabels[i], caption);
            }
        }

        private void DrawHandLabels()
        {
            for (int i = 0; i < _handLabels.Length; i++)
                _handLabels[i].Draw();
        }

        private static string FormatTrick(TrucoTrick trick)
        {
            if (trick.IsParda)
                return "parda";

            return trick.Winner == TrucoPlayer.Player ? "vos" : "rival";
        }

        private void DisposeSprites()
        {
            for (int i = 0; i < _handSprites.Length; i++)
            {
                if (_handSprites[i] != null)
                {
                    _handSprites[i].Dispose();
                    _handSprites[i] = null;
                }
            }

            for (int i = 0; i < _playedSprites.Length; i++)
            {
                if (_playedSprites[i] != null)
                {
                    _playedSprites[i].Dispose();
                    _playedSprites[i] = null;
                }
            }
        }

        private static void SetCaption(TextElement element, string caption)
        {
            if (!string.Equals(element.Caption, caption, StringComparison.Ordinal))
                element.Caption = caption;
        }
    }
}
