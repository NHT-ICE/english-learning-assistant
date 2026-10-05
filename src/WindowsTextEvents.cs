using System;
using System.Runtime.InteropServices;
using LearningUia;

namespace LearningIme {
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class WindowsTextEvents : IUIAutomationTextEditTextChangedEventHandler, IUIAutomationEventHandler {
        private readonly Action<IUIAutomationElement, TextEditChangeType, string[]> deliver;
        public WindowsTextEvents(Action<IUIAutomationElement, TextEditChangeType, string[]> deliver) { this.deliver = deliver; }
        public void HandleTextEditTextChangedEvent(IUIAutomationElement sender, TextEditChangeType type, string[] strings) {
            try { deliver(sender, type, strings); } catch (Exception) { }
        }
        public void HandleAutomationEvent(IUIAutomationElement sender, int eventId) {
            // Modern Chromium signals committed edits with TextChanged, without Finalized.
            try { deliver(sender, (TextEditChangeType)0, null); } catch (Exception) { }
        }
    }
}
