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
    /// F7 toggles the texture viewer; F8 opens the hand prototype against a random-play opponent.
    /// </summary>
    public sealed class TrucoScript : Script
    {
        private const string DefaultTextureDictionary = "trucor2_cards";
        private const Keys TogglePreviewKey = Keys.F7;
        private const Keys ToggleTableKey = Keys.F8;
        private const int CardsShown = 3;
        private const float CardWidth = 180.0f;
        private const float CardHeight = 270.0f;
        private const float CardTop = 690.0f;
        private const double MissingDictionaryGraceSeconds = 5.0;

        private readonly string _textureDictionary;
        private readonly string[] _textureNames;
        private readonly PointF[] _cardPositions;
        private readonly Sprite[] _sprites = new Sprite[CardsShown];
        private readonly TextElement[] _cardLabels = new TextElement[CardsShown];
        private readonly TextElement _header;
        private readonly TextElement _instructions;
        private readonly TextElement _status;
        private readonly TrucoTablePreview _tablePreview;
        private readonly string _diagnosticLogPath;

        private bool _previewEnabled;
        private bool _toggleKeyDown;
        private bool _tableToggleKeyDown;
        private bool _textureRequestSent;
        private bool _spritesCreated;
        private int _firstCardIndex;
        private DateTime _textureRequestTimeUtc;
        private string _lastDiagnosticState;

        public TrucoScript()
        {
            Interval = 0;
            _textureDictionary = ReadTextureDictionarySetting();
            _textureNames = CardTextureCatalog.CreateTrucoDeckTextureNames();
            _tablePreview = new TrucoTablePreview(_textureDictionary);

            string scriptDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(scriptDirectory))
                scriptDirectory = AppDomain.CurrentDomain.BaseDirectory;

            _diagnosticLogPath = Path.Combine(scriptDirectory, "TrucoRDR2.log");

            _cardPositions = new[]
            {
                new PointF(670.0f, CardTop),
                new PointF(870.0f, CardTop),
                new PointF(1070.0f, CardTop)
            };

            _header = new TextElement(
                "TRUCO RDR2 - PRUEBA DE TEXTURAS",
                new PointF(960.0f, 80.0f), 0.65f, Color.Wheat,
                Alignment.Center, true, true);

            _instructions = new TextElement(
                "F7: mostrar/ocultar    AV PAG: siguiente    RE PAG: anterior    ESC: cerrar",
                new PointF(960.0f, 125.0f), 0.40f, Color.WhiteSmoke,
                Alignment.Center, true, true);

            _status = new TextElement(
                "Esperando carga del diccionario...",
                new PointF(960.0f, 1000.0f), 0.40f, Color.WhiteSmoke,
                Alignment.Center, true, true);

            for (int i = 0; i < CardsShown; i++)
            {
                _cardLabels[i] = new TextElement(
                    _textureNames[i],
                    new PointF(_cardPositions[i].X + CardWidth / 2.0f, 655.0f),
                    0.42f, Color.WhiteSmoke, Alignment.Center, true, true);
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
            if (e.KeyCode == ToggleTableKey)
            {
                // Ignore repeated KeyDown events until the physical key is released.
                if (!_tableToggleKeyDown)
                {
                    _tableToggleKeyDown = true;
                    if (_tablePreview.IsVisible)
                    {
                        if (_tablePreview.IsRoundComplete)
                        {
                            _tablePreview.Start();
                            WriteDiagnostic("Nueva mano de prueba repartida.");
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
                        WriteDiagnostic("Mesa de prueba activada.");
                    }
                }

                e.Handled = true;
                return;
            }

            if (e.KeyCode == TogglePreviewKey)
            {
                // The F7 texture viewer and the F8 table prototype are mutually exclusive.
                if (_tablePreview.IsVisible)
                {
                    e.Handled = true;
                    return;
                }

                // Ignore repeated KeyDown events until the physical key is released.
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
            if (e.KeyCode == ToggleTableKey)
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
            if (_tablePreview.IsVisible)
            {
                _tablePreview.Draw();
                return;
            }

            if (!_previewEnabled)
                return;

            _header.Draw();
            _instructions.Draw();

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
                _status.Draw();
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

            _status.Draw();

            for (int i = 0; i < CardsShown; i++)
            {
                if (_spritesCreated && dictionaryLoaded && _sprites[i] != null)
                    _sprites[i].Draw();

                // Keep texture names visible even when the YTD is missing or not loaded.
                _cardLabels[i].Draw();
            }
        }

        private void DrawVisibleCardLabels()
        {
            for (int i = 0; i < CardsShown; i++)
                _cardLabels[i].Draw();
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
                    new SizeF(CardWidth, CardHeight),
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
            _previewEnabled = false;
            DisposeSprites();
            WriteDiagnostic("Script abortado; sprites liberados.");
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