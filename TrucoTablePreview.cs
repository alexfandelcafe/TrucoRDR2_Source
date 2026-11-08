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
        private const float HandCardWidth = 160.0f;
        private const float HandCardHeight = 240.0f;
        private const float HandTop = 700.0f;
        private const float PlayedCardWidth = 108.0f;
        private const float PlayedCardHeight = 162.0f;
        private const float OpponentBackWidth = 64.0f;
        private const float OpponentBackHeight = 96.0f;
        private const string OpponentBackTextureName = "reverso";
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
        private readonly TrucoWorldInteraction _worldInteraction;
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
        private readonly TextElement _roundDetails;
        private readonly TextElement _announcement;
        private readonly TextElement _handTitle;
        private readonly TextElement _status;
        private readonly TextElement _menuOptions;
        private readonly TextElement _menuHint;
        private readonly TextElement[] _handLabels = new TextElement[3];
        private readonly TextElement[] _playedLabels = new TextElement[6];
        private readonly Sprite[] _handSprites = new Sprite[3];
        private readonly Sprite[] _opponentRevealSprites = new Sprite[3];
        private readonly Sprite[] _opponentBackSprites = new Sprite[3];
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
            int targetPoints, bool florEnabled, bool soundEnabled,
            TrucoWorldInteraction worldInteraction)
        {
            if (string.IsNullOrEmpty(textureDictionary))
                throw new ArgumentException("El nombre del diccionario no puede estar vacío.", "textureDictionary");
            if (targetPoints != 15 && targetPoints != 30)
                throw new ArgumentOutOfRangeException("targetPoints");

            _textureDictionary = textureDictionary;
            _worldInteraction = worldInteraction;
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
                LayoutPoint(740.0f, 285.0f),
                LayoutPoint(928.0f, 285.0f),
                LayoutPoint(1116.0f, 285.0f)
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
                LayoutPoint(960.0f, 198.0f), LayoutTextScale(0.36f), Color.WhiteSmoke,
                Alignment.Center, true, true);

            _roundDetails = new TextElement(
                string.Empty,
                LayoutPoint(960.0f, 225.0f), LayoutTextScale(0.36f), Color.WhiteSmoke,
                Alignment.Center, true, true);

            _announcement = new TextElement(
                string.Empty,
                LayoutPoint(960.0f, 252.0f), LayoutTextScale(0.34f), Color.Wheat,
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
                    LayoutPoint(650.0f + i * 230.0f + HandCardWidth / 2.0f, 963.0f),
                    LayoutTextScale(0.34f), Color.WhiteSmoke, Alignment.Center, true, true);
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

            SetHeaderForMenu(_showMenu);
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
            SetHeaderForMenu(false);
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
                "ARRIBA/ABAJO: elegir   IZQUIERDA/DERECHA: cambiar\nENTER: confirmar   ESC: cerrar");
        }

        public void ContinueAfterResult()
        {
            if (_match == null) return;

            if (_match.IsComplete)
            {
                _match = null;
                _showMenu = true;
                SetHeaderForMenu(true);
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
                    DelayComputerAction();
                    RefreshSprites();
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
                    DelayComputerAction();
                    RefreshSprites();
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
                {
                    DelayComputerAction();
                    TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
                }
            }
            else if (e.KeyCode == Keys.C)
            {
                if (round.RaiseFlor(TrucoPlayer.Player, TrucoCall.ContraFlor))
                {
                    DelayComputerAction();
                    TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
                }
            }
            else if (e.KeyCode == Keys.B)
            {
                if (round.RaiseFlor(TrucoPlayer.Player, TrucoCall.ContraFlorAlResto))
                {
                    DelayComputerAction();
                    TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
                }
            }

            if (e.KeyCode == Keys.E || e.KeyCode == Keys.R || e.KeyCode == Keys.F ||
                e.KeyCode == Keys.T || e.KeyCode == Keys.C || e.KeyCode == Keys.B)
                e.Handled = true;
        }

        private void TryCallEnvido(TrucoCall call)
        {
            if (_match.Round.CallEnvido(TrucoPlayer.Player, call))
            {
                DelayComputerAction();
                TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
            }
        }

        private void TryRaiseEnvido(TrucoCall call)
        {
            if (_match.Round.CallEnvido(TrucoPlayer.Player, call))
            {
                DelayComputerAction();
                TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
            }
        }

        private void TryCallTruco()
        {
            if (_match.Round.CallTruco(TrucoPlayer.Player))
            {
                DelayComputerAction();
                TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
            }
        }

        public void Draw()
        {
            if (!_visible)
                return;

            // Set the script graphics layer once per frame; changing it for each text element
            // can interfere with other RDR2 UI layers.
            try
            {
                GRAPHICS.SET_SCRIPT_GFX_DRAW_ORDER(3);
            }
            catch
            {
                // Rendering should continue if this optional layer adjustment is unavailable.
            }

            // Keep the table open through minor damage or bumps while the player is walking
            // into the chair. Only death interrupts the overlay; a small health drop must not
            // close the menu during the seating animation.
            try
            {
                if (RDR2.Game.Player.IsDead)
                {
                    Close();
                    RDR2.UI.Screen.PrintSubtitle("Partida interrumpida porque el jugador murió.");
                    return;
                }
            }
            catch
            {
                // Some game states do not expose a valid player ped; keep rendering in that case.
            }

            try
            {
                // Disable movement/combat, camera and frontend controls while the menu/table is open.
                PAD.DISABLE_ALL_CONTROL_ACTIONS(0);
                PAD.DISABLE_ALL_CONTROL_ACTIONS(1);
                PAD.DISABLE_ALL_CONTROL_ACTIONS(2);
            }
            catch
            {
                // Input suppression is best-effort and must not stop the table from rendering.
            }

            if (_showMenu)
            {
                DrawMenuSurface();
                DrawTextElement(_header);
                DrawTextElement(_menuOptions);
                DrawTextElement(_menuHint);
                return;
            }

            if (_match == null)
                return;

            DrawTopPanel();
            DrawTableSurface();

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
                return;
            }

            AdvanceComputer();

            if (!_spritesCreated)
                RefreshSprites();

            UpdateText();
            DrawOpponentCardBacks();
            for (int i = 0; i < _opponentRevealSprites.Length; i++)
                if (_opponentRevealSprites[i] != null)
                    _opponentRevealSprites[i].Draw();

            // Add dark label plates first, then draw all text after card sprites so it cannot
            // flicker underneath or be covered by a sprite rendered later in the frame.

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
                if (i == _selectedCardIndex && !_dealAnimationActive &&
                    _match.Round.CurrentPlayer == TrucoPlayer.Player &&
                    !_match.Round.IsComplete && !_match.Round.HasPendingResponse)
                    DrawSelectedCardFrame(i);

                if (_handSprites[i] != null)
                {
                    if (!DrawDealingCard(i))
                        _handSprites[i].Draw();
                }
            }

            DrawTextElement(_header);
            DrawTextElement(_instructions);
            DrawTextElement(_scoreboard);
            DrawTextElement(_roundInfo);
            DrawTextElement(_roundDetails);
            DrawTextElement(_announcement);
            DrawTextElement(_handTitle);

            for (int i = 0; i < _playedLabels.Length; i++)
                DrawTextElement(_playedLabels[i]);

            if (!_dealAnimationActive || DateTime.UtcNow >= _dealAnimationStartUtc.AddMilliseconds(650))
            {
                for (int i = 0; i < _handLabels.Length; i++)
                    DrawTextElement(_handLabels[i]);
            }

            DrawTextElement(_status);
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

            // Envido is evaluated separately: truco strength and envido points are
            // different resources and should not use the same threshold.
            int envido = round.EnvidoFor(TrucoPlayer.Opponent);
            if (round.CanCallEnvido(TrucoPlayer.Opponent))
            {
                TrucoCall? call = ChooseOpeningEnvidoCall(envido);
                if (call.HasValue && round.CallEnvido(TrucoPlayer.Opponent, call.Value))
                {
                    TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
                    return;
                }
            }

            int equity = EstimateTrucoEquity(round);
            int callThreshold = 68 + GetScoreRiskModifier();
            if (round.TrucoLevel == 1)
                callThreshold += 8;
            else if (round.TrucoLevel >= 2)
                callThreshold += 15;

            if (round.CanCallTruco(TrucoPlayer.Opponent) && equity >= callThreshold)
            {
                round.CallTruco(TrucoPlayer.Opponent);
                TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
                return;
            }

            PlayComputerCard();
        }

        private TrucoCall? ChooseOpeningEnvidoCall(int points)
        {
            int target = _match.TargetPoints;
            int highestScore = Math.Max(_match.PlayerScore, _match.OpponentScore);
            bool nearFinish = highestScore >= target - 5;
            int risk = GetScoreRiskModifier();

            // A Falta Envido is high variance. Prefer it near the finish with a
            // very strong hand, unless the score says the computer should protect a lead.
            if (points >= 32 && nearFinish && (risk <= 0 || points == 33))
                return TrucoCall.FaltaEnvido;

            if (points >= 31)
                return TrucoCall.RealEnvido;

            if (points >= 28)
                return TrucoCall.Envido;

            // When substantially behind, the computer occasionally contests a
            // marginal envido instead of playing passively every hand.
            if (points >= 26 && risk <= -5 && _random.NextDouble() < 0.28)
                return TrucoCall.Envido;

            return null;
        }

        private void ComputerRespond()
        {
            TrucoRound round = _match.Round;
            TrucoCall pending = round.PendingCall;
            bool accepted = false;
            int risk = GetScoreRiskModifier();

            if (pending == TrucoCall.Envido || pending == TrucoCall.RealEnvido ||
                pending == TrucoCall.FaltaEnvido)
            {
                int points = round.EnvidoFor(TrucoPlayer.Opponent);
                bool nearFinish = Math.Max(_match.PlayerScore, _match.OpponentScore) >=
                                  _match.TargetPoints - 5;

                if (pending != TrucoCall.FaltaEnvido && points >= 32 && nearFinish &&
                    (risk <= 2 || points == 33) &&
                    round.CanRaiseEnvido(TrucoPlayer.Opponent, TrucoCall.FaltaEnvido))
                {
                    round.CallEnvido(TrucoPlayer.Opponent, TrucoCall.FaltaEnvido);
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

                if (pending == TrucoCall.Envido && points >= 29 && risk <= -5 &&
                    _random.NextDouble() < 0.35 &&
                    round.CanRaiseEnvido(TrucoPlayer.Opponent, TrucoCall.Envido))
                {
                    round.CallEnvido(TrucoPlayer.Opponent, TrucoCall.Envido);
                    TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
                    return;
                }

                // Accept based on estimated chance to win rather than a single,
                // fixed point cutoff. Larger stakes require more confidence.
                double winChance = EstimateEnvidoWinChance(points);
                double requiredChance = round.PendingValue <= 2 ? 0.24 :
                                        round.PendingValue <= 3 ? 0.32 :
                                        round.PendingValue <= 5 ? 0.40 : 0.49;
                requiredChance += risk * 0.004;
                requiredChance = Clamp(requiredChance, 0.12, 0.82);
                accepted = winChance >= requiredChance;
                round.RespondPending(TrucoPlayer.Opponent, accepted);
            }
            else if (pending == TrucoCall.Flor || pending == TrucoCall.ContraFlor ||
                     pending == TrucoCall.ContraFlorAlResto)
            {
                int flor = round.FlorFor(TrucoPlayer.Opponent);
                bool nearFinish = Math.Max(_match.PlayerScore, _match.OpponentScore) >=
                                  _match.TargetPoints - 5;

                if (pending == TrucoCall.Flor && flor >= 33 && risk <= 8 &&
                    round.CanRaiseFlor(TrucoPlayer.Opponent, TrucoCall.ContraFlor))
                {
                    round.RaiseFlor(TrucoPlayer.Opponent, TrucoCall.ContraFlor);
                    TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
                    return;
                }

                if (pending == TrucoCall.ContraFlor && flor >= 35 && nearFinish &&
                    (risk <= 0 || flor >= 38) &&
                    round.CanRaiseFlor(TrucoPlayer.Opponent, TrucoCall.ContraFlorAlResto))
                {
                    round.RaiseFlor(TrucoPlayer.Opponent, TrucoCall.ContraFlorAlResto);
                    TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
                    return;
                }

                double winChance = EstimateFlorWinChance(flor);
                double requiredChance = pending == TrucoCall.Flor ? 0.16 :
                                        pending == TrucoCall.ContraFlor ? 0.27 : 0.41;
                requiredChance += risk * 0.004;
                requiredChance = Clamp(requiredChance, 0.08, 0.80);
                accepted = winChance >= requiredChance;
                round.RespondPending(TrucoPlayer.Opponent, accepted);
            }
            else
            {
                // Envido may still be sung in response to a Truco before the first
                // card. The round engine suspends and restores the truco offer.
                int envido = round.EnvidoFor(TrucoPlayer.Opponent);
                if (round.CardsPlayed == 0 && round.CanCallEnvido(TrucoPlayer.Opponent) &&
                    envido >= 29 && (risk <= 5 || envido >= 32))
                {
                    TrucoCall answer = envido >= 32 ? TrucoCall.RealEnvido : TrucoCall.Envido;
                    if (round.CallEnvido(TrucoPlayer.Opponent, answer))
                    {
                        TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
                        return;
                    }
                }

                int equity = EstimateTrucoEquity(round);
                int threshold = pending == TrucoCall.Truco ? 42 :
                                pending == TrucoCall.Retruco ? 56 : 68;
                threshold += risk;

                int raiseThreshold = pending == TrucoCall.Truco ? 86 : 92;
                raiseThreshold += Math.Max(0, risk);
                if ((pending == TrucoCall.Truco || pending == TrucoCall.Retruco) &&
                    equity >= raiseThreshold &&
                    round.RaiseTruco(TrucoPlayer.Opponent))
                {
                    TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
                    return;
                }

                accepted = equity >= threshold;
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
                if (_worldInteraction != null)
                    _worldInteraction.PlayCardDealAnimation(true);
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

        private void DelayComputerAction()
        {
            _nextComputerActionUtc = DateTime.UtcNow.AddSeconds(ComputerThinkSeconds);
        }

        private void PlayComputerCard()
        {
            TrucoRound round = _match.Round;
            IList<TrucoCard> cards = round.OpponentCards;
            if (cards.Count == 0) return;

            int cardIndex = ChooseComputerCard(round, cards);
            TrucoCard playedCard = cards[cardIndex];
            PointF from = _opponentCardBackPositions[Math.Min(cardIndex, _opponentCardBackPositions.Length - 1)];
            round.PlayCard(TrucoPlayer.Opponent, cardIndex);
            if (_worldInteraction != null)
                _worldInteraction.PlayCardDealAnimation(false);
            StartPlayedCardAnimation(TrucoPlayer.Opponent, playedCard, from);
            RefreshSprites();
            TrucoAudio.Play(_soundEnabled,
                (_match.IsComplete || round.IsComplete) ? TrucoSound.Win : TrucoSound.Select);
        }

        private int ChooseComputerCard(TrucoRound round, IList<TrucoCard> cards)
        {
            int weakestIndex = 0;
            int weakestPower = int.MaxValue;
            int strongestIndex = 0;
            int strongestPower = int.MinValue;
            int secondStrongestIndex = -1;
            int secondStrongestPower = int.MinValue;
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
                    secondStrongestPower = strongestPower;
                    secondStrongestIndex = strongestIndex;
                    strongestPower = power;
                    strongestIndex = i;
                }
                else if (power > secondStrongestPower)
                {
                    secondStrongestPower = power;
                    secondStrongestIndex = i;
                }

                if (round.PendingCard != null)
                {
                    // Spend the cheapest card that can win the current trick.
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
                // A tie is useful only when the current hand state favours the
                // computer on a parda. Otherwise prefer a low-cost winning card.
                if (IsPardaFavorableToComputer(round) && tyingIndex >= 0)
                    return tyingIndex;

                if (winningIndex >= 0)
                    return winningIndex;

                // Nothing can beat the exposed card: keep the strongest cards.
                return weakestIndex;
            }

            if (MustWinNextTrick(round))
                return strongestIndex;

            if (round.Tricks.Count == 0 && cards.Count >= 3)
            {
                // Open with a middle-strength card when the hand has a reliable
                // top card, preserving it as a possible response later.
                if (strongestPower >= 12 && secondStrongestPower >= 8)
                    return secondStrongestIndex;
                if (strongestPower >= 9 && secondStrongestPower >= 7)
                    return secondStrongestIndex;
                return weakestIndex;
            }

            // If the computer won the first trick, a cheap lead keeps its edge
            // while preserving cards for a possible third trick.
            if (round.Tricks.Count == 1 && round.Tricks[0].Winner == TrucoPlayer.Opponent)
                return weakestIndex;

            // If all of the first two tricks were pardas and the computer is mano,
            // a tie is already favourable; lead cheaply and make the player respond.
            if (round.Tricks.Count == 1 && round.Tricks[0].IsParda &&
                round.Mano == TrucoPlayer.Opponent)
                return weakestIndex;

            return weakestIndex;
        }

        private static bool IsPardaFavorableToComputer(TrucoRound round)
        {
            if (round.Tricks.Count == 0)
                return round.Mano == TrucoPlayer.Opponent;

            if (round.Tricks.Count == 1)
            {
                TrucoTrick first = round.Tricks[0];
                if (first.Winner == TrucoPlayer.Opponent)
                    return true;
                if (first.Winner == TrucoPlayer.Player)
                    return false;
                return round.Mano == TrucoPlayer.Opponent;
            }

            if (round.Tricks.Count == 2)
            {
                TrucoTrick first = round.Tricks[0];
                TrucoTrick second = round.Tricks[1];
                if (first.Winner == TrucoPlayer.Opponent &&
                    second.Winner == TrucoPlayer.Player)
                    return true;
                if (first.Winner == TrucoPlayer.Player &&
                    second.Winner == TrucoPlayer.Opponent)
                    return false;
                if (first.IsParda && second.IsParda)
                    return round.Mano == TrucoPlayer.Opponent;
            }

            return false;
        }

        private static bool MustWinNextTrick(TrucoRound round)
        {
            if (round.Tricks.Count < 2)
                return false;

            TrucoTrick first = round.Tricks[0];
            TrucoTrick second = round.Tricks[1];

            // Once each player has taken one trick, the third decides the hand.
            if (first.Winner.HasValue && second.Winner.HasValue &&
                first.Winner.Value != second.Winner.Value)
                return true;

            // If player mano is tied twice, the computer must break the third parda.
            if (first.IsParda && second.IsParda && round.Mano != TrucoPlayer.Opponent)
                return true;

            return false;
        }

        private int EstimateTrucoEquity(TrucoRound round)
        {
            int equity = EstimateCardPotential(round.OpponentCards);

            foreach (TrucoTrick trick in round.Tricks)
            {
                if (trick.Winner == TrucoPlayer.Opponent)
                    equity += 11;
                else if (trick.Winner == TrucoPlayer.Player)
                    equity -= 11;
                else
                    equity += round.Mano == TrucoPlayer.Opponent ? 3 : -3;
            }

            // Only use information visible to the opponent: a card already played
            // face-up by the player. Never inspect the player's unplayed cards.
            if (round.PendingCard != null && round.PendingPlayer == TrucoPlayer.Player)
            {
                int pendingPower = TrucoRules.TrucoPower(round.PendingCard);
                int bestPower = 0;
                bool canTie = false;
                foreach (TrucoCard card in round.OpponentCards)
                {
                    int power = TrucoRules.TrucoPower(card);
                    if (power > bestPower)
                        bestPower = power;
                    if (power == pendingPower)
                        canTie = true;
                }

                if (bestPower > pendingPower)
                    equity += 10;
                else if (canTie)
                    equity += IsPardaFavorableToComputer(round) ? 3 : -5;
                else
                    equity -= 12;
            }

            return Clamp(equity, 5, 98);
        }

        private static int EstimateCardPotential(IList<TrucoCard> cards)
        {
            if (cards == null || cards.Count == 0)
                return 0;

            List<int> powers = new List<int>(cards.Count);
            foreach (TrucoCard card in cards)
            {
                if (card != null)
                    powers.Add(TrucoRules.TrucoPower(card));
            }
            if (powers.Count == 0)
                return 0;

            powers.Sort(delegate(int left, int right) { return right.CompareTo(left); });

            // Normalizing against the maximum for the remaining card count keeps
            // bidding thresholds meaningful after the first and second tricks.
            double weighted = powers[0];
            double maximum = 14.0;
            if (powers.Count > 1)
            {
                weighted += powers[1] * 0.70;
                maximum += 13.0 * 0.70;
            }
            if (powers.Count > 2)
            {
                weighted += powers[2] * 0.40;
                maximum += 12.0 * 0.40;
            }

            return (int)Math.Round(weighted / maximum * 100.0);
        }

        private int GetScoreRiskModifier()
        {
            if (_match == null)
                return 0;

            int modifier = 0;
            int computerToWin = _match.TargetPoints - _match.OpponentScore;
            int playerToWin = _match.TargetPoints - _match.PlayerScore;

            // Positive values mean protect a lead; negative values mean take more risk.
            if (computerToWin <= 3)
                modifier += 10;
            else if (computerToWin <= 6)
                modifier += 4;

            if (playerToWin <= 3)
                modifier -= 10;
            else if (playerToWin <= 6)
                modifier -= 4;

            int scoreDifference = _match.OpponentScore - _match.PlayerScore;
            if (scoreDifference >= 6)
                modifier += 4;
            else if (scoreDifference <= -6)
                modifier -= 4;

            return Clamp(modifier, -15, 15);
        }

        private static double EstimateEnvidoWinChance(int points)
        {
            if (points <= 20) return 0.08;
            if (points <= 24) return 0.08 + (points - 20) * 0.035;
            if (points <= 27) return 0.22 + (points - 24) * 0.06;
            if (points <= 30) return 0.40 + (points - 27) * 0.08;
            return Clamp(0.64 + (points - 30) * 0.09, 0.0, 0.91);
        }

        private static double EstimateFlorWinChance(int points)
        {
            if (points <= 0) return 0.0;
            return Clamp(0.10 + (points - 20) * 0.045, 0.05, 0.91);
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
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
            float width = Lerp(LayoutWidth(72.0f), LayoutWidth(HandCardWidth), t);
            float height = Lerp(LayoutHeight(108.0f), LayoutHeight(HandCardHeight), t);
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
                FromWidth = LayoutWidth(isPlayer ? HandCardWidth : OpponentBackWidth),
                FromHeight = LayoutHeight(isPlayer ? HandCardHeight : OpponentBackHeight),
                ToWidth = LayoutWidth(PlayedCardWidth),
                ToHeight = LayoutHeight(PlayedCardHeight),
                StartedUtc = DateTime.UtcNow,
                DurationSeconds = 0.45
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
                Size resolution = RDR2.UI.Screen.Resolution;
                if (resolution.Width <= 0 || resolution.Height <= 0)
                    return 1.0f;

                return Math.Min(
                    resolution.Width / RDR2.UI.Screen.Width,
                    resolution.Height / RDR2.UI.Screen.Height);
            }
        }

        private static PointF LayoutPoint(float x, float y)
        {
            Size resolution = RDR2.UI.Screen.Resolution;
            if (resolution.Width <= 0 || resolution.Height <= 0)
                return new PointF(x, y);

            float scale = LayoutScale;
            float offsetX = (resolution.Width - RDR2.UI.Screen.Width * scale) / 2.0f;
            float offsetY = (resolution.Height - RDR2.UI.Screen.Height * scale) / 2.0f;

            // Convert the fitted physical-pixel layout back to the API's 1920x1080 coordinate base.
            float logicalX = (offsetX + x * scale) * RDR2.UI.Screen.Width / resolution.Width;
            float logicalY = (offsetY + y * scale) * RDR2.UI.Screen.Height / resolution.Height;
            return new PointF(logicalX, logicalY);
        }

        private static SizeF LayoutSize(float width, float height)
        {
            Size resolution = RDR2.UI.Screen.Resolution;
            if (resolution.Width <= 0 || resolution.Height <= 0)
                return new SizeF(width, height);

            float scale = LayoutScale;
            return new SizeF(
                width * scale * RDR2.UI.Screen.Width / resolution.Width,
                height * scale * RDR2.UI.Screen.Height / resolution.Height);
        }

        private static float LayoutWidth(float value)
        {
            Size resolution = RDR2.UI.Screen.Resolution;
            if (resolution.Width <= 0 || resolution.Height <= 0)
                return value;

            return value * LayoutScale * RDR2.UI.Screen.Width / resolution.Width;
        }

        private static float LayoutHeight(float value)
        {
            Size resolution = RDR2.UI.Screen.Resolution;
            if (resolution.Width <= 0 || resolution.Height <= 0)
                return value;

            return value * LayoutScale * RDR2.UI.Screen.Height / resolution.Height;
        }

        private static float LayoutTextScale(float value)
        {
            Size resolution = RDR2.UI.Screen.Resolution;
            if (resolution.Width <= 0 || resolution.Height <= 0)
                return value;

            // Text already scales with physical resolution; apply only the extra letterbox fit.
            return value * LayoutScale * RDR2.UI.Screen.Height / resolution.Height;
        }

        private void DrawMenuSurface()
        {
            PointF center = LayoutPoint(960.0f, 535.0f);
            SizeF outer = LayoutSize(790.0f, 430.0f);
            SizeF inner = LayoutSize(770.0f, 410.0f);
            DrawRect(center.X, center.Y, outer.Width, outer.Height, 24, 19, 15, 235);
            DrawRect(center.X, center.Y, inner.Width, inner.Height, 46, 35, 24, 238);
            DrawRect(center.X, center.Y - LayoutHeight(170.0f), LayoutWidth(620.0f), LayoutHeight(2.0f), 193, 157, 91, 255);
            DrawRect(center.X, center.Y + LayoutHeight(175.0f), LayoutWidth(620.0f), LayoutHeight(2.0f), 193, 157, 91, 255);
        }

        private void SetHeaderForMenu(bool menu)
        {
            _header.Position = LayoutPoint(960.0f, menu ? 340.0f : 65.0f);
        }

        private void DrawTopPanel()
        {
            PointF center = LayoutPoint(960.0f, 155.0f);
            SizeF outer = LayoutSize(1420.0f, 250.0f);
            SizeF inner = LayoutSize(1404.0f, 234.0f);
            DrawRect(center.X, center.Y, outer.Width, outer.Height, 18, 16, 13, 220);
            DrawRect(center.X, center.Y, inner.Width, inner.Height, 32, 27, 21, 218);
            DrawRect(center.X, center.Y - LayoutHeight(59.0f), LayoutWidth(1320.0f), LayoutHeight(2.0f), 168, 133, 76, 235);
        }

        private void DrawTableSurface()
        {
            PointF center = LayoutPoint(960.0f, 495.0f);
            SizeF outer = LayoutSize(840.0f, 225.0f);
            SizeF middle = LayoutSize(824.0f, 209.0f);
            SizeF inner = LayoutSize(806.0f, 191.0f);
            DrawRect(center.X, center.Y, outer.Width, outer.Height, 27, 19, 13, 240);
            DrawRect(center.X, center.Y, middle.Width, middle.Height, 147, 103, 54, 255);
            DrawRect(center.X, center.Y, inner.Width, inner.Height, 31, 78, 54, 235);
            DrawRect(center.X, center.Y, LayoutWidth(2.0f), LayoutHeight(165.0f), 151, 190, 128, 115);
        }

        private void DrawSelectedCardFrame(int index)
        {
            if (index < 0 || index >= _handPositions.Length)
                return;

            PointF position = _handPositions[index];
            SizeF cardSize = LayoutSize(HandCardWidth, HandCardHeight);
            float centerX = position.X + cardSize.Width / 2.0f;
            float centerY = position.Y + cardSize.Height / 2.0f;
            DrawRect(centerX, centerY, cardSize.Width + LayoutWidth(10.0f),
                cardSize.Height + LayoutHeight(10.0f), 225, 190, 101, 255);
            DrawRect(centerX, centerY, cardSize.Width + LayoutWidth(4.0f),
                cardSize.Height + LayoutHeight(4.0f), 46, 34, 24, 255);
        }

        private void DrawOpponentCardBacks()
        {
            if (_match == null || _match.Round == null)
                return;

            for (int i = 0; i < _opponentRevealSprites.Length; i++)
                if (_opponentRevealSprites[i] != null)
                    return;

            int count = Math.Min(3, _match.Round.OpponentCards.Count);
            for (int i = 0; i < count; i++)
            {
                // Draw only the supplied texture; no artificial frame or color plate.
                if (_opponentBackSprites[i] != null)
                    _opponentBackSprites[i].Draw();
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

            // The envido winner proves the announced points at the end of the hand.
            // A flor is shown as soon as it is announced/resolved, before card play.
            bool revealEnvidoProof = (_match.IsComplete || round.IsComplete) &&
                round.EnvidoResolved && round.EnvidoWinner == TrucoPlayer.Opponent;
            bool florCallVisible = round.PendingCall == TrucoCall.Flor ||
                round.PendingCall == TrucoCall.ContraFlor ||
                round.PendingCall == TrucoCall.ContraFlorAlResto;
            bool revealFlorProof = round.FlorEnabled && round.OpponentHadFlor &&
                ((round.CardsPlayed == 0 && (round.FlorResolved || florCallVisible)) ||
                 ((round.IsComplete || _match.IsComplete) && round.FlorResolved));

            if (revealEnvidoProof || revealFlorProof)
            {
                // Played cards already prove part of the hand; show only cards still held
                // in the opponent's hand, avoiding duplicates on the table.
                IList<TrucoCard> opponentCards = round.OpponentCards;
                for (int i = 0; i < opponentCards.Count && i < _opponentRevealSprites.Length; i++)
                {
                    _opponentRevealSprites[i] = new Sprite(
                        _textureDictionary,
                        opponentCards[i].TextureName,
                        LayoutSize(OpponentBackWidth, OpponentBackHeight),
                        _opponentCardBackPositions[i]);
                }
            }
            else
            {
                // Use the custom YTD texture for the hidden hand instead of a drawn pattern.
                int opponentCount = Math.Min(3, round.OpponentCards.Count);
                SizeF backSize = LayoutSize(OpponentBackWidth - 8.0f, OpponentBackHeight - 8.0f);
                for (int i = 0; i < opponentCount; i++)
                {
                    PointF position = _opponentCardBackPositions[i];
                    _opponentBackSprites[i] = new Sprite(
                        _textureDictionary,
                        OpponentBackTextureName,
                        backSize,
                        new PointF(
                            position.X + LayoutWidth(4.0f),
                            position.Y + LayoutHeight(4.0f)));
                }
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
                "    |    RIVAL: " + round.OpponentCards.Count + " cartas" +
                "    |    BAZAS: " + first + " / " + second + " / " + third);

            SetCaption(_roundDetails,
                "TRUCO: " + round.TrucoStake + " punto(s)" +
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
                bool faltaCallsWholeMatch =
                    (round.PendingCall == TrucoCall.FaltaEnvido ||
                     round.PendingCall == TrucoCall.ContraFlorAlResto) &&
                    (_match.TargetPoints == 15 ||
                     (_match.PlayerScore < 15 && _match.OpponentScore < 15));
                string offerValue = isTrucoOffer
                    ? "por " + (round.PendingValue + 1) + " puntos (no querido: " + round.PendingValue + ")"
                    : (faltaCallsWholeMatch ? "por la partida" : round.PendingValue + " punto(s)");
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
            DrawOpponentCardBacks();

            DrawTextElement(_header);
            DrawTextElement(_instructions);
            DrawTextElement(_scoreboard);
            DrawTextElement(_roundInfo);
            DrawTextElement(_roundDetails);
            DrawTextElement(_announcement);
            DrawTextElement(_handTitle);
            for (int i = 0; i < _playedLabels.Length; i++)
                DrawTextElement(_playedLabels[i]);
            DrawHandLabels();
            DrawTextElement(_status);
        }

        private void DrawHandLabels()
        {
            for (int i = 0; i < _handLabels.Length; i++)
                DrawTextElement(_handLabels[i]);
        }

        private void DrawTextElement(TextElement element)
        {
            if (element == null || !element.Enabled || string.IsNullOrEmpty(element.Caption))
                return;

            // Keep the same UIDEBUG background-text path used by TextElement in SHVDotNet V2.
            // The legacy HUD _DISPLAY_TEXT/_SET_TEXT_COLOR natives are inactive on current builds.
            PointF position = element.Position;
            float textWidth = EstimateTextWidth(element.Caption, element.Scale);
            float x = position.X;
            if (element.Alignment == Alignment.Center)
                x -= textWidth * 0.5f;
            else if (element.Alignment == Alignment.Right)
                x -= textWidth;

            float screenWidth = RDR2.UI.Screen.Width;
            float screenHeight = RDR2.UI.Screen.Height;
            if (screenWidth <= 0.0f || screenHeight <= 0.0f)
                return;

            try
            {
                // Reassert the overlay's draw layer immediately before issuing text commands.
                GRAPHICS.SET_SCRIPT_GFX_DRAW_ORDER(3);
            }
            catch
            {
                // Keep drawing if this optional graphics native is unavailable.
            }

            UIDEBUG._BG_SET_TEXT_SCALE(element.Scale, element.Scale);
            UIDEBUG._BG_SET_TEXT_COLOR(element.Color.R, element.Color.G, element.Color.B, element.Color.A);
            string renderedText = MISC.VAR_STRING(10, "LITERAL_STRING", element.Caption);
            UIDEBUG._BG_DISPLAY_TEXT(renderedText, x / screenWidth, position.Y / screenHeight);
        }

        private static float EstimateTextWidth(string caption, float scale)
        {
            float maxUnits = 0.0f;
            string[] lines = (caption ?? string.Empty).Split('\n');
            foreach (string line in lines)
            {
                float units = 0.0f;
                foreach (char ch in line)
                {
                    if (char.IsWhiteSpace(ch))
                        units += 0.32f;
                    else if (char.IsUpper(ch))
                        units += 0.60f;
                    else if (char.IsDigit(ch))
                        units += 0.52f;
                    else if (char.IsPunctuation(ch))
                        units += 0.30f;
                    else
                        units += 0.49f;
                }

                if (units > maxUnits)
                    maxUnits = units;
            }

            return maxUnits * scale * 70.0f;
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

            for (int i = 0; i < _opponentRevealSprites.Length; i++)
            {
                if (_opponentRevealSprites[i] != null)
                {
                    _opponentRevealSprites[i].Dispose();
                    _opponentRevealSprites[i] = null;
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

            for (int i = 0; i < _opponentBackSprites.Length; i++)
            {
                if (_opponentBackSprites[i] != null)
                {
                    _opponentBackSprites[i].Dispose();
                    _opponentBackSprites[i] = null;
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