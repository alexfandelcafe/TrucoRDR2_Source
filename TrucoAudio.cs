using RDR2.Native;

namespace TrucoRDR2
{
    internal enum TrucoSound
    {
        Select,
        Deal,
        Accept,
        Reject,
        Win
    }

    /// <summary>Small wrapper around RDR2's built-in frontend UI sounds.</summary>
    internal static class TrucoAudio
    {
        public static void Play(bool enabled, TrucoSound sound)
        {
            if (!enabled) return;

            string name;
            string soundSet;

            switch (sound)
            {
                case TrucoSound.Deal:
                    name = "MENU_ENTER";
                    soundSet = "HUD_PLAYER_MENU";
                    break;
                case TrucoSound.Accept:
                    name = "SELECT";
                    soundSet = "HUD_SHOP_SOUNDSET";
                    break;
                case TrucoSound.Reject:
                    name = "BACK";
                    soundSet = "HUD_SHOP_SOUNDSET";
                    break;
                case TrucoSound.Win:
                    name = "MENU_CLOSE";
                    soundSet = "HUD_PLAYER_MENU";
                    break;
                default:
                    name = "SELECT";
                    soundSet = "HUD_SHOP_SOUNDSET";
                    break;
            }

            try
            {
                AUDIO.PLAY_SOUND_FRONTEND(name, soundSet, true, 0);
            }
            catch
            {
                // Audio is cosmetic; it must never interrupt a game action.
            }
        }
    }
}