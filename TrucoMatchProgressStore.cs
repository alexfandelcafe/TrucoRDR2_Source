using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TrucoRDR2
{
    /// <summary>
    /// A small checkpoint for the active match. It intentionally stores only match-level
    /// progress; the current three-card hand is restarted from its beginning after a game reload.
    /// </summary>
    internal sealed class TrucoMatchProgress
    {
        public int TargetPoints { get; private set; }
        public int PlayerScore { get; private set; }
        public int OpponentScore { get; private set; }
        public TrucoPlayer Mano { get; private set; }
        public bool FlorEnabled { get; private set; }
        public bool IsComplete { get; private set; }
        public TrucoPlayer? Winner { get; private set; }

        public TrucoMatchProgress(int targetPoints, int playerScore, int opponentScore,
            TrucoPlayer mano, bool florEnabled, bool isComplete, TrucoPlayer? winner)
        {
            if (targetPoints != 15 && targetPoints != 30)
                throw new ArgumentOutOfRangeException("targetPoints");
            if (playerScore < 0 || opponentScore < 0)
                throw new ArgumentOutOfRangeException("playerScore");
            if (isComplete && !winner.HasValue)
                throw new ArgumentException("Una partida terminada debe tener ganador.", "winner");

            TargetPoints = targetPoints;
            PlayerScore = playerScore;
            OpponentScore = opponentScore;
            Mano = mano;
            FlorEnabled = florEnabled;
            IsComplete = isComplete;
            Winner = winner;
        }
    }

    /// <summary>Stores and validates a single active-match checkpoint; no match history is kept.</summary>
    internal static class TrucoMatchProgressStore
    {
        public static void Save(string path, TrucoMatchProgress progress)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("La ruta del guardado no puede estar vacía.", "path");
            if (progress == null)
                throw new ArgumentNullException("progress");

            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string[] lines =
            {
                "Version=1",
                "TargetPoints=" + progress.TargetPoints.ToString(CultureInfo.InvariantCulture),
                "PlayerScore=" + progress.PlayerScore.ToString(CultureInfo.InvariantCulture),
                "OpponentScore=" + progress.OpponentScore.ToString(CultureInfo.InvariantCulture),
                "Mano=" + progress.Mano.ToString(),
                "FlorEnabled=" + progress.FlorEnabled.ToString().ToLowerInvariant(),
                "IsComplete=" + progress.IsComplete.ToString().ToLowerInvariant(),
                "Winner=" + (progress.Winner.HasValue ? progress.Winner.Value.ToString() : string.Empty)
            };

            string temporaryPath = fullPath + ".tmp";
            try
            {
                File.WriteAllLines(temporaryPath, lines, new UTF8Encoding(false));
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

        public static bool TryLoad(string path, out TrucoMatchProgress progress)
        {
            progress = null;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            try
            {
                Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string rawLine in File.ReadAllLines(path))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#"))
                        continue;

                    int equals = line.IndexOf('=');
                    if (equals <= 0)
                        continue;

                    values[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
                }

                int version;
                int target;
                int playerScore;
                int opponentScore;
                bool flor;
                bool complete;
                TrucoPlayer mano;
                TrucoPlayer winner;

                if (!ReadInt(values, "Version", out version) || version != 1 ||
                    !ReadInt(values, "TargetPoints", out target) ||
                    !ReadInt(values, "PlayerScore", out playerScore) ||
                    !ReadInt(values, "OpponentScore", out opponentScore) ||
                    !ReadBool(values, "FlorEnabled", out flor) ||
                    !ReadBool(values, "IsComplete", out complete) ||
                    !ReadEnum(values, "Mano", out mano))
                    return false;

                TrucoPlayer? nullableWinner = null;
                string winnerText;
                if (values.TryGetValue("Winner", out winnerText) && winnerText.Length > 0)
                {
                    if (!Enum.TryParse(winnerText, true, out winner) ||
                        !Enum.IsDefined(typeof(TrucoPlayer), winner))
                        return false;
                    nullableWinner = winner;
                }

                progress = new TrucoMatchProgress(target, playerScore, opponentScore,
                    mano, flor, complete, nullableWinner);
                return true;
            }
            catch
            {
                progress = null;
                return false;
            }
        }

        public static void Delete(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }

        private static bool ReadInt(IDictionary<string, string> values, string key, out int result)
        {
            result = 0;
            string raw;
            return values.TryGetValue(key, out raw) &&
                int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        private static bool ReadBool(IDictionary<string, string> values, string key, out bool result)
        {
            result = false;
            string raw;
            return values.TryGetValue(key, out raw) && bool.TryParse(raw, out result);
        }

        private static bool ReadEnum(IDictionary<string, string> values, string key, out TrucoPlayer result)
        {
            result = TrucoPlayer.Player;
            string raw;
            return values.TryGetValue(key, out raw) &&
                Enum.TryParse(raw, true, out result) &&
                Enum.IsDefined(typeof(TrucoPlayer), result);
        }
    }
}