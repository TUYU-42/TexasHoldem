using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Reflection;

namespace TexasHoldem
{
    /// <summary>
    /// Plays short WAV sound effects bundled as embedded resources OR loaded from disk.
    /// Falls back silently if a sound is missing.
    /// </summary>
    public static class SoundManager
    {
        private static readonly Dictionary<string, SoundPlayer> _players = new Dictionary<string, SoundPlayer>();
        private static bool _enabled = true;
        public static bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        private static string SoundsDir
        {
            get
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string p = Path.Combine(baseDir, "Resources", "Sounds");
                if (Directory.Exists(p)) return p;
                // Walk up to find project Resources/Sounds (debug from VS without copying)
                var dir = new DirectoryInfo(baseDir);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "Resources", "Sounds");
                    if (Directory.Exists(candidate)) return candidate;
                    dir = dir.Parent;
                }
                return p;
            }
        }

        public static void Play(string key)
        {
            if (!_enabled) return;
            try
            {
                if (!_players.TryGetValue(key, out var sp))
                {
                    string path = Path.Combine(SoundsDir, key + ".wav");
                    if (!File.Exists(path)) return;
                    sp = new SoundPlayer(path);
                    sp.Load();
                    _players[key] = sp;
                }
                sp.Play();
            }
            catch
            {
                // Ignore audio failures - game continues without sound
            }
        }
    }
}
