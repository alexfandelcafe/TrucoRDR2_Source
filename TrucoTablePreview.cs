using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using RDR2.Native;
using RDR2.UI;

namespace TrucoRDR2
{
    /// <summary>
    /// Playable two-player Argentine truco table. Handles player input, opponent decisions,
    /// scoreboard, all played cards and optional frontend sounds.
    /// </summary>
    internal sealed class TrucoTablePreview
    {
        private const float DesignWidth = 1920.0f;
        private const float DesignHeight = 1080.0f;
        private const float HandCardWidth = 160.0f;
        private const float HandCardHeight = 240.0f;
        private const float HandTop = 700.0f;
        private const float PlayedCardWidth = 108.0f;
        private const float PlayedCardHeight = 162.0f;
        private const float OpponentBackWidth = 64.0f;
        private const float OpponentBackHeight = 96.0f;
        private const double ComputerThinkSeconds = 0.65;

        private sealed class CardAnimation
        {
            public int Slot;
            public string TextureName;
            public PointF From;
            public PointF To;
            public float FromWidth;
            public float FromHeight;
            public float ToWidth;
            public float ToHeight;
            public DateTime StartedUtc;
            public double DurationSeconds;
        }

        private readonly string _textureDictionary;
        private readonly string _tableToggleText;
        private int _targetPoints;
        private bool _florEnabled;
        private bool _soundEnabled;
        private bool _showMenu;
        private int _menuSelection;
        private readonly Random _random = new Random();

        private readonly PointF[] _handPositions;
        private readonly PointF[] _playedPositions;
        private readonly PointF[] _opponentCardBackPositions;

        private readonly TextElement _header;
        private readonly TextElement _instructions;
        private readonly TextElement _scoreboard;
        private readonly TextElement _roundInfo;
        private readonly TextElement _announcement;
        private readonly TextElement _handTitle;
        private readonly TextElement _status;
        private readonly TextElement _menuOptions;
        private readonly TextElement _menuHint;
        private readonly TextElement[] _handLabels = new TextElement[3];
        private readonly TextElement[] _playedLabels = new TextElement[6];
        private readonly Sprite[] _handSprites = new Sprite[3];
        private readonly Sprite[] _playedSprites = new Sprite[6];

        private TrucoMatch _match;
        private bool _visible;
        private bool _textureRequestSent;
        private bool _spritesCreated;
        private bool _textureDictionaryLoaded;
        private int _selectedCardIndex;
        private DateTime _textureRequestTimeUtc;
        private DateTime _nextComputerActionUtc;
        private DateTime _dealAnimationStartUtc;
        private bool _dealAnimationActive;
        private CardAnimation _playedCardAnimation;

        public bool IsVisible { get { return _visible; } }
        public bool IsRoundComplete
        {
            get { return _match != null && (_match.IsComplete || _match.Round.IsComplete); }
        }

        public TrucoTablePreview(string textureDictionary, string tableToggleText,
            int targetPoints, bool florEnabled, bool soundEnabled)
        {
            if (string.IsNullOrEmpty(textureDictionary))
                throw new ArgumentException("El nombre del diccionario no puede estar vacío.", "textureDictionary");
            if (targetPoints != 15 && targetPoints != 30)
                throw new ArgumentOutOfRangeException("targetPoints");

            _textureDictionary = textureDictionary;
            _tableToggleText = string.IsNullOrEmpty(tableToggleText) ? "COMBINACION" : tableToggleText.ToUpperInvariant();
            _targetPoints = targetPoints;
            _florEnabled = florEnabled;
            _soundEnabled = soundEnabled;

            _handPositions = new[]
            {
                LayoutPoint(650.0f, HandTop),
                LayoutPoint(880.0f, HandTop),
                LayoutPoint(1110.0f, HandTop)
            };
            _playedPositions = new[]
            {
                LayoutPoint(560.0f, 395.0f), LayoutPoint(680.0f, 395.0f),
                LayoutPoint(830.0f, 395.0f), LayoutPoint(950.0f, 395.0f),
                LayoutPoint(1100.0f, 395.0f), LayoutPoint(1220.0f, 395.0f)
            };
            _opponentCardBackPositions = new[]
            {
                LayoutPoint(740.0f, 275.0f),
                LayoutPoint(928.0f, 275.0f),
                LayoutPoint(1116.0f, 275.0f)
            };

            _header = new TextElement(
                "TRUCO RDR2",
                LayoutPoint(960.0f, 65.0f), LayoutTextScale(0.70f), Color.Wheat,
                Alignment.Center, true, true);

            _instructions = new TextElement(
                "E ENVIDO | R REAL | F FALTA | T TRUCO | M MAZO | Q QUIERO | N NO QUIERO | FLECHAS + ENTER",
                LayoutPoint(960.0f, 112.0f), LayoutTextScale(0.34f), Color.WhiteSmoke,
                Alignment.Center, true, true);

            _scoreboard = new TextElement(
                string.Empty,
                LayoutPoint(960.0f, 165.0f), LayoutTextScale(0.55f), Color.White,
                Alignment.Center, true, true);

            _roundInfo = new TextElement(
                string.Empty,
                LayoutPoint(960.0f, 210.0f), LayoutTextScale(0.39f), Color.WhiteSmoke,
                Alignment.Center, true, true);

            _announcement = new TextElement(
                string.Empty,
                LayoutPoint(960.0f, 250.0f), LayoutTextScale(0.43f), Color.Wheat,
                Alignment.Center, true, true);

            _handTitle = new TextElement(
                "TUS CARTAS",
                LayoutPoint(960.0f, 665.0f), LayoutTextScale(0.45f), Color.Wheat,
                Alignment.Center, true, true);

            _status = new TextElement(
                "Preparando la mesa...",
                LayoutPoint(960.0f, 1025.0f), LayoutTextScale(0.39f), Color.WhiteSmoke,
                Alignment.Center, true, true);

            _menuOptions = new TextElement(
                string.Empty,
                LayoutPoint(960.0f, 470.0f), LayoutTextScale(0.60f), Color.WhiteSmoke,
                Alignment.Center, true, true);

            _menuHint = new TextElement(
                string.Empty,
                LayoutPoint(960.0f, 650.0f), LayoutTextScale(0.38f), Color.Wheat,
                Alignment.Center, true, true);

            for (int i = 0; i < 3; i++)
            {
                _handLabels[i] = new TextElement(
                    string.Empty,
                    LayoutPoint(650.0f + i * 230.0f + HandCardWidth / 2.0f, 670.0f),
                    LayoutTextScale(0.38f), Color.WhiteSmoke, Alignment.Center, true, true);
            }

            for (int i = 0; i < 6; i++)
            {
                string owner = (i % 2 == 0) ? "VOS" : "RIVAL";
                _playedLabels[i] = new TextElement(
                    owner,
                    LayoutPoint((i < 2 ? 560.0f : (i < 4 ? 830.0f : 1100.0f)) + (i % 2 == 0 ? 0.0f : 120.0f) + PlayedCardWidth / 2.0f, 368.0f),
                    LayoutTextScale(0.34f), Color.WhiteSmoke, Alignment.Center, true, true);
            }
        }

        public void Start()
        {
            _visible = true;

            if (_match == null)
            {
                _showMenu = true;
                _menuSelection = 0;
                UpdateMenuText();
            }
            else
            {
                _showMenu = false;
            }

            DisposeSprites();
            _textureRequestSent = false;
            _textureDictionaryLoaded = false;
            _textureRequestTimeUtc = DateTime.UtcNow;
            _nextComputerActionUtc = DateTime.UtcNow.AddSeconds(ComputerThinkSeconds);
            _dealAnimationActive = false;
            _playedCardAnimation = null;
            _spritesCreated = false;
            UpdateHandLabels();
        }

        private void StartMatch()
        {
            _match = new TrucoMatch(_targetPoints, _florEnabled, _random);
            _showMenu = false;
            _selectedCardIndex = 0;
            _textureRequestSent = false;
            _textureDictionaryLoaded = false;
            _textureRequestTimeUtc = DateTime.UtcNow;
            _nextComputerActionUtc = DateTime.UtcNow.AddSeconds(ComputerThinkSeconds);
            _dealAnimationStartUtc = DateTime.UtcNow;
            _dealAnimationActive = true;
            _playedCardAnimation = null;
            _spritesCreated = false;
            DisposeSprites();
            UpdateHandLabels();
            TrucoAudio.Play(_soundEnabled, TrucoSound.Deal);
        }

        private void UpdateMenuText()
        {
            string target = _targetPoints == 15 ? "15 PUNTOS" : "30 PUNTOS";
            string flor = _florEnabled ? "ACTIVADA" : "DESACTIVADA";
            string sound = _soundEnabled ? "ACTIVADO" : "DESACTIVADO";

            string startPrefix = _menuSelection == 0 ? "> " : "  ";
            string pointsPrefix = _menuSelection == 1 ? "> " : "  ";
            string florPrefix = _menuSelection == 2 ? "> " : "  ";
            string soundPrefix = _menuSelection == 3 ? "> " : "  ";

            SetCaption(_menuOptions,
                startPrefix + "INICIAR PARTIDA\n" +
                pointsPrefix + "DURACION: " + target + "\n" +
                florPrefix + "FLOR: " + flor + "\n" +
                soundPrefix + "SONIDO: " + sound);

            SetCaption(_menuHint,
                "ARRIBA/ABAJO: elegir    IZQUIERDA/DERECHA: cambiar    ENTER: confirmar    ESC: cerrar");
        }

        public void ContinueAfterResult()
        {
            if (_match == null) return;

            if (_match.IsComplete)
            {
                _match = null;
                _showMenu = true;
                _menuSelection = 0;
                DisposeSprites();
                _spritesCreated = false;
                UpdateMenuText();
                return;
            }

            if (!_match.Round.IsComplete)
                return;

            _match.StartNextRound();
            _selectedCardIndex = 0;
            _nextComputerActionUtc = DateTime.UtcNow.AddSeconds(ComputerThinkSeconds);
            _dealAnimationStartUtc = DateTime.UtcNow;
            _dealAnimationActive = !_match.IsComplete;
            _playedCardAnimation = null;
            _spritesCreated = false;
            DisposeSprites();
            UpdateHandLabels();
            TrucoAudio.Play(_soundEnabled, _match.IsComplete ? TrucoSound.Win : TrucoSound.Deal);
        }

        public void Close()
        {
            _visible = false;
            _dealAnimationActive = false;
            _playedCardAnimation = null;
            DisposeSprites();
            // Keep the match, score and current hand so reopening the table resumes play.
            _textureRequestSent = false;
            _spritesCreated = false;
            _textureDictionaryLoaded = false;
        }

        public void HandleKeyDown(KeyEventArgs e)
        {
            if (!_visible)
                return;

            // All keyboard input belongs to the table while it is visible.
            e.Handled = true;

            if (_showMenu)
            {
                HandleMenuKeyDown(e);
                return;
            }

            if (_match == null)
                return;

            if (e.KeyCode == Keys.Escape)
            {
                Close();
                e.Handled = true;
                return;
            }

            if (_dealAnimationActive)
            {
                if (DateTime.UtcNow < _dealAnimationStartUtc.AddMilliseconds(650) &&
                    e.KeyCode != Keys.Escape)
                {
                    e.Handled = true;
                    return;
                }

                _dealAnimationActive = false;
            }

            if (_match.IsComplete || _match.Round.IsComplete)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    ContinueAfterResult();
                    e.Handled = true;
                }
                return;
            }

            if (_match.Round.HasPendingResponse)
            {
                HandlePendingResponse(e);
                return;
            }

            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
            {
                if (_textureDictionaryLoaded &&
                    _match.Round.CurrentPlayer == TrucoPlayer.Player &&
                    _match.Round.PlayerCards.Count > 0)
                {
                    int count = _match.Round.PlayerCards.Count;
                    _selectedCardIndex = e.KeyCode == Keys.Left
                        ? (_selectedCardIndex - 1 + count) % count
                        : (_selectedCardIndex + 1) % count;
                    UpdateHandLabels();
                    TrucoAudio.Play(_soundEnabled, TrucoSound.Select);
                }
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Enter)
            {
                if (_textureDictionaryLoaded && _match.Round.CurrentPlayer == TrucoPlayer.Player)
                    PlayPlayerCard();
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.E)
                TryCallEnvido(TrucoCall.Envido);
            else if (e.KeyCode == Keys.R)
                TryCallEnvido(TrucoCall.RealEnvido);
            else if (e.KeyCode == Keys.F)
                TryCallEnvido(TrucoCall.FaltaEnvido);
            else if (e.KeyCode == Keys.T)
                TryCallTruco();
            else if (e.KeyCode == Keys.M)
            {
                if (_match.Round.Fold(TrucoPlayer.Player))
                {
                    TrucoAudio.Play(_soundEnabled, TrucoSound.Win);
                    RefreshSprites();
                }
            }

            if (e.KeyCode == Keys.E || e.KeyCode == Keys.R ||
                e.KeyCode == Keys.F || e.KeyCode == Keys.T || e.KeyCode == Keys.M)
                e.Handled = true;
        }

        private void HandleMenuKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
                return;
            }

            if (e.KeyCode == Keys.Up)
            {
                _menuSelection = (_menuSelection - 1 + 4) % 4;
                UpdateMenuText();
                TrucoAudio.Play(_soundEnabled, TrucoSound.Select);
                return;
            }

            if (e.KeyCode == Keys.Down)
            {
                _menuSelection = (_menuSelection + 1) % 4;
                UpdateMenuText();
                TrucoAudio.Play(_soundEnabled, TrucoSound.Select);
                return;
            }

            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Enter)
            {
                if (_menuSelection == 0)
                {
                    if (e.KeyCode == Keys.Enter)
                        StartMatch();
                }
                else if (_menuSelection == 1)
                {
                    _targetPoints = _targetPoints == 15 ? 30 : 15;
                }
                else if (_menuSelection == 2)
                {
                    _florEnabled = !_florEnabled;
                }
                else if (_menuSelection == 3)
                {
                    _soundEnabled = !_soundEnabled;
                }

                UpdateMenuText();
                TrucoAudio.Play(_soundEnabled, TrucoSound.Select);
            }
        }

        private void HandlePendingResponse(KeyEventArgs e)
        {
            TrucoRound round = _match.Round;
            if (!round.PendingResponder.HasValue ||
                round.PendingResponder.Value != TrucoPlayer.Player)
            {
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Q)
            {
                if (round.RespondPending(TrucoPlayer.Player, true))
                {
                    bool finished = _match.IsComplete || round.IsComplete;
                    TrucoAudio.Play(_soundEnabled, finished ? TrucoSound.Win : TrucoSound.Accept);
                }
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.N)
            {
                if (round.RespondPending(TrucoPlayer.Player, false))
                {
                    bool finished = _match.IsComplete || round.IsComplete;
                    TrucoAudio.Play(_soundEnabled, finished ? TrucoSound.Win : TrucoSound.Reject);
                }
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.E)
                TryRaiseEnvido(TrucoCall.Envido);
            else if (e.KeyCode == Keys.R)
                TryRaiseEnvido(TrucoCall.RealEnvido);
            else if (e.KeyCode == Keys.F)
                TryRaiseEnvido(TrucoCall.FaltaEnvido);
            else if (e.KeyCode == Keys.T)
            {
                if (round.RaiseTruco(TrucoPlayer.Player))
                    TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
            }
            else if (e.KeyCode == Keys.C)
            {
                if (round.RaiseFlor(TrucoPlayer.Player, TrucoCall.ContraFlor))
                    TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
            }
            else if (e.KeyCode == Keys.B)
            {
                if (round.RaiseFlor(TrucoPlayer.Player, TrucoCall.ContraFlorAlResto))
                    TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
            }

            if (e.KeyCode == Keys.E || e.KeyCode == Keys.R || e.KeyCode == Keys.F ||
                e.KeyCode == Keys.T || e.KeyCode == Keys.C || e.KeyCode == Keys.B)
                e.Handled = true;
        }

        private void TryCallEnvido(TrucoCall call)
        {
            if (_match.Round.CallEnvido(TrucoPlayer.Player, call))
                TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
        }

        private void TryRaiseEnvido(TrucoCall call)
        {
            if (_match.Round.CallEnvido(TrucoPlayer.Player, call))
                TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
        }

        private void TryCallTruco()
        {
            if (_match.Round.CallTruco(TrucoPlayer.Player))
                TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
        }

        public void Draw()
        {
            if (!_visible)
                return;

            try
            {
                // Keep normal gameplay controls disabled for both the in-game menu and the table.
                PAD.DISABLE_ALL_CONTROL_ACTIONS(0);
            }
            catch
            {
                // Input suppression is best-effort and must not stop the table from rendering.
            }

            if (_showMenu)
            {
                DrawMenuSurface();
                _header.Draw();
                _menuOptions.Draw();
                _menuHint.Draw();
                return;
            }

            if (_match == null)
                return;

            DrawTopPanel();
            DrawTableSurface();
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
                DrawTextOnly();
                return;
            }

            _textureDictionaryLoaded = dictionaryLoaded;
            if (!dictionaryLoaded)
            {
                if (!dictionaryExists &&
                    (DateTime.UtcNow - _textureRequestTimeUtc).TotalSeconds >= 5.0)
                    SetCaption(_status, "No se encuentra '" + _textureDictionary + "'. Revisa la instalación del YTD.");
                else
                    SetCaption(_status, "Esperando carga del diccionario: " + _textureDictionary);

                UpdateText();
                DrawTextOnly();
                DrawHandLabels();
                return;
            }

            AdvanceComputer();

            if (!_spritesCreated)
                RefreshSprites();

            UpdateText();
            _scoreboard.Draw();
            _roundInfo.Draw();
            _announcement.Draw();
            DrawOpponentCardBacks();
            _handTitle.Draw();

            for (int i = 0; i < _playedLabels.Length; i++)
                _playedLabels[i].Draw();

            for (int i = 0; i < _playedSprites.Length; i++)
            {
                if (_playedCardAnimation != null && _playedCardAnimation.Slot == i)
                {
                    if (DrawPlayedCardAnimation())
                        continue;

                    _playedCardAnimation = null;
                }

                if (_playedSprites[i] != null)
                    _playedSprites[i].Draw();
            }

            for (int i = 0; i < _handSprites.Length; i++)
            {
                if (_handSprites[i] != null)
                {
                    if (!DrawDealingCard(i))
                        _handSprites[i].Draw();
                }

                if (!_dealAnimationActive || DateTime.UtcNow >= _dealAnimationStartUtc.AddMilliseconds(650))
                    _handLabels[i].Draw();
            }

            _status.Draw();
        }

        private void AdvanceComputer()
        {
            if (_match.IsComplete || _match.Round.IsComplete ||
                DateTime.UtcNow < _nextComputerActionUtc)
                return;

            TrucoRound round = _match.Round;
            if (round.HasPendingResponse)
            {
                if (round.PendingResponder == TrucoPlayer.Opponent)
                {
                    _nextComputerActionUtc = DateTime.UtcNow.AddSeconds(ComputerThinkSeconds);
                    ComputerRespond();
                    RefreshSprites();
                }
                return;
            }

            if (round.CurrentPlayer != TrucoPlayer.Opponent)
                return;

            _nextComputerActionUtc = DateTime.UtcNow.AddSeconds(ComputerThinkSeconds);

            int envido = round.EnvidoFor(TrucoPlayer.Opponent);
            if (round.CanCallEnvido(TrucoPlayer.Opponent))
            {
                TrucoCall? envidoCall = null;
                if (envido >= 32 && Math.Max(_match.PlayerScore, _match.OpponentScore) >= _match.TargetPoints - 5)
                    envidoCall = TrucoCall.FaltaEnvido;
                else if (envido >= 32)
                    envidoCall = TrucoCall.RealEnvido;
                else if (envido >= 29)
                    envidoCall = TrucoCall.Envido;

                if (envidoCall.HasValue && round.CallEnvido(TrucoPlayer.Opponent, envidoCall.Value))
                {
                    TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
                    return;
                }
            }

            int strength = round.HandStrength(TrucoPlayer.Opponent);
            if (round.CanCallTruco(TrucoPlayer.Opponent) && strength >= 28)
            {
                round.CallTruco(TrucoPlayer.Opponent);
                TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
                return;
            }

            // With a very weak hand facing an accepted raise, the computer may concede
            // instead of playing out a hand that is unlikely to recover.
            if (round.TrucoStake >= 2 && strength <= 12 && _random.NextDouble() < 0.30)
            {
                if (round.Fold(TrucoPlayer.Opponent))
                    TrucoAudio.Play(_soundEnabled, TrucoSound.Win);
                return;
            }

            PlayComputerCard();
        }

        private void ComputerRespond()
        {
            TrucoRound round = _match.Round;
            TrucoCall pending = round.PendingCall;
            bool accepted;

            if (pending == TrucoCall.Envido || pending == TrucoCall.RealEnvido ||
                pending == TrucoCall.FaltaEnvido)
            {
                int points = round.EnvidoFor(TrucoPlayer.Opponent);

                if (pending == TrucoCall.Envido && points >= 26 && _random.NextDouble() < 0.20 &&
                    round.CanRaiseEnvido(TrucoPlayer.Opponent, TrucoCall.Envido))
                {
                    round.CallEnvido(TrucoPlayer.Opponent, TrucoCall.Envido);
                    TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
                    return;
                }

                if (pending == TrucoCall.Envido && points >= 31 &&
                    round.CanRaiseEnvido(TrucoPlayer.Opponent, TrucoCall.RealEnvido))
                {
                    round.CallEnvido(TrucoPlayer.Opponent, TrucoCall.RealEnvido);
                    TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
                    return;
                }

                if ((pending == TrucoCall.Envido || pending == TrucoCall.RealEnvido) && points >= 32 &&
                    Math.Max(_match.PlayerScore, _match.OpponentScore) >= _match.TargetPoints - 5 &&
                    round.CanRaiseEnvido(TrucoPlayer.Opponent, TrucoCall.FaltaEnvido))
                {
                    round.CallEnvido(TrucoPlayer.Opponent, TrucoCall.FaltaEnvido);
                    TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
                    return;
                }

                if (pending == TrucoCall.RealEnvido && points >= 33 &&
                    round.CanRaiseEnvido(TrucoPlayer.Opponent, TrucoCall.FaltaEnvido))
                {
                    round.CallEnvido(TrucoPlayer.Opponent, TrucoCall.FaltaEnvido);
                    TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
                    return;
                }

                int threshold = round.PendingValue <= 2 ? 22 :
                                round.PendingValue <= 5 ? 27 : 31;
                if (_match.OpponentScore > _match.PlayerScore)
                    threshold -= 2;
                accepted = points >= threshold;
                round.RespondPending(TrucoPlayer.Opponent, accepted);
            }
            else if (pending == TrucoCall.Flor || pending == TrucoCall.ContraFlor ||
                     pending == TrucoCall.ContraFlorAlResto)
            {
                int flor = round.FlorFor(TrucoPlayer.Opponent);
                if (pending == TrucoCall.Flor && flor >= 30 &&
                    round.CanRaiseFlor(TrucoPlayer.Opponent, TrucoCall.ContraFlor))
                {
                    round.RaiseFlor(TrucoPlayer.Opponent, TrucoCall.ContraFlor);
                    TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
                    return;
                }

                if (pending == TrucoCall.ContraFlor && flor >= 32 &&
                    round.CanRaiseFlor(TrucoPlayer.Opponent, TrucoCall.ContraFlorAlResto))
                {
                    round.RaiseFlor(TrucoPlayer.Opponent, TrucoCall.ContraFlorAlResto);
                    TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
                    return;
                }

                accepted = flor >= 28;
                round.RespondPending(TrucoPlayer.Opponent, accepted);
            }
            else
            {
                int envido = round.EnvidoFor(TrucoPlayer.Opponent);
                if (round.CardsPlayed == 0 && round.CanCallEnvido(TrucoPlayer.Opponent) && envido >= 29)
                {
                    TrucoCall answer = envido >= 32 ? TrucoCall.RealEnvido : TrucoCall.Envido;
                    round.CallEnvido(TrucoPlayer.Opponent, answer);
                    TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
                    return;
                }

                int strength = round.HandStrength(TrucoPlayer.Opponent);
                int threshold = pending == TrucoCall.Truco ? 21 :
                                pending == TrucoCall.Retruco ? 29 : 36;
                if (_match.OpponentScore >= _match.TargetPoints - 3)
                    threshold += 2;

                if ((pending == TrucoCall.Truco || pending == TrucoCall.Retruco) &&
                    strength >= 34 && round.RaiseTruco(TrucoPlayer.Opponent))
                {
                    TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
                    return;
                }

                accepted = strength >= threshold;
                round.RespondPending(TrucoPlayer.Opponent, accepted);
            }

            if (_match.IsComplete || round.IsComplete)
                TrucoAudio.Play(_soundEnabled, TrucoSound.Win);
            else
                TrucoAudio.Play(_soundEnabled, accepted ? TrucoSound.Accept : TrucoSound.Reject);
        }

        private void PlayPlayerCard()
        {
            TrucoRound round = _match.Round;
            if (round.PlayerCards.Count == 0 ||
                _selectedCardIndex < 0 || _selectedCardIndex >= round.PlayerCards.Count)
                return;

            try
            {
                TrucoCard playedCard = round.PlayerCards[_selectedCardIndex];
                PointF from = _handPositions[_selectedCardIndex];
                round.PlayCard(TrucoPlayer.Player, _selectedCardIndex);
                StartPlayedCardAnimation(TrucoPlayer.Player, playedCard, from);
                _selectedCardIndex = Math.Min(_selectedCardIndex, Math.Max(0, round.PlayerCards.Count - 1));
                _nextComputerActionUtc = DateTime.UtcNow.AddSeconds(ComputerThinkSeconds);
                RefreshSprites();
                TrucoAudio.Play(_soundEnabled,
                    (_match.IsComplete || round.IsComplete) ? TrucoSound.Win : TrucoSound.Select);
            }
            catch (Exception ex)
            {
                SetCaption(_status, "No se pudo jugar la carta: " + ex.Message);
            }
        }

        private void PlayComputerCard()
        {
            TrucoRound round = _match.Round;
            IList<TrucoCard> cards = round.OpponentCards;
            if (cards.Count == 0) return;

            int cardIndex = ChooseComputerCard(round, cards);
            TrucoCard playedCard = cards[cardIndex];
            PointF from = LayoutPoint(905.0f, 205.0f);
            round.PlayCard(TrucoPlayer.Opponent, cardIndex);
            StartPlayedCardAnimation(TrucoPlayer.Opponent, playedCard, from);
            RefreshSprites();
            TrucoAudio.Play(_soundEnabled,
                (_match.IsComplete || round.IsComplete) ? TrucoSound.Win : TrucoSound.Select);
        }

        private int ChooseComputerCard(TrucoRound round, IList<TrucoCard> cards)
        {
            int weakestIndex = 0;
            int weakestPower = int.MaxValue;
            int winningIndex = -1;
            int winningPower = int.MaxValue;

            for (int i = 0; i < cards.Count; i++)
            {
                int power = TrucoRules.TrucoPower(cards[i]);
                if (power < weakestPower)
                {
                    weakestPower = power;
                    weakestIndex = i;
                }

                if (round.PendingCard != null && power > TrucoRules.TrucoPower(round.PendingCard) &&
                    power < winningPower)
                {
                    winningPower = power;
                    winningIndex = i;
                }
            }

            // If responding to a card, spend the weakest card that wins; otherwise shed the weakest.
            return winningIndex >= 0 ? winningIndex : weakestIndex;
        }

        private bool DrawDealingCard(int index)
        {
            if (!_dealAnimationActive || _match == null || index < 0 ||
                index >= _match.Round.PlayerCards.Count)
                return false;

            DateTime now = DateTime.UtcNow;
            double elapsed = (now - _dealAnimationStartUtc).TotalSeconds - index * 0.11;
            const double duration = 0.32;
            if (elapsed >= duration)
            {
                if (now >= _dealAnimationStartUtc.AddMilliseconds(650))
                    _dealAnimationActive = false;
                return false;
            }

            if (elapsed < 0)
                return true;

            float t = Ease((float)(elapsed / duration));
            PointF target = _handPositions[index];
            PointF source = LayoutPoint(905.0f, 365.0f);
            PointF position = Lerp(source, target, t);
            float width = Lerp(LayoutLength(72.0f), LayoutLength(HandCardWidth), t);
            float height = Lerp(LayoutLength(108.0f), LayoutLength(HandCardHeight), t);
            DrawTransientSprite(
                _match.Round.PlayerCards[index].TextureName,
                new SizeF(width, height),
                position);
            return true;
        }

        private bool DrawPlayedCardAnimation()
        {
            CardAnimation animation = _playedCardAnimation;
            if (animation == null)
                return false;

            double elapsed = (DateTime.UtcNow - animation.StartedUtc).TotalSeconds;
            if (elapsed >= animation.DurationSeconds)
                return false;

            float t = Ease((float)(elapsed / animation.DurationSeconds));
            PointF position = Lerp(animation.From, animation.To, t);
            float width = Lerp(animation.FromWidth, animation.ToWidth, t);
            float height = Lerp(animation.FromHeight, animation.ToHeight, t);
            DrawTransientSprite(animation.TextureName, new SizeF(width, height), position);
            return true;
        }

        private void StartPlayedCardAnimation(TrucoPlayer player, TrucoCard card, PointF from)
        {
            if (card == null || _match == null)
                return;

            TrucoRound round = _match.Round;
            int cardCount = round.CardsPlayed;
            int trickIndex = Math.Max(0, (cardCount - 1) / 2);
            int slot = trickIndex * 2 + (player == TrucoPlayer.Player ? 0 : 1);
            if (slot < 0 || slot >= _playedPositions.Length)
                return;

            PointF to = _playedPositions[slot];
            bool isPlayer = player == TrucoPlayer.Player;
            _playedCardAnimation = new CardAnimation
            {
                Slot = slot,
                TextureName = card.TextureName,
                From = from,
                To = to,
                FromWidth = LayoutLength(isPlayer ? HandCardWidth : 78.0f),
                FromHeight = LayoutLength(isPlayer ? HandCardHeight : 117.0f),
                ToWidth = LayoutLength(PlayedCardWidth),
                ToHeight = LayoutLength(PlayedCardHeight),
                StartedUtc = DateTime.UtcNow,
                DurationSeconds = 0.28
            };
        }

        private void DrawTransientSprite(string textureName, SizeF size, PointF position)
        {
            Sprite transient = null;
            try
            {
                transient = new Sprite(_textureDictionary, textureName, size, position);
                transient.Draw();
            }
            finally
            {
                if (transient != null)
                    transient.Dispose();
            }
        }

        private static float LayoutScale
        {
            get
            {
                float width = RDR2.UI.Screen.Width;
                float height = RDR2.UI.Screen.Height;
                if (width <= 0.0f || height <= 0.0f)
                    return 1.0f;

                float scaleX = width / DesignWidth;
                float scaleY = height / DesignHeight;
                return Math.Min(scaleX, scaleY);
            }
        }

        private static PointF LayoutPoint(float x, float y)
        {
            float scale = LayoutScale;
            float width = RDR2.UI.Screen.Width;
            float height = RDR2.UI.Screen.Height;
            float offsetX = (width - DesignWidth * scale) / 2.0f;
            float offsetY = (height - DesignHeight * scale) / 2.0f;
            return new PointF(offsetX + x * scale, offsetY + y * scale);
        }

        private static SizeF LayoutSize(float width, float height)
        {
            float scale = LayoutScale;
            return new SizeF(width * scale, height * scale);
        }

        private static float LayoutLength(float value)
        {
            return value * LayoutScale;
        }

        private static float LayoutTextScale(float value)
        {
            return value * LayoutScale;
        }

        private void DrawMenuSurface()
        {
            PointF center = LayoutPoint(960.0f, 535.0f);
            SizeF outer = LayoutSize(790.0f, 330.0f);
            SizeF inner = LayoutSize(770.0f, 310.0f);
            DrawRect(center.X, center.Y, outer.Width, outer.Height, 24, 19, 15, 235);
            DrawRect(center.X, center.Y, inner.Width, inner.Height, 46, 35, 24, 238);
            DrawRect(center.X, center.Y - LayoutLength(145.0f), LayoutLength(620.0f), LayoutLength(2.0f), 193, 157, 91, 255);
            DrawRect(center.X, center.Y + LayoutLength(145.0f), LayoutLength(620.0f), LayoutLength(2.0f), 193, 157, 91, 255);
        }

        private void DrawTopPanel()
        {
            PointF center = LayoutPoint(960.0f, 188.0f);
            SizeF outer = LayoutSize(1420.0f, 185.0f);
            SizeF inner = LayoutSize(1404.0f, 169.0f);
            DrawRect(center.X, center.Y, outer.Width, outer.Height, 18, 16, 13, 220);
            DrawRect(center.X, center.Y, inner.Width, inner.Height, 32, 27, 21, 218);
            DrawRect(center.X, center.Y - LayoutLength(82.0f), LayoutLength(1320.0f), LayoutLength(2.0f), 168, 133, 76, 235);
        }

        private void DrawTableSurface()
        {
            PointF center = LayoutPoint(960.0f, 480.0f);
            SizeF outer = LayoutSize(840.0f, 225.0f);
            SizeF middle = LayoutSize(824.0f, 209.0f);
            SizeF inner = LayoutSize(806.0f, 191.0f);
            DrawRect(center.X, center.Y, outer.Width, outer.Height, 27, 19, 13, 240);
            DrawRect(center.X, center.Y, middle.Width, middle.Height, 147, 103, 54, 255);
            DrawRect(center.X, center.Y, inner.Width, inner.Height, 31, 78, 54, 235);
            DrawRect(center.X, center.Y, LayoutLength(2.0f), LayoutLength(165.0f), 151, 190, 128, 115);
        }

        private void DrawOpponentCardBacks()
        {
            if (_match == null || _match.Round == null)
                return;

            int count = Math.Min(3, _match.Round.OpponentCards.Count);
            SizeF cardSize = LayoutSize(OpponentBackWidth, OpponentBackHeight);
            for (int i = 0; i < count; i++)
            {
                PointF position = _opponentCardBackPositions[i];
                float centerX = position.X + cardSize.Width / 2.0f;
                float centerY = position.Y + cardSize.Height / 2.0f;

                DrawRect(centerX, centerY, cardSize.Width, cardSize.Height, 22, 20, 26, 245);
                DrawRect(centerX, centerY, cardSize.Width - LayoutLength(5.0f), cardSize.Height - LayoutLength(5.0f), 151, 104, 51, 255);
                DrawRect(centerX, centerY, cardSize.Width - LayoutLength(10.0f), cardSize.Height - LayoutLength(10.0f), 42, 42, 74, 255);
                DrawRect(centerX, centerY, LayoutLength(4.0f), cardSize.Height - LayoutLength(18.0f), 202, 170, 100, 255);
                DrawRect(centerX, centerY, cardSize.Width - LayoutLength(18.0f), LayoutLength(4.0f), 202, 170, 100, 255);
                DrawRect(centerX, centerY, LayoutLength(15.0f), LayoutLength(15.0f), 218, 193, 135, 255);
                DrawRect(centerX, centerY, LayoutLength(9.0f), LayoutLength(9.0f), 42, 42, 74, 255);
            }
        }

        private static void DrawRect(float centerX, float centerY, float width, float height,
            int red, int green, int blue, int alpha)
        {
            float screenWidth = RDR2.UI.Screen.Width;
            float screenHeight = RDR2.UI.Screen.Height;
            if (screenWidth <= 0.0f || screenHeight <= 0.0f)
                return;

            GRAPHICS.DRAW_RECT(
                centerX / screenWidth,
                centerY / screenHeight,
                width / screenWidth,
                height / screenHeight,
                red, green, blue, alpha, false, false);
        }

        private static float Ease(float t)
        {
            if (t < 0.0f) t = 0.0f;
            if (t > 1.0f) t = 1.0f;
            return t * t * (3.0f - 2.0f * t);
        }

        private static PointF Lerp(PointF from, PointF to, float t)
        {
            return new PointF(Lerp(from.X, to.X, t), Lerp(from.Y, to.Y, t));
        }

        private static float Lerp(float from, float to, float t)
        {
            return from + (to - from) * t;
        }

        private void RefreshSprites()
        {
            DisposeSprites();
            if (_match == null || _match.Round == null || !_textureDictionaryLoaded)
            {
                _spritesCreated = false;
                return;
            }

            TrucoRound round = _match.Round;
            IList<TrucoCard> playerCards = round.PlayerCards;
            for (int i = 0; i < playerCards.Count && i < _handSprites.Length; i++)
            {
                _handSprites[i] = new Sprite(
                    _textureDictionary, playerCards[i].TextureName,
                    LayoutSize(HandCardWidth, HandCardHeight), _handPositions[i]);
            }

            foreach (TrucoTrick trick in round.Tricks)
            {
                int trickIndex = trick.Number - 1;
                SetPlayedSprite(trickIndex, TrucoPlayer.Player,
                    trick.FirstPlayer == TrucoPlayer.Player ? trick.FirstCard : trick.SecondCard);
                SetPlayedSprite(trickIndex, TrucoPlayer.Opponent,
                    trick.FirstPlayer == TrucoPlayer.Opponent ? trick.FirstCard : trick.SecondCard);
            }

            if (round.PendingCard != null && round.PendingPlayer.HasValue)
                SetPlayedSprite(round.Tricks.Count, round.PendingPlayer.Value, round.PendingCard);

            _spritesCreated = true;
        }

        private void SetPlayedSprite(int trickIndex, TrucoPlayer player, TrucoCard card)
        {
            if (trickIndex < 0 || trickIndex >= 3 || card == null) return;
            int slot = trickIndex * 2 + (player == TrucoPlayer.Player ? 0 : 1);
            _playedSprites[slot] = new Sprite(
                _textureDictionary, card.TextureName,
                LayoutSize(PlayedCardWidth, PlayedCardHeight), _playedPositions[slot]);
        }

        private void UpdateText()
        {
            TrucoRound round = _match.Round;

            SetCaption(_scoreboard,
                "VOS  " + _match.PlayerScore + "   -   " + _match.OpponentScore + "  RIVAL" +
                "       |       " + _match.TargetPoints + " PUNTOS       |       " + _match.ScorePhase);

            string first = round.Tricks.Count > 0 ? FormatTrick(round.Tricks[0]) : "-";
            string second = round.Tricks.Count > 1 ? FormatTrick(round.Tricks[1]) : "-";
            string third = round.Tricks.Count > 2 ? FormatTrick(round.Tricks[2]) : "-";
            SetCaption(_roundInfo,
                "MANO: " + (round.Mano == TrucoPlayer.Player ? "VOS" : "RIVAL") +
                "    |    RIVAL: " + round.OpponentCards.Count + " ocultas" +
                "    |    BAZAS: " + first + " / " + second + " / " + third +
                "    |    TRUCO: " + round.TrucoStake + " punto(s)" +
                "    |    FLOR: " + (_florEnabled ? "SI" : "NO"));

            string announcement = round.LastAnnouncement;
            if (!string.IsNullOrEmpty(_match.LastScoreEvent) &&
                (_match.IsComplete || round.IsComplete))
                announcement = _match.LastScoreEvent;
            SetCaption(_announcement, announcement);

            SetCaption(_status, GetStatusText());
            UpdateHandLabels();
        }

        private string GetStatusText()
        {
            TrucoRound round = _match.Round;

            if (_match.IsComplete)
                return (_match.Winner == TrucoPlayer.Player ? "GANASTE LA PARTIDA" : "EL RIVAL GANO LA PARTIDA") +
                       ". ENTER: volver al menú.";
            if (round.IsComplete)
                return (round.Winner == TrucoPlayer.Player ? "Ganaste la mano" : "El rival ganó la mano") +
                       " (" + round.PointsAwardedForHand + " puntos). ENTER: siguiente mano.";

            if (round.HasPendingResponse && round.PendingResponder == TrucoPlayer.Player)
            {
                bool isTrucoOffer = round.PendingCall == TrucoCall.Truco ||
                                    round.PendingCall == TrucoCall.Retruco ||
                                    round.PendingCall == TrucoCall.ValeCuatro;
                string offerValue = isTrucoOffer
                    ? "por " + (round.PendingValue + 1) + " puntos (no querido: " + round.PendingValue + ")"
                    : round.PendingValue + " punto(s)";
                string response = "RIVAL CANTA " + TrucoRound.CallName(round.PendingCall) +
                                  " (" + offerValue + "). Q=QUIERO / N=NO QUIERO";
                if (round.PendingCall == TrucoCall.Envido)
                    response += " / E, R o F para subir";
                else if (round.PendingCall == TrucoCall.RealEnvido)
                    response += " / F para Falta";
                else if (round.PendingCall == TrucoCall.Truco || round.PendingCall == TrucoCall.Retruco)
                {
                    response += " / T para subir";
                    if (round.CardsPlayed == 0)
                        response += " / E-R-F para cantar envido primero";
                }
                else if (round.PendingCall == TrucoCall.Flor)
                    response += " / C contra flor";
                else if (round.PendingCall == TrucoCall.ContraFlor)
                    response += " / B contra flor al resto";
                return response;
            }

            if (round.HasPendingResponse)
                return "Esperando respuesta del rival...";

            if (round.CurrentPlayer == TrucoPlayer.Player)
                return "TU TURNO: flechas + ENTER | E ENVIDO | R REAL | F FALTA | T TRUCO | M MAZO";
            return "Turno del rival...";
        }

        private void UpdateHandLabels()
        {
            IList<TrucoCard> cards = _match == null ? null : _match.Round.PlayerCards;
            for (int i = 0; i < _handLabels.Length; i++)
            {
                string caption = string.Empty;
                if (cards != null && i < cards.Count)
                {
                    string prefix = i == _selectedCardIndex ? "> " : "  ";
                    caption = prefix + (i + 1) + ". " + FormatCardName(cards[i]);
                }
                SetCaption(_handLabels[i], caption);
            }
        }

        private static string FormatCardName(TrucoCard card)
        {
            if (card == null) return string.Empty;

            string rank;
            switch (card.Rank)
            {
                case 1: rank = "As"; break;
                case 2: rank = "Dos"; break;
                case 3: rank = "Tres"; break;
                case 4: rank = "Cuatro"; break;
                case 5: rank = "Cinco"; break;
                case 6: rank = "Seis"; break;
                case 7: rank = "Siete"; break;
                case 10: rank = "Sota"; break;
                case 11: rank = "Caballo"; break;
                case 12: rank = "Rey"; break;
                default: rank = card.Rank.ToString(); break;
            }

            return rank + " de " + card.Suit.ToString().ToLowerInvariant();
        }

        private void DrawTextOnly()
        {
            UpdateText();
            _scoreboard.Draw();
            _roundInfo.Draw();
            _announcement.Draw();
            DrawOpponentCardBacks();
            _handTitle.Draw();
            _status.Draw();
        }

        private void DrawHandLabels()
        {
            for (int i = 0; i < _handLabels.Length; i++)
                _handLabels[i].Draw();
        }

        private static string FormatTrick(TrucoTrick trick)
        {
            if (trick.IsParda) return "parda";
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
            if (element != null && !string.Equals(element.Caption, caption, StringComparison.Ordinal))
                element.Caption = caption;
        }
    }
}