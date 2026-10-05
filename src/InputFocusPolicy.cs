namespace LearningIme {
    internal static class InputFocusPolicy {
        // Suspend reading during popup interaction or a transient non-editor focus
        // in the same application. Never read a suspended editor's old document.
        public static bool Suspend(int own, int foreground, int focused, int bound, bool editable, bool password) {
            if (foreground == own || focused == own) return true;
            return !editable && !password && bound != 0 && foreground == bound
                && (focused == 0 || focused == bound);
        }
    }
}
