using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SportsOverlayApp.Utils
{
    /// <summary>
    /// The tray icon's right-click menu, drawn like a Windows 11 dark context
    /// menu: rounded corners, a rounded hover highlight, thin separators and a
    /// Segoe Fluent Icons glyph per item.
    /// </summary>
    public static class TrayMenu
    {
        private static readonly Color Background = Color.FromArgb(0x2C, 0x2C, 0x2C);
        private static readonly Color Hover = Color.FromArgb(0x3D, 0x3D, 0x3D);
        private static readonly Color Text = Color.FromArgb(0xF2, 0xF2, 0xF2);
        private static readonly Color Line = Color.FromArgb(0x40, 0x40, 0x40);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        public static ContextMenuStrip Create()
        {
            var menu = new ContextMenuStrip
            {
                Renderer = new FluentRenderer(),
                BackColor = Background,
                ForeColor = Text,
                Font = new Font("Segoe UI", 9f),
                Padding = new Padding(4),
                ShowImageMargin = true,
                ShowCheckMargin = false
            };
            // A roomy image slot so the glyph sits away from the edge, as in
            // Windows 11 menus; the glyph itself is drawn smaller inside it.
            var scale = menu.DeviceDpi / 96f;
            menu.ImageScalingSize = new Size((int)(20 * scale), (int)(16 * scale));
            // Windows 11 rounds any window that asks; older Windows ignores it.
            menu.HandleCreated += (s, e) =>
            {
                int round = DWMWCP_ROUND;
                DwmSetWindowAttribute(menu.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            };
            return menu;
        }

        public static void AddItem(ContextMenuStrip menu, string text, char glyph, Action onClick)
        {
            var item = new ToolStripMenuItem(text, Glyph(glyph, menu.ImageScalingSize), (s, e) => onClick())
            {
                ForeColor = Text,
                Padding = new Padding(2, 3, 8, 3)
            };
            menu.Items.Add(item);
        }

        public static void AddSeparator(ContextMenuStrip menu) =>
            menu.Items.Add(new ToolStripSeparator { AutoSize = false, Height = 7 });

        // Renders an icon-font glyph to a small bitmap for the item's image.
        private static Bitmap Glyph(char glyph, Size size)
        {
            var bmp = new Bitmap(size.Width, size.Height);
            using var g = Graphics.FromImage(bmp);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var px = size.Height * 0.8f;
            using var font = new Font("Segoe Fluent Icons", px, GraphicsUnit.Pixel);
            // Fall back to the Windows 10 icon font where Fluent Icons is missing.
            using var used = font.Name == "Segoe Fluent Icons"
                ? (Font)font.Clone()
                : new Font("Segoe MDL2 Assets", px, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Text);
            using var format = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
            // Right-aligned in the slot: the extra width becomes left padding.
            g.DrawString(glyph.ToString(), used, brush, new RectangleF(0, 0, size.Width, size.Height), format);
            return bmp;
        }

        private sealed class FluentRenderer : ToolStripRenderer
        {
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using var b = new SolidBrush(Background);
                e.Graphics.FillRectangle(b, e.AffectedBounds);
            }

            // The rounded window gets its border from DWM; draw none of our own.
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }

            protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected || !e.Item.Enabled) return;
                var r = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var path = RoundedRect(r, 4);
                using var b = new SolidBrush(Hover);
                e.Graphics.FillPath(b, path);
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = Text;
                // The image slot is taller than the text, and WinForms lays the
                // text out high; centre it on the item's full height instead.
                var r = e.TextRectangle;
                e.TextRectangle = new Rectangle(r.X, 0, r.Width, e.Item.Height);
                e.TextFormat = (e.TextFormat & ~(TextFormatFlags.Top | TextFormatFlags.Bottom))
                               | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                int y = e.Item.Height / 2;
                using var pen = new Pen(Line);
                e.Graphics.DrawLine(pen, 4, y, e.Item.Width - 4, y);
            }

            private static GraphicsPath RoundedRect(Rectangle r, int radius)
            {
                int d = radius * 2;
                var p = new GraphicsPath();
                p.AddArc(r.X, r.Y, d, d, 180, 90);
                p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                p.CloseFigure();
                return p;
            }
        }
    }
}
