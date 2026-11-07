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
        private readonly string _tableToggleText;
        private readonly string _matchSavePath;
        private readonly Action<int, bool, bool> _saveSettings;
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
        private int _lastObservedPlayerHealth;
        private TrucoMatchProgress _savedProgress;
        private bool _hasSavedMatch;
        private bool _progressSavedForCurrentRound;
        private string _saveStatusMessage;

        public bool IsVisible { get { return _visible; } }
        public bool IsRoundComplete
        {
            get { return _match != null && (_match.IsComplete || _match.Round.IsComplete); }
        }

        public TrucoTablePreview(string textureDictionary, string tableToggleText,
            int targetPoints, bool florEnabled, bool soundEnabled, string matchSavePath,
            Action<int, bool, bool> saveSettings)
        {
            if (string.IsNullOrEmpty(textureDictionary))
                throw new ArgumentException("El nombre del diccionario no puede estar vacío.", "textureDictionary");
            if (targetPoints != 15 && targetPoints != 30)
                throw new ArgumentOutOfRangeException("targetPoints");

            if (string.IsNullOrWhiteSpace(matchSavePath))
                throw new ArgumentException("La ruta del guardado no puede estar vacía.", "matchSavePath");

            _textureDictionary = textureDictionary;
            _tableToggleText = string.IsNullOrEmpty(tableToggleText) ? "COMBINACION" : tableToggleText.ToUpperInvariant();
            _matchSavePath = matchSavePath;
            _saveSettings = saveSettings;
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
            _lastObservedPlayerHealth = ReadPlayerHealth();

            if (_match == null)
            {
                _hasSavedMatch = TrucoMatchProgressStore.TryLoad(_matchSavePath, out _savedProgress);
                if (_hasSavedMatch)
                {
                    _targetPoints = _savedProgress.TargetPoints;
                    _florEnabled = _savedProgress.FlorEnabled;
                }
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
            _savedProgress = null;
            _hasSavedMatch = false;
            _progressSavedForCurrentRound = false;
            _saveStatusMessage = null;
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
            SaveMatchProgress();
            TrucoAudio.Play(_soundEnabled, TrucoSound.Deal);
        }

        private void ContinueSavedMatch()
        {
            if (!_hasSavedMatch || _savedProgress == null)
                return;

            _targetPoints = _savedProgress.TargetPoints;
            _florEnabled = _savedProgress.FlorEnabled;
            _match = new TrucoMatch(_savedProgress, _random);
            _showMenu = false;
            SetHeaderForMenu(false);
            _selectedCardIndex = 0;
            _textureRequestSent = false;
            _textureDictionaryLoaded = false;
            _textureRequestTimeUtc = DateTime.UtcNow;
            _nextComputerActionUtc = DateTime.UtcNow.AddSeconds(ComputerThinkSeconds);
            _dealAnimationStartUtc = DateTime.UtcNow;
            _dealAnimationActive = !_match.IsComplete;
            _playedCardAnimation = null;
            _spritesCreated = false;
            _progressSavedForCurrentRound = _match.IsComplete;
            _saveStatusMessage = null;
            DisposeSprites();
            UpdateHandLabels();
            TrucoAudio.Play(_soundEnabled, _match.IsComplete ? TrucoSound.Win : TrucoSound.Deal);
        }

        private void SaveMatchProgress()
        {
            if (_match == null)
                return;

            try
            {
                TrucoPlayer savedMano = _match.Mano;
                if (!_match.IsComplete && _match.Round != null && _match.Round.IsComplete)
                    savedMano = savedMano == TrucoPlayer.Player ? TrucoPlayer.Opponent : TrucoPlayer.Player;

                _savedProgress = new TrucoMatchProgress(
                    _match.TargetPoints,
                    _match.PlayerScore,
                    _match.OpponentScore,
                    savedMano,
                    _match.FlorEnabled,
                    _match.IsComplete,
                    _match.Winner);
                TrucoMatchProgressStore.Save(_matchSavePath, _savedProgress);
                _hasSavedMatch = true;
                _progressSavedForCurrentRound = true;
                _saveStatusMessage = "Partida guardada al cierre de la mano.";
            }
            catch (Exception ex)
            {
                _saveStatusMessage = "No se pudo guardar la partida: " + ex.Message;
            }
        }

        private void PersistMenuSettings()
        {
            try
            {
                if (_saveSettings != null)
                    _saveSettings(_targetPoints, _florEnabled, _soundEnabled);
            }
            catch (Exception ex)
            {
                _saveStatusMessage = "No se pudieron guardar las opciones: " + ex.Message;
            }
        }

        private void UpdateMenuText()
        {
            string target = _targetPoints == 15 ? "15 PUNTOS" : "30 PUNTOS";
            string flor = _florEnabled ? "ACTIVADA" : "DESACTIVADA";
            string sound = _soundEnabled ? "ACTIVADO" : "DESACTIVADO";
            int offset = _hasSavedMatch ? 1 : 0;
            List<string> options = new List<string>();

            if (_hasSavedMatch)
                options.Add((_menuSelection == 0 ? "> " : "  ") + "CONTINUAR PARTIDA");

            options.Add((_menuSelection == offset ? "> " : "  ") + "NUEVA PARTIDA");
            options.Add((_menuSelection == offset + 1 ? "> " : "  ") + "DURACION: " + target);
            options.Add((_menuSelection == offset + 2 ? "> " : "  ") + "FLOR: " + flor);
            options.Add((_menuSelection == offset + 3 ? "> " : "  ") + "SONIDO: " + sound);

            SetCaption(_menuOptions, string.Join("\n", options.ToArray()));
            SetCaption(_menuHint,
                "ARRIBA/ABAJO: elegir   IZQUIERDA/DERECHA: cambiar\nENTER/A: confirmar   ESC/B: cerrar");
        }

        public void ContinueAfterResult()
        {
            if (_match == null) return;

            if (_match.IsComplete)
            {
                SaveMatchProgress();
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
            _progressSavedForCurrentRound = false;
            _selectedCardIndex = 0;
            _nextComputerActionUtc = DateTime.UtcNow.AddSeconds(ComputerThinkSeconds);
            _dealAnimationStartUtc = DateTime.UtcNow;
            _dealAnimationActive = !_match.IsComplete;
            _playedCardAnimation = null;
            _spritesCreated = false;
            DisposeSprites();
            UpdateHandLabels();
            SaveMatchProgress();
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

            int selectionCount = _hasSavedMatch ? 5 : 4;
            int offset = _hasSavedMatch ? 1 : 0;

            if (e.KeyCode == Keys.Up)
            {
                _menuSelection = (_menuSelection - 1 + selectionCount) % selectionCount;
                UpdateMenuText();
                TrucoAudio.Play(_soundEnabled, TrucoSound.Select);
                return;
            }

            if (e.KeyCode == Keys.Down)
            {
                _menuSelection = (_menuSelection + 1) % selectionCount;
                UpdateMenuText();
                TrucoAudio.Play(_soundEnabled, TrucoSound.Select);
                return;
            }

            if (e.KeyCode != Keys.Left && e.KeyCode != Keys.Right && e.KeyCode != Keys.Enter)
                return;

            if (_hasSavedMatch && _menuSelection == 0)
            {
                if (e.KeyCode == Keys.Enter)
                    ContinueSavedMatch();
                return;
            }

            if (_menuSelection == offset)
            {
                if (e.KeyCode == Keys.Enter)
                    StartMatch();
                return;
            }

            bool changed = false;
            if (_menuSelection == offset + 1)
            {
                _targetPoints = _targetPoints == 15 ? 30 : 15;
                changed = true;
            }
            else if (_menuSelection == offset + 2)
            {
                _florEnabled = !_florEnabled;
                changed = true;
            }
            else if (_menuSelection == offset + 3)
            {
                _soundEnabled = !_soundEnabled;
                changed = true;
            }

            if (changed)
                PersistMenuSettings();

            UpdateMenuText();
            TrucoAudio.Play(_soundEnabled, TrucoSound.Select);
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

            // The world keeps running behind the overlay. If combat or a fall damages the player,
            // close the overlay but retain the match state so Ctrl+F9 can reopen it safely.
            try
            {
                int health = ReadPlayerHealth();
                bool dead = RDR2.Game.Player.IsDead;
                if (dead || (_lastObservedPlayerHealth > 0 && health > 0 && health < _lastObservedPlayerHealth))
                {
                    Close();
                    RDR2.UI.Screen.PrintSubtitle("Partida interrumpida por daño. Usá " + _tableToggleText + " para volver.");
                    return;
                }

                if (health > 0)
                    _lastObservedPlayerHealth = health;
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
                PollGamepadControls();
                if (!_visible)
                    return;
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

            if ((_match.IsComplete || _match.Round.IsComplete) && !_progressSavedForCurrentRound)
                SaveMatchProgress();

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

        private void PollGamepadControls()
        {
            // Controls are disabled for the overlay, so query the disabled-control
            // variant. A and B also answer pending calls (quiero / no quiero).
            if (GamepadJustPressed(0x6319DB71UL)) HandleGamepadKey(Keys.Up);       // D-pad up
            if (GamepadJustPressed(0x05CA7C52UL)) HandleGamepadKey(Keys.Down);     // D-pad down
            if (GamepadJustPressed(0xA65EBAB4UL)) HandleGamepadKey(Keys.Left);     // D-pad left
            if (GamepadJustPressed(0xDEB34313UL)) HandleGamepadKey(Keys.Right);    // D-pad right
            if (GamepadJustPressed(0xC7B5340AUL)) HandleGamepadKey(Keys.Enter);    // A / accept
            if (GamepadJustPressed(0x156F7119UL)) HandleGamepadKey(Keys.Escape);  // B / cancel
            if (GamepadJustPressed(0x6DB8C62FUL)) HandleGamepadKey(Keys.E);       // X / envido
            if (GamepadJustPressed(0x7C0162C0UL)) HandleGamepadKey(Keys.T);       // Y / truco
            if (GamepadJustPressed(0xE885EF16UL)) HandleGamepadKey(Keys.R);       // LB / real envido
            if (GamepadJustPressed(0x17BEC168UL)) HandleGamepadKey(Keys.F);       // RB / falta envido
            if (GamepadJustPressed(0x51104035UL)) HandleGamepadKey(Keys.C);       // LT / contra flor
            if (GamepadJustPressed(0x6FED71BCUL)) HandleGamepadKey(Keys.B);       // RT / contra flor al resto
            if (GamepadJustPressed(0x43CDA5B0UL)) HandleGamepadKey(Keys.M);       // L3 / irse al mazo
        }

        private void HandleGamepadKey(Keys key)
        {
            if (!_visible)
                return;

            if (!_showMenu && _match != null && _match.Round.HasPendingResponse)
            {
                if (key == Keys.Enter) key = Keys.Q;
                else if (key == Keys.Escape) key = Keys.N;
            }

            HandleKeyDown(new KeyEventArgs(key));
        }

        private static bool GamepadJustPressed(ulong control)
        {
            try
            {
                return Function.Call<bool>(0x91AEF906BCA88877UL, 0, unchecked((int)control));
            }
            catch
            {
                return false;
            }
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
                TrucoCall? envidoCall = TrucoAi.ChooseEnvidoCall(
                    envido, _match.OpponentScore, _match.PlayerScore, _match.TargetPoints);
                if (envidoCall.HasValue && round.CallEnvido(TrucoPlayer.Opponent, envidoCall.Value))
                {
                    TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
                    return;
                }
            }

            int strength = round.HandStrength(TrucoPlayer.Opponent);
            if (round.CanCallTruco(TrucoPlayer.Opponent) &&
                TrucoAi.ShouldCallTruco(strength, _match.OpponentScore, _match.PlayerScore, _match.TargetPoints))
            {
                round.CallTruco(TrucoPlayer.Opponent);
                TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
                return;
            }

            if (TrucoAi.ShouldFold(strength, round.TrucoStake,
                _match.OpponentScore, _match.PlayerScore, _match.TargetPoints))
            {
                if (round.Fold(TrucoPlayer.Opponent))
                {
                    RefreshSprites();
                    TrucoAudio.Play(_soundEnabled, TrucoSound.Win);
                }
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

                if (TrucoAi.ShouldRaiseEnvido(points, pending))
                {
                    TrucoCall raise = pending == TrucoCall.Envido && points >= 33
                        ? TrucoCall.RealEnvido
                        : TrucoCall.FaltaEnvido;
                    if (round.CanRaiseEnvido(TrucoPlayer.Opponent, raise) &&
                        round.CallEnvido(TrucoPlayer.Opponent, raise))
                    {
                        TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
                        return;
                    }
                }

                accepted = TrucoAi.ShouldAcceptEnvido(
                    points, round.PendingValue, _match.OpponentScore, _match.PlayerScore, _match.TargetPoints);
                round.RespondPending(TrucoPlayer.Opponent, accepted);
            }
            else if (pending == TrucoCall.Flor || pending == TrucoCall.ContraFlor ||
                     pending == TrucoCall.ContraFlorAlResto)
            {
                int flor = round.FlorFor(TrucoPlayer.Opponent);
                if (TrucoAi.ShouldRaiseFlor(flor, pending))
                {
                    TrucoCall raise = pending == TrucoCall.Flor
                        ? TrucoCall.ContraFlor
                        : TrucoCall.ContraFlorAlResto;
                    if (round.CanRaiseFlor(TrucoPlayer.Opponent, raise) &&
                        round.RaiseFlor(TrucoPlayer.Opponent, raise))
                    {
                        TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
                        return;
                    }
                }

                accepted = TrucoAi.ShouldAcceptFlor(
                    flor, round.PendingValue, _match.OpponentScore, _match.PlayerScore, _match.TargetPoints);
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
                if ((pending == TrucoCall.Truco || pending == TrucoCall.Retruco) &&
                    TrucoAi.ShouldRaiseTruco(strength, _match.OpponentScore,
                        _match.PlayerScore, _match.TargetPoints) &&
                    round.RaiseTruco(TrucoPlayer.Opponent))
                {
                    TrucoAudio.Play(_soundEnabled, TrucoSound.Accept);
                    return;
                }

                accepted = TrucoAi.ShouldAcceptTruco(strength, pending,
                    _match.OpponentScore, _match.PlayerScore, _match.TargetPoints);
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
            StartPlayedCardAnimation(TrucoPlayer.Opponent, playedCard, from);
            RefreshSprites();
            TrucoAudio.Play(_soundEnabled,
                (_match.IsComplete || round.IsComplete) ? TrucoSound.Win : TrucoSound.Select);
        }

        private int ChooseComputerCard(TrucoRound round, IList<TrucoCard> cards)
        {
            return TrucoAi.ChooseCard(round, cards);
        }

                private static bool WouldPardaWinHandForComputer(TrucoRound round)
        {
            if (round.Tricks.Count == 1)
                return round.Tricks[0].Winner == TrucoPlayer.Opponent;

            if (round.Tricks.Count == 2)
            {
                TrucoTrick first = round.Tricks[0];
                TrucoTrick second = round.Tricks[1];

                if (first.Winner == TrucoPlayer.Opponent)
                    return true;

                if (first.IsParda && second.IsParda && round.Mano == TrucoPlayer.Opponent)
                    return true;
            }

            return false;
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

            if (!string.IsNullOrEmpty(_saveStatusMessage))
                return _saveStatusMessage;

            if (round.CurrentPlayer == TrucoPlayer.Player)
                return "TU TURNO: flechas/DPAD + ENTER/A | E ENVIDO | R REAL | F FALTA | T TRUCO | M MAZO";
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

        private static int ReadPlayerHealth()
        {
            try
            {
                return RDR2.Game.Player.Character.Health;
            }
            catch
            {
                return -1;
            }
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

            // TextElement's implementation ignores Alignment, so center/right aligned elements
            // must be shifted manually before calling its left-anchored Draw().
            PointF originalPosition = element.Position;
            float textWidth = EstimateTextWidth(element.Caption, element.Scale);
            float x = originalPosition.X;
            if (element.Alignment == Alignment.Center)
                x -= textWidth * 0.5f;
            else if (element.Alignment == Alignment.Right)
                x -= textWidth;

            element.Position = new PointF(x, originalPosition.Y);
            try
            {
                element.Draw();
            }
            finally
            {
                element.Position = originalPosition;
            }
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