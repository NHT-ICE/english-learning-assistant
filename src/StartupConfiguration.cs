using System;

namespace LearningIme {
    internal static class StartupConfiguration {
        public static bool Ensure(Settings settings, Func<bool> configure) {
            if (settings.HasUsableKey) return true;
            return configure() && settings.HasUsableKey;
        }
    }
}
