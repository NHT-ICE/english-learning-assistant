using LearningUia;

namespace LearningIme {
    internal static class BoundedTextRange {
        public static bool IsReadOnly(IUIAutomationTextPattern pattern) {
            if (pattern == null) return false;
            try { var value = pattern.DocumentRange.GetAttributeValue(40015); return value is bool && (bool)value; }
            catch (System.Exception) { return false; }
        }
        // Chromium permits character movement past the edit into the surrounding page.
        // Clamp to the focused provider BEFORE reading any text.
        public static string Tail(IUIAutomationTextPattern pattern, IUIAutomationTextRange input, bool useEnd, bool requireAvailable = false) {
            if (pattern == null || input == null) {
                if (requireAvailable) throw new System.Runtime.InteropServices.COMException("当前文本范围暂不可用", unchecked((int)0x80040201));
                return "";
            }
            var owner = pattern.DocumentRange;
            if (owner == null) {
                if (requireAvailable) throw new System.Runtime.InteropServices.COMException("当前编辑区边界暂不可用", unchecked((int)0x80040201));
                return "";
            }
            // Short edit boxes need no backward character walk. Some Chromium
            // providers walk the entire surrounding page before clamping it.
            var range = owner.Clone();
            var endpoint = useEnd ? TextPatternRangeEndpoint.TextPatternRangeEndpoint_End : TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start;
            range.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, input, endpoint);
            if (range.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, owner, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start) < 0)
                range.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, owner, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start);
            if (range.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, owner, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End) > 0)
                range.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, owner, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End);
            // Moving an end before the owner also moves the start on some
            // providers, so restore it before the bounded prefix read.
            range.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, owner, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start);
            string prefix = range.GetText(512);
            if (prefix != null && prefix.Length < 512) return prefix;
            range.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, range, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End);
            range.MoveEndpointByUnit(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, TextUnit.TextUnit_Character, -512);
            if (range.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, owner, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start) < 0)
                range.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, owner, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start);
            string text = range.GetText(512);
            if (text == null && requireAvailable) throw new System.Runtime.InteropServices.COMException("当前文本暂不可用", unchecked((int)0x80040201));
            return text ?? "";
        }
    }
}
