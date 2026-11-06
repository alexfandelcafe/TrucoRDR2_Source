using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using RDR2;
using RDR2.Native;
using RDR2.UI;

namespace TrucoRDR2
{
    /// <summary>
    /// First in-game build for verifying the custom YTD before wiring in the full game/table logic.
    /// F7 toggles the preview. PageUp/PageDown browse the 40 card textures. Escape closes it.
    /// </summary>
    public sealed class TrucoScript : Script
    {
        private const string DefaultTextureDictionary = "trucor2_cards";
        private const Keys TogglePreviewKey = Keys.F7;
        private const int CardsShown = 3;
        private const float CardWidth = 180.0f;
        private const float CardHeight = 270.0f;
        private const float CardTop = 690.0f;

        private readonly string _textureDictionary;
        private readonly string[] _textureNames;
        private readonly PointF[] _cardPositions;
        private readonly Sprite[] _sprites = new Sprite[CardsShown];
        private readonly TextElement[] _cardLabels = new TextElement[CardsShown];
        private readonly TextElement _header;
        private readonly TextElement _instructions;
        private readonly TextElement _status;

        private bool _previewEnabled;
        private bool _textureRequestSent;
        private bool _spritesCreated;
        private int _firstCardIndex;

        public TrucoScript()
        {
            Interval = 0;
            _textureDictionary = ReadTextureDictionarySetting();
            _textureNames = CardTextureCatalog.CreateTrucoDeckTextureNames();

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
                    string.Empty,
                    new PointF(_cardPositions[i].X + CardWidth / 2.0f, 655.0f),
                    0.42f, Color.WhiteSmoke, Alignment.Center, true, true);
            }

            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += OnAborted;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == TogglePreviewKey)
            {
                SetPreviewEnabled(!_previewEnabled);
                e.Handled = true;
                return;
            }

            if (!_previewEnabled)
                return;

            if (e.KeyCode == Keys.PageDown)
            {
                _firstCardIndex = (_firstCardIndex + 1) % _textureNames.Length;
                if (_spritesCreated)
                    RebuildVisibleSprites();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.PageUp)
            {
                _firstCardIndex = (_firstCardIndex - 1 + _textureNames.Length) % _textureNames.Length;
                RebuildVisibleSprites();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                SetPreviewEnabled(false);
                e.Handled = true;
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!_previewEnabled)
                return;

            _header.Draw();
            _instructions.Draw();

            bool dictionaryExists = false;
            bool dictionaryLoaded = false;
            try
            {
                // LML must register the YTD as a streaming asset before the game can request it.
                dictionaryExists = TXD.DOES_STREAMED_TEXTURE_DICT_EXIST(_textureDictionary);
                if (dictionaryExists)
                {
                    dictionaryLoaded = TXD.HAS_STREAMED_TEXTURE_DICT_LOADED(_textureDictionary);
                    if (!dictionaryLoaded && !_textureRequestSent)
                    {
                        // Request once; Sprite's constructor also requests the dictionary after it loads.
                        TXD.REQUEST_STREAMED_TEXTURE_DICT(_textureDictionary, false);
                        _textureRequestSent = true;
                    }
                }
            }
            catch (Exception)
            {
                SetStatusCaption("Error consultando el diccionario " + _textureDictionary +
                                 ". Revisa el log de ScriptHook.");
                _status.Draw();
                return;
            }

            if (!dictionaryExists)
            {
                SetStatusCaption("LML no registro el YTD: " + _textureDictionary +
                                 ". Revisa lml\\stream o el install.xml.");
            }
            else if (!dictionaryLoaded)
            {
                SetStatusCaption("YTD registrado; esperando carga: " + _textureDictionary);
            }
            else
            {
                SetStatusCaption("Diccionario cargado: " + _textureDictionary +
                                 "  |  40 cartas esperadas.");
                if (!_spritesCreated)
                {
                    RebuildVisibleSprites();
                    _spritesCreated = true;
                }
            }

            _status.Draw();

            if (_spritesCreated && dictionaryLoaded)
            {
                for (int i = 0; i < CardsShown; i++)
                {
                    if (_sprites[i] != null)
                        _sprites[i].Draw();

                    _cardLabels[i].Draw();
                }
            }
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
                _textureRequestSent = false;
                _spritesCreated = false;
                RDR2.UI.Screen.PrintSubtitle("TrucoRDR2: vista de texturas activada. Av Pag/Re Pag cambia las cartas.");
            }
            else
            {
                DisposeSprites();
                _spritesCreated = false;
                _textureRequestSent = false;
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
            _previewEnabled = false;
            DisposeSprites();
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
