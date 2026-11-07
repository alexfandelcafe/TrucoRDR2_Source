using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TrucoRDR2
{
    /// <summary>Writes the user-editable gameplay options without discarding other INI entries.</summary>
    internal static class TrucoConfiguration
    {
        public static void SaveGameplaySettings(string iniPath, int targetPoints, bool florEnabled, bool soundEnabled)
        {
            if (string.IsNullOrWhiteSpace(iniPath))
                throw new ArgumentException("La ruta del INI no puede estar vacía.", "iniPath");
            if (targetPoints != 15 && targetPoints != 30)
                throw new ArgumentOutOfRangeException("targetPoints");

            List<string> lines = File.Exists(iniPath)
                ? new List<string>(File.ReadAllLines(iniPath))
                : new List<string>();

            SetValue(lines, "Game", "TargetPoints", targetPoints.ToString());
            SetValue(lines, "Game", "FlorEnabled", florEnabled.ToString().ToLowerInvariant());
            SetValue(lines, "Audio", "Enabled", soundEnabled.ToString().ToLowerInvariant());

            string fullPath = Path.GetFullPath(iniPath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string temporaryPath = fullPath + ".tmp";
            try
            {
                File.WriteAllLines(temporaryPath, lines.ToArray(), new UTF8Encoding(false));
                if (File.Exists(fullPath))
                {
                    try
                    {
                        File.Replace(temporaryPath, fullPath, null);
                    }
                    catch
                    {
                        File.Copy(temporaryPath, fullPath, true);
                        File.Delete(temporaryPath);
                    }
                }
                else
                {
                    File.Move(temporaryPath, fullPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        private static void SetValue(List<string> lines, string section, string key, string value)
        {
            int sectionStart = -1;
            int sectionEnd = lines.Count;

            for (int i = 0; i < lines.Count; i++)
            {
                string trimmed = lines[i].Trim();
                if (!IsSection(trimmed))
                    continue;

                string name = trimmed.Substring(1, trimmed.Length - 2).Trim();
                if (sectionStart >= 0)
                {
                    sectionEnd = i;
                    break;
                }

                if (string.Equals(name, section, StringComparison.OrdinalIgnoreCase))
                    sectionStart = i;
            }

            if (sectionStart < 0)
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Length > 0)
                    lines.Add(string.Empty);
                lines.Add("[" + section + "]");
                lines.Add(key + "=" + value);
                return;
            }

            int existingIndex = -1;
            for (int i = sectionStart + 1; i < sectionEnd; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#"))
                    continue;

                int equals = line.IndexOf('=');
                if (equals > 0 &&
                    string.Equals(line.Substring(0, equals).Trim(), key, StringComparison.OrdinalIgnoreCase))
                {
                    if (existingIndex < 0)
                    {
                        existingIndex = i;
                        lines[i] = key + "=" + value;
                    }
                    else
                    {
                        lines.RemoveAt(i);
                        i--;
                        sectionEnd--;
                    }
                }
            }

            if (existingIndex < 0)
                lines.Insert(sectionEnd, key + "=" + value);
        }

        private static bool IsSection(string line)
        {
            return line.Length >= 2 && line[0] == '[' && line[line.Length - 1] == ']';
        }
    }
}