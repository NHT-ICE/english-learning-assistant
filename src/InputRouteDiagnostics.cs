using System;
using System.IO;
using System.Web.Script.Serialization;

namespace LearningIme {
    // Observe routing without changing which input source is accepted. Metadata only.
    internal sealed class InputRouteDiagnostics {
        private readonly string path;
        private readonly object gate = new object();
        private int nativePackets, nativeIgnored, nativeTypedOnly, windowsSnapshots;
        private object lastNative, lastWindows;
        private DateTime nextSave;
        public InputRouteDiagnostics(string root) { path = Path.Combine(root, "input-route-status.json"); Save(); }
        public void Native(Snapshot packet, bool accepted) {
            if (!DiagnosticsPolicy.Enabled) return;
            if (!string.IsNullOrEmpty(packet.command)) return;
            lock (gate) {
                nativePackets++; if (!accepted) nativeIgnored++; if (packet.typed_only) nativeTypedOnly++;
                lastNative = new { utc = DateTime.UtcNow.ToString("o"), packet.scope, packet.typed_only, packet.composing, accepted, length = (packet.text ?? "").Length };
                Save();
            }
        }
        public void Windows(Snapshot packet) {
            if (!DiagnosticsPolicy.Enabled) return;
            lock (gate) {
                windowsSnapshots++;
                lastWindows = new { utc = DateTime.UtcNow.ToString("o"), packet.scope, packet.reason, packet.composing, length = (packet.text ?? "").Length };
                Save();
            }
        }
        private void Save() {
            if (!DiagnosticsPolicy.Enabled) return;
            if (DateTime.UtcNow < nextSave) return;
            nextSave = DateTime.UtcNow.AddSeconds(1);
            try { File.WriteAllText(path, new JavaScriptSerializer().Serialize(new { utc = DateTime.UtcNow.ToString("o"), native_packets = nativePackets, native_ignored = nativeIgnored, native_typed_only = nativeTypedOnly, windows_snapshots = windowsSnapshots, last_native = lastNative, last_windows = lastWindows })); } catch (IOException) { }
        }
    }
}
