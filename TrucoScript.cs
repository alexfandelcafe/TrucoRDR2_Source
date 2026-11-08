using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using RDR2;
using RDR2.Native;
using RDR2.UI;

namespace TrucoRDR2
{
    /// <summary>
    /// Texture validation plus an early interactive hand prototype.
    /// F7 toggles the texture viewer; Ctrl+F9 opens the mano a mano table against a basic strategic opponent.
    /// </summary>
    public sealed class TrucoScript : Script
    {
        private struct KeyBinding
        {
            public readonly Keys Key;
            public readonly Keys Modifiers;

            public KeyBinding(Keys key, Keys modifiers)
            {
                Key = key;
                Modifiers = modifiers;
            }
        }

        private const string DefaultTextureDictionary = "trucor2_cards";
        private const Keys TogglePreviewKey = Keys.F7;
        private const int CardsShown = 3;
        private const float CardWidth = 180.0f;
        private const float CardHeight = 270.0f;
        private const float CardTop = 690.0f;
        private const double MissingDictionaryGraceSeconds = 5.0;

        private readonly string _textureDictionary;
        private readonly KeyBinding _tableToggleBinding;
        private readonly string[] _textureNames;
        private readonly PointF[] _cardPositions;
        private readonly Sprite[] _sprites = new Sprite[CardsShown];
        private readonly TextElement[] _cardLabels = new TextElement[CardsShown];
        private readonly TextElement _header;
        private readonly TextElement _instructions;
        private readonly TextElement _status;
        private readonly TrucoTablePreview _tablePreview;
        private readonly TrucoWorldInteraction _worldInteraction;
        private readonly string _diagnosticLogPath;

        private bool _previewEnabled;
        private bool _toggleKeyDown;
        private bool _tableToggleKeyDown;
        private bool _textureRequestSent;
        private bool _spritesCreated;
        private int _firstCardIndex;
        private DateTime _textureRequestTimeUtc;
        private string _lastDiagnosticState;
        private string _lastWorldInteractionError;
        private string _lastCompanionPromptIntegrationStatus;

        public TrucoScript()
        {
            Interval = 0;
            _textureDictionary = ReadTextureDictionarySetting();
            _tableToggleBinding = ReadTableToggleBinding();
            _textureNames = CardTextureCatalog.CreateTrucoDeckTextureNames();
            _worldInteraction = new TrucoWorldInteraction();
            _tablePreview = new TrucoTablePreview(
                _textureDictionary,
                FormatKeyBinding(_tableToggleBinding),
                ReadMatchTargetSetting(),
                ReadFlorEnabledSetting(),
                ReadSoundEnabledSetting(),
                _worldInteraction);

            string scriptDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(scriptDirectory))
                scriptDirectory = AppDomain.CurrentDomain.BaseDirectory;

            _diagnosticLogPath = Path.Combine(scriptDirectory, "TrucoRDR2.log");

            _cardPositions = new[]
            {
                LayoutPoint(670.0f, CardTop),
                LayoutPoint(870.0f, CardTop),
                LayoutPoint(1070.0f, CardTop)
            };

            _header = new TextElement(
                "TRUCO RDR2 - PRUEBA DE TEXTURAS",
                LayoutPoint(960.0f, 80.0f), LayoutTextScale(0.65f), Color.Wheat,
                Alignment.Center, true, true);

            _instructions = new TextElement(
                "F7: mostrar/ocultar    AV PAG: siguiente    RE PAG: anterior    ESC: cerrar",
                LayoutPoint(960.0f, 125.0f), LayoutTextScale(0.40f), Color.WhiteSmoke,
                Alignment.Center, true, true);

            _status = new TextElement(
                "Esperando carga del diccionario...",
                LayoutPoint(960.0f, 1000.0f), LayoutTextScale(0.40f), Color.WhiteSmoke,
                Alignment.Center, true, true);

            for (int i = 0; i < CardsShown; i++)
            {
                _cardLabels[i] = new TextElement(
                    _textureNames[i],
                    LayoutPoint(670.0f + i * 200.0f + CardWidth / 2.0f, 655.0f),
                    LayoutTextScale(0.42f), Color.WhiteSmoke, Alignment.Center, true, true);
            }

            Tick += OnTick;
            KeyDown += OnKeyDown;
            KeyUp += OnKeyUp;
            Aborted += OnAborted;

            WriteDiagnostic("Script iniciado. Diccionario configurado: '" + _textureDictionary + "'.");
            WriteExpectedYtdPath(scriptDirectory);
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            // The salon interaction is handled by the native contextual prompt attached
            // to the seated rival. E is not intercepted globally, so ordinary game
            // interactions keep their normal bindings outside that prompt.

            if (e.KeyCode == _tableToggleBinding.Key && e.Modifiers == _tableToggleBinding.Modifiers)
            {
                // Keep the direct shortcut as a development fallback.
                if (!_tableToggleKeyDown)
                {
                    _tableToggleKeyDown = true;
                    if (_tablePreview.IsVisible)
                    {
                        if (_tablePreview.IsRoundComplete)
                        {
                            _tablePreview.ContinueAfterResult();
                            WriteDiagnostic("Se avanzó a la siguiente mano o se inició una nueva partida.");
                        }
                        else
                        {
                            _tablePreview.Close();
                            WriteDiagnostic("Mesa de prueba cerrada.");
                        }
                    }
                    else
                    {
                        if (_previewEnabled)
                            SetPreviewEnabled(false);

                        _tablePreview.Start();
                        WriteDiagnostic("Mesa de prueba activada mediante el atajo de desarrollo.");
                    }
                }

                e.Handled = true;
                return;
            }

            if (e.KeyCode == TogglePreviewKey)
            {
                // The F7 texture viewer and the table prototype are mutually exclusive.
                if (_tablePreview.IsVisible)
                {
                    e.Handled = true;
                    return;
                }

                if (!_toggleKeyDown)
                {
                    _toggleKeyDown = true;
                    SetPreviewEnabled(!_previewEnabled);
                }
                e.Handled = true;
                return;
            }

            if (_tablePreview.IsVisible)
            {
                _tablePreview.HandleKeyDown(e);
                return;
            }

            if (!_previewEnabled)
                return;

            if (e.KeyCode == Keys.PageDown)
            {
                _firstCardIndex = (_firstCardIndex + 1) % _textureNames.Length;
                UpdateVisibleCardLabels();
                if (_spritesCreated)
                    RebuildVisibleSprites();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.PageUp)
            {
                _firstCardIndex = (_firstCardIndex - 1 + _textureNames.Length) % _textureNames.Length;
                UpdateVisibleCardLabels();
                if (_spritesCreated)
                    RebuildVisibleSprites();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                SetPreviewEnabled(false);
                e.Handled = true;
            }
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == _tableToggleBinding.Key && _tableToggleKeyDown)
            {
                _tableToggleKeyDown = false;
                e.Handled = true;
                return;
            }

            if (e.KeyCode == TogglePreviewKey)
            {
                _toggleKeyDown = false;
                e.Handled = true;
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            bool startLocationMatch = _worldInteraction.Update(_tablePreview.IsVisible || _previewEnabled);

            if (!string.IsNullOrEmpty(_worldInteraction.LastError))
            {
                if (!string.Equals(_lastWorldInteractionError, _worldInteraction.LastError, StringComparison.Ordinal))
                {
                    _lastWorldInteractionError = _worldInteraction.LastError;
                    WriteDiagnostic("Error de interacción/prompt de Valentine: " + _lastWorldInteractionError);
                    RDR2.UI.Screen.PrintSubtitle("No se pudo preparar el prompt del truco. Revisa scripts\\TrucoRDR2.log.");
                }
            }
            else
            {
                _lastWorldInteractionError = null;
            }

            string companionStatus = _worldInteraction.CompanionPromptIntegrationStatus;
            if (!string.Equals(_lastCompanionPromptIntegrationStatus, companionStatus, StringComparison.Ordinal))
            {
                _lastCompanionPromptIntegrationStatus = companionStatus;
                WriteDiagnostic("Integración del prompt Companion: " + companionStatus);
            }

            if (_worldInteraction.SessionWasInterrupted && _tablePreview.IsVisible)
            {
                _tablePreview.Close();
                WriteDiagnostic("Mesa cerrada: el rival entró en un estado incompatible (muerte, huida, combate o daño).");
            }

            if (startLocationMatch)
            {
                _tablePreview.Start();
                WriteDiagnostic("Mesa de Valentine abierta desde el prompt contextual del rival.");
            }

            if (_tablePreview.IsVisible)
            {
                _tablePreview.Draw();
                return;
            }

            // The persistent table scene owns the world while a match is active.
            if (_worldInteraction.IsSessionActive)
                return;

            if (!_previewEnabled)
                return;

            try
            {
                GRAPHICS.SET_SCRIPT_GFX_DRAW_ORDER(3);
            }
            catch
            {
                // Keep the texture viewer usable even if the renderer lacks this draw-order native.
            }

            DrawPreviewSurface();
            DrawTextElement(_header);
            DrawTextElement(_instructions);

            bool dictionaryExists;
            bool dictionaryLoaded;

            try
            {
                // Request first. DOES_STREAMED_TEXTURE_DICT_EXIST can be false before the
                // request is issued, so gating the request on that check prevents first-time loading.
                if (!_textureRequestSent)
                {
                    _textureRequestSent = true;
                    _textureRequestTimeUtc = DateTime.UtcNow;
                    TXD.REQUEST_STREAMED_TEXTURE_DICT(_textureDictionary, false);
                    WriteDiagnostic("REQUEST_STREAMED_TEXTURE_DICT enviado para '" + _textureDictionary + "'.");
                }

                dictionaryExists = TXD.DOES_STREAMED_TEXTURE_DICT_EXIST(_textureDictionary);
                dictionaryLoaded = TXD.HAS_STREAMED_TEXTURE_DICT_LOADED(_textureDictionary);
            }
            catch (Exception ex)
            {
                string errorState = "ERROR|" + ex.GetType().FullName + "|" + ex.Message;
                if (!string.Equals(_lastDiagnosticState, errorState, StringComparison.Ordinal))
                {
                    WriteDiagnostic("ERROR consultando/cargando el diccionario: " + ex);
                    _lastDiagnosticState = errorState;
                }

                SetStatusCaption("Error de TXD (" + ex.GetType().Name + "). Ver scripts\\TrucoRDR2.log.");
                DrawTextElement(_status);
                DrawVisibleCardLabels();
                return;
            }

            string diagnosticState = dictionaryLoaded
                ? "LOADED"
                : (dictionaryExists ? "EXISTS_NOT_LOADED" : "NOT_FOUND");

            if (!string.Equals(_lastDiagnosticState, diagnosticState, StringComparison.Ordinal))
            {
                WriteDiagnostic(
                    "Estado del diccionario: " + diagnosticState +
                    " | DOES_STREAMED_TEXTURE_DICT_EXIST=" + dictionaryExists +
                    " | HAS_STREAMED_TEXTURE_DICT_LOADED=" + dictionaryLoaded + ".");
                _lastDiagnosticState = diagnosticState;
            }

            if (!dictionaryLoaded)
            {
                if (!dictionaryExists)
                {
                    double secondsSinceRequest = (DateTime.UtcNow - _textureRequestTimeUtc).TotalSeconds;
                    if (secondsSinceRequest < MissingDictionaryGraceSeconds)
                    {
                        SetStatusCaption("Solicitando YTD: " + _textureDictionary + "...");
                    }
                    else
                    {
                        SetStatusCaption(
                            "El juego no encuentra '" + _textureDictionary +
                            "'. Revisa ruta, nombre y formato del YTD.");
                    }
                }
                else
                {
                    SetStatusCaption("Diccionario encontrado; esperando carga: " + _textureDictionary);
                }
            }
            else
            {
                SetStatusCaption("Diccionario cargado: " + _textureDictionary +
                                 "  |  40 cartas esperadas.");
                if (!_spritesCreated)
                {
                    RebuildVisibleSprites();
                    _spritesCreated = true;
                    WriteDiagnostic("Diccionario cargado; sprites de vista previa creados.");
                }
            }

            DrawTextElement(_status);

            for (int i = 0; i < CardsShown; i++)
            {
                if (_spritesCreated && dictionaryLoaded && _sprites[i] != null)
                    _sprites[i].Draw();

                // Keep texture names visible even when the YTD is missing or not loaded.
                DrawTextElement(_cardLabels[i]);
            }
        }

        private void DrawVisibleCardLabels()
        {
            for (int i = 0; i < CardsShown; i++)
                DrawTextElement(_cardLabels[i]);
        }

        private void DrawPreviewSurface()
        {
            DrawUiRect(960.0f, 105.0f, 1420.0f, 110.0f, 24, 19, 15, 225);
            DrawUiRect(960.0f, 105.0f, 1410.0f, 100.0f, 46, 35, 24, 238);

            // Texture labels and load status are intentionally unboxed, matching the game's HUD text style.
        }

        private static void DrawUiRect(float x, float y, float width, float height,
            int red, int green, int blue, int alpha)
        {
            PointF center = LayoutPoint(x, y);
            SizeF size = LayoutSize(width, height);
            GRAPHICS.DRAW_RECT(
                center.X / RDR2.UI.Screen.Width,
                center.Y / RDR2.UI.Screen.Height,
                size.Width / RDR2.UI.Screen.Width,
                size.Height / RDR2.UI.Screen.Height,
                red, green, blue, alpha, false, false);
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

        private void SetStatusCaption(string caption)
        {
            // Avoid resetting the text element every frame while the load state is unchanged.
            if (!string.Equals(_status.Caption, caption, StringComparison.Ordinal))
                _status.Caption = caption;
        }

        private void SetPreviewEnabled(bool enabled)
        {
            if (_previewEnabled == enabled)
                return;

            _previewEnabled = enabled;
            if (_previewEnabled)
            {
                _firstCardIndex = 0;
                UpdateVisibleCardLabels();
                _textureRequestSent = false;
                _textureRequestTimeUtc = DateTime.UtcNow;
                _lastDiagnosticState = null;
                _spritesCreated = false;
                WriteDiagnostic("Vista de texturas activada.");
                RDR2.UI.Screen.PrintSubtitle("TrucoRDR2: vista de texturas activada. Av Pag/Re Pag cambia las cartas.");
            }
            else
            {
                DisposeSprites();
                _spritesCreated = false;
                _textureRequestSent = false;
                _lastDiagnosticState = null;
                WriteDiagnostic("Vista de texturas cerrada.");
                RDR2.UI.Screen.PrintSubtitle("TrucoRDR2: vista de texturas cerrada.");
            }
        }

        private void RebuildVisibleSprites()
        {
            // Create the replacements before disposing the old sprites so the texture dictionary
            // remains referenced while the player browses cards.
            Sprite[] replacements = new Sprite[CardsShown];
            string[] visibleNames = new string[CardsShown];

            for (int i = 0; i < CardsShown; i++)
            {
                int catalogIndex = (_firstCardIndex + i) % _textureNames.Length;
                visibleNames[i] = _textureNames[catalogIndex];
                replacements[i] = new Sprite(
                    _textureDictionary,
                    visibleNames[i],
                    LayoutSize(CardWidth, CardHeight),
                    _cardPositions[i]);
            }

            for (int i = 0; i < CardsShown; i++)
            {
                if (_sprites[i] != null)
                    _sprites[i].Dispose();

                _sprites[i] = replacements[i];
                _cardLabels[i].Caption = visibleNames[i];
            }
        }

        private void UpdateVisibleCardLabels()
        {
            for (int i = 0; i < CardsShown; i++)
            {
                int catalogIndex = (_firstCardIndex + i) % _textureNames.Length;
                _cardLabels[i].Caption = _textureNames[catalogIndex];
            }
        }

        private void DisposeSprites()
        {
            for (int i = 0; i < CardsShown; i++)
            {
                if (_sprites[i] != null)
                {
                    _sprites[i].Dispose();
                    _sprites[i] = null;
                }
            }
        }

        private void OnAborted(object sender, EventArgs e)
        {
            _tablePreview.Close();
            _worldInteraction.Dispose();
            _previewEnabled = false;
            DisposeSprites();
            WriteDiagnostic("Script abortado; prompt, mesa, sillas, rival y sprites liberados.");
        }

        private void WriteExpectedYtdPath(string scriptDirectory)
        {
            try
            {
                DirectoryInfo scriptsInfo = new DirectoryInfo(scriptDirectory);
                DirectoryInfo gameRoot = scriptsInfo.Parent;
                if (gameRoot == null)
                    return;

                string expectedYtdPath = Path.Combine(
                    gameRoot.FullName, "lml", "stream", _textureDictionary + ".ytd");
                WriteDiagnostic(
                    "YTD esperado para LML: '" + expectedYtdPath +
                    "' | existe en disco: " + File.Exists(expectedYtdPath) + ".");
            }
            catch (Exception ex)
            {
                WriteDiagnostic("No se pudo comprobar la ruta esperada del YTD: " + ex.Message);
            }
        }

        private void WriteDiagnostic(string message)
        {
            try
            {
                string line = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
                              " " + message + Environment.NewLine;
                File.AppendAllText(_diagnosticLogPath, line);
            }
            catch
            {
                // Diagnostics must never prevent the script from running.
            }
        }

        private static string FormatKeyBinding(KeyBinding binding)
        {
            string result = string.Empty;
            if ((binding.Modifiers & Keys.Control) != Keys.None)
                result += "Ctrl+";
            if ((binding.Modifiers & Keys.Shift) != Keys.None)
                result += "Shift+";
            if ((binding.Modifiers & Keys.Alt) != Keys.None)
                result += "Alt+";

            return result + binding.Key;
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
            return new PointF(
                (offsetX + x * scale) * RDR2.UI.Screen.Width / resolution.Width,
                (offsetY + y * scale) * RDR2.UI.Screen.Height / resolution.Height);
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

        private static float LayoutTextScale(float value)
        {
            Size resolution = RDR2.UI.Screen.Resolution;
            if (resolution.Width <= 0 || resolution.Height <= 0)
                return value;

            // Text already scales with physical resolution; apply only the extra letterbox fit.
            return value * LayoutScale * RDR2.UI.Screen.Height / resolution.Height;
        }

        private static KeyBinding ReadTableToggleBinding()
        {
            KeyBinding defaultBinding = new KeyBinding(Keys.F9, Keys.Control);

            try
            {
                string scriptDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(scriptDirectory))
                    return defaultBinding;

                string iniPath = Path.Combine(scriptDirectory, "TrucoRDR2.ini");
                if (!File.Exists(iniPath))
                    return defaultBinding;

                string currentSection = string.Empty;
                foreach (string rawLine in File.ReadAllLines(iniPath))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#"))
                        continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        currentSection = line.Substring(1, line.Length - 2).Trim();
                        continue;
                    }

                    if (!string.Equals(currentSection, "Controls", StringComparison.OrdinalIgnoreCase))
                        continue;

                    int equalsIndex = line.IndexOf('=');
                    if (equalsIndex <= 0)
                        continue;

                    string key = line.Substring(0, equalsIndex).Trim();
                    string value = line.Substring(equalsIndex + 1).Trim();
                    if (string.Equals(key, "TableToggle", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
                        return ParseKeyBinding(value, defaultBinding);
                }
            }
            catch
            {
                // A malformed optional control setting must not prevent the script from loading.
            }

            return defaultBinding;
        }

        private static KeyBinding ParseKeyBinding(string value, KeyBinding defaultBinding)
        {
            Keys key = Keys.None;
            Keys modifiers = Keys.None;

            string[] parts = value.Split('+');
            foreach (string rawPart in parts)
            {
                string part = rawPart.Trim();
                if (part.Length == 0)
                    return defaultBinding;

                if (string.Equals(part, "Ctrl", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(part, "Control", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= Keys.Control;
                }
                else if (string.Equals(part, "Shift", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= Keys.Shift;
                }
                else if (string.Equals(part, "Alt", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= Keys.Alt;
                }
                else
                {
                    Keys parsedKey;
                    if (key != Keys.None ||
                        !Enum.TryParse(part, true, out parsedKey) ||
                        !Enum.IsDefined(typeof(Keys), parsedKey) ||
                        parsedKey == Keys.None ||
                        (parsedKey & Keys.Modifiers) != Keys.None ||
                        parsedKey == Keys.ControlKey ||
                        parsedKey == Keys.ShiftKey ||
                        parsedKey == Keys.Menu ||
                        parsedKey == Keys.F7 ||
                        parsedKey == Keys.F8 ||
                        parsedKey == Keys.F6 ||
                        parsedKey == Keys.Insert)
                    {
                        return defaultBinding;
                    }

                    key = parsedKey;
                }
            }

            return key == Keys.None
                ? defaultBinding
                : new KeyBinding(key, modifiers);
        }

        private static int ReadMatchTargetSetting()
        {
            int value;
            string raw = ReadIniSetting("Game", "TargetPoints");
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) &&
                (value == 15 || value == 30))
                return value;

            return 30;
        }

        private static bool ReadFlorEnabledSetting()
        {
            bool value;
            string raw = ReadIniSetting("Game", "FlorEnabled");
            return bool.TryParse(raw, out value) ? value : false;
        }

        private static bool ReadSoundEnabledSetting()
        {
            bool value;
            string raw = ReadIniSetting("Audio", "Enabled");
            return bool.TryParse(raw, out value) ? value : true;
        }

        private static string ReadIniSetting(string sectionName, string settingName)
        {
            try
            {
                string scriptDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(scriptDirectory))
                    return null;

                string iniPath = Path.Combine(scriptDirectory, "TrucoRDR2.ini");
                if (!File.Exists(iniPath))
                    return null;

                string currentSection = string.Empty;
                foreach (string rawLine in File.ReadAllLines(iniPath))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#"))
                        continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        currentSection = line.Substring(1, line.Length - 2).Trim();
                        continue;
                    }

                    if (!string.Equals(currentSection, sectionName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    int equalsIndex = line.IndexOf('=');
                    if (equalsIndex <= 0)
                        continue;

                    string key = line.Substring(0, equalsIndex).Trim();
                    if (string.Equals(key, settingName, StringComparison.OrdinalIgnoreCase))
                        return line.Substring(equalsIndex + 1).Trim();
                }
            }
            catch
            {
                // Optional settings always fall back to safe defaults.
            }

            return null;
        }

        private static string ReadTextureDictionarySetting()
        {
            string defaultValue = DefaultTextureDictionary;
            try
            {
                string scriptDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(scriptDirectory))
                    return defaultValue;

                string iniPath = Path.Combine(scriptDirectory, "TrucoRDR2.ini");
                if (!File.Exists(iniPath))
                    return defaultValue;

                string currentSection = string.Empty;
                foreach (string rawLine in File.ReadAllLines(iniPath))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#"))
                        continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        currentSection = line.Substring(1, line.Length - 2).Trim();
                        continue;
                    }

                    if (!string.Equals(currentSection, "Textures", StringComparison.OrdinalIgnoreCase))
                        continue;

                    int equalsIndex = line.IndexOf('=');
                    if (equalsIndex <= 0)
                        continue;

                    string key = line.Substring(0, equalsIndex).Trim();
                    string value = line.Substring(equalsIndex + 1).Trim();
                    if (string.Equals(key, "TextureDictionary", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
                        return value;
                }
            }
            catch
            {
                // A malformed optional INI should not prevent ScriptHook from loading the script.
            }

            return defaultValue;
        }
    }
}