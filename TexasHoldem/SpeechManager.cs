using System;
using System.Speech.Synthesis;
using System.Threading.Tasks;

namespace TexasHoldem
{
    /// <summary>
    /// Text-to-speech wrapper around System.Speech.Synthesis.SpeechSynthesizer.
    /// Speaks asynchronously so UI doesn't block, and queues short phrases for player actions.
    /// </summary>
    public static class SpeechManager
    {
        private static SpeechSynthesizer _synth;
        private static bool _enabled = true;
        private static bool _initialized = false;

        public static bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                if (!value) SilenceNow();
            }
        }

        private static void EnsureInit()
        {
            if (_initialized) return;
            _initialized = true;
            try
            {
                _synth = new SpeechSynthesizer();
                _synth.Rate = 1;       // slightly faster than default
                _synth.Volume = 90;
                // Prefer a Chinese voice if installed, otherwise fall back to default (English)
                try
                {
                    foreach (var v in _synth.GetInstalledVoices())
                    {
                        var info = v.VoiceInfo;
                        if (info != null && info.Culture != null &&
                            (info.Culture.Name.StartsWith("zh") || info.Culture.Name.StartsWith("ja")))
                        {
                            _synth.SelectVoice(info.Name);
                            break;
                        }
                    }
                }
                catch { /* keep default voice */ }
            }
            catch
            {
                _synth = null; // TTS not available on this machine
            }
        }

        /// <summary>
        /// Speak a phrase asynchronously. Cancels any prior in-progress speech first
        /// so quick consecutive actions don't pile up.
        /// </summary>
        public static void Speak(string text)
        {
            if (!_enabled || string.IsNullOrWhiteSpace(text)) return;
            EnsureInit();
            if (_synth == null) return;
            try
            {
                _synth.SpeakAsyncCancelAll();
                _synth.SpeakAsync(text);
            }
            catch
            {
                // Ignore TTS failures - game continues silently
            }
        }

        /// <summary>
        /// Speaks the action of a given player. Player names that are pure English (Alex, Bella…)
        /// will be read naturally; "You" is special-cased to "你".
        /// </summary>
        public static void SpeakAction(string playerName, PlayerAction action, int amount)
        {
            // Strip the Chinese name suffix "玩家" if present to keep speech short
            string spoken = playerName.Replace(" 玩家", "").Trim();
            string phrase;
            switch (action)
            {
                case PlayerAction.Fold:
                    phrase = $"{spoken} 棄牌"; break;
                case PlayerAction.Check:
                    phrase = $"{spoken} 過牌"; break;
                case PlayerAction.Call:
                    phrase = $"{spoken} 跟注 {amount}"; break;
                case PlayerAction.Raise:
                    phrase = $"{spoken} 加注到 {amount}"; break;
                case PlayerAction.AllIn:
                    phrase = $"{spoken} 全下！"; break;
                default:
                    return;
            }
            Speak(phrase);
        }

        public static void SilenceNow()
        {
            try { _synth?.SpeakAsyncCancelAll(); } catch { }
        }
    }
}
