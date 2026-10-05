using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LearningIme {
    internal sealed class CardForm : Form {
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        public event Action<string> StateChanged;
        private int paintCount;
        private bool reportPaint;
        public object DiagnosticState { get { return new { visible = Visible, native_visible = IsHandleCreated && IsWindowVisible(Handle), left = Left, top = Top, width = Width, height = Height,
            paint_count = paintCount, source_length = source.Length, translation_length = english.Length, on_screen = Screen.FromRectangle(Bounds).WorkingArea.IntersectsWith(Bounds) }; } }
        private void Report(string reason) { if (StateChanged != null) StateChanged(reason); }
        private string source = "", english = "", terms = "";
        private readonly Timer dismiss = new Timer();
        private readonly Settings settings;
        private bool dragging;
        private Point dragStart, windowStart;
        private Snapshot lastSnapshot;
        public CardForm(Settings settings) : this(settings.dismiss_ms) { this.settings = settings; }
        public CardForm(int dismissMs) {
            Text = "英语学习悬浮窗";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; TopMost = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(248, 250, 253);
            DoubleBuffered = true;
            Width = 550; Height = 190;
            dismiss.Interval = dismissMs;
            dismiss.Tick += (_, __) => { dismiss.Stop(); Hide(); Report("显示时间结束"); };
            MouseDown += (_, e) => {
                if (e.Button != MouseButtons.Left) return;
                if (e.X > ClientSize.Width - 42 && e.Y < 42) { dismiss.Stop(); Hide(); Report("点击关闭"); return; }
                if (e.Y > 36) return;
                dragging = true; dragStart = Cursor.Position; windowStart = Location;
                Capture = true; dismiss.Stop();
            };
            MouseMove += (_, e) => {
                Cursor = e.Y <= 36 && e.X < ClientSize.Width - 42 ? Cursors.SizeAll : Cursors.Default;
                if (dragging) Location = new Point(windowStart.X + Cursor.Position.X - dragStart.X, windowStart.Y + Cursor.Position.Y - dragStart.Y);
            };
            MouseUp += (_, e) => {
                if (!dragging) return;
                dragging = false; Capture = false;
                if (settings != null) {
                    settings.fixed_position = true; settings.fixed_x = Left; settings.fixed_y = Top;
                    try { settings.Save(); } catch (System.IO.IOException) { }
                }
                dismiss.Start();
            };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams {
            get { var value = base.CreateParams; value.ExStyle |= 0x08000000 | 0x00000080; return value; }
        }
        protected override void WndProc(ref Message m) {
            if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; } // WM_MOUSEACTIVATE / MA_NOACTIVATE
            base.WndProc(ref m);
        }
        public void Present(Snapshot snapshot, string chinese, string translation) {
            if (string.IsNullOrEmpty(translation)) { dismiss.Stop(); Hide(); Report("输入状态取消显示"); return; }
            source = chinese; english = translation; terms = Glossary.LearningLine(chinese);
            lastSnapshot = snapshot;
            AccessibleName = english;
            AccessibleDescription = source + " " + terms;
            var anchor = snapshot.rect ?? new Anchor { left = Cursor.Position.X, top = Cursor.Position.Y, bottom = Cursor.Position.Y + 18 };
            bool fixedPosition = settings != null && settings.fixed_position;
            var position = fixedPosition ? new Point(settings.fixed_x, settings.fixed_y) : new Point(anchor.left, anchor.top);
            Rectangle work = Screen.FromPoint(position).WorkingArea;
            Width = Math.Min(550, work.Width - 20);
            using (var enFont = new Font("Segoe UI", 13))
            using (var smallFont = new Font("Microsoft YaHei UI", 9)) {
                int enHeight = TextRenderer.MeasureText(english, enFont, new Size(Width - 40, 0), TextFormatFlags.WordBreak).Height;
                int zhHeight = TextRenderer.MeasureText(source, smallFont, new Size(Width - 40, 0), TextFormatFlags.WordBreak).Height;
                int termHeight = terms.Length == 0 ? 0 : TextRenderer.MeasureText(terms, smallFont, new Size(Width - 40, 0), TextFormatFlags.WordBreak).Height + 14;
                Height = Math.Min(work.Height - 24, 68 + enHeight + zhHeight + termHeight);
            }
            int x = Math.Max(work.Left + 8, Math.Min(anchor.left, work.Right - Width - 8));
            int y = anchor.bottom + 12;
            if (y + Height > work.Bottom - 8) y = Math.Max(work.Top + 8, anchor.top - Height - 12);
            y = Math.Max(work.Top + 8, Math.Min(y, work.Bottom - Height - 8));
            if (fixedPosition) {
                x = Math.Max(work.Left + 8, Math.Min(settings.fixed_x, work.Right - Width - 8));
                y = Math.Max(work.Top + 8, Math.Min(settings.fixed_y, work.Bottom - Height - 8));
            }
            if (!dragging) Location = new Point(x, y);
            if (!Visible) Show();
            // Keep the learning window above normal app windows without taking input focus.
            SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10 | 0x40);
            reportPaint = true;
            Invalidate(); dismiss.Stop(); dismiss.Start(); Report("显示译文");
        }
        public void ExportPreview(string path) {
            using (var bitmap = new Bitmap(Width, Height)) {
                DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        public void SetDismiss(int milliseconds) {
            dismiss.Interval = milliseconds;
            if (Visible && lastSnapshot != null) Present(lastSnapshot, source, english);
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var border = new Pen(Color.FromArgb(201, 214, 232))) e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            using (var title = new Font("Microsoft YaHei UI", 9))
            using (var enFont = new Font("Segoe UI", 13))
            using (var zhFont = new Font("Microsoft YaHei UI", 9)) {
                var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
                TextRenderer.DrawText(e.Graphics, "英语学习  ·  拖动标题可固定位置", title, new Point(20, 13), Color.FromArgb(68, 107, 160));
                TextRenderer.DrawText(e.Graphics, "×", enFont, new Point(Width - 30, 7), Color.Gray);
                int enHeight = TextRenderer.MeasureText(english, enFont, new Size(Width - 40, 0), TextFormatFlags.WordBreak).Height;
                TextRenderer.DrawText(e.Graphics, english, enFont, new Rectangle(20, 42, Width - 40, enHeight + 4), Color.FromArgb(26, 39, 59), flags);
                int top = 52 + enHeight;
                int zhHeight = TextRenderer.MeasureText(source, zhFont, new Size(Width - 40, 0), TextFormatFlags.WordBreak).Height;
                TextRenderer.DrawText(e.Graphics, source, zhFont, new Rectangle(20, top, Width - 40, zhHeight + 4), Color.FromArgb(100, 110, 125), flags);
                if (terms.Length > 0) {
                    top += zhHeight + 12;
                    TextRenderer.DrawText(e.Graphics, terms, zhFont, new Rectangle(20, top, Width - 40, Height - top - 10), Color.FromArgb(49, 107, 121), flags);
                }
            }
            paintCount++;
            if (reportPaint) { reportPaint = false; Report("完成绘制"); }
        }
        protected override void Dispose(bool disposing) {
            if (disposing) dismiss.Dispose(); base.Dispose(disposing);
        }
    }
}
