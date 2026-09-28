using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace SportsOverlayApp.Utils
{
    /// <summary>
    /// Gives a normal (framed) window the Windows 11 look: dark title bar and
    /// the Mica backdrop behind a transparent client area, plus the user's
    /// accent colour as the "AccentBrush" resource. On older Windows the
    /// window keeps its solid background.
    /// </summary>
    public static class FluentWindow
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

        [StructLayout(LayoutKind.Sequential)]
        private struct Margins { public int Left, Right, Top, Bottom; }

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38; // Windows 11 22H2+
        private const int DWMSBT_MAINWINDOW = 2;          // Mica

        public static void Apply(Window window)
        {
            window.Resources["AccentBrush"] = new SolidColorBrush(AccentForDark());
            // Solid dark surface until (or unless) Mica takes over.
            window.Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
            // Defaults that text inherits (the Fluent dictionary resets the
            // app-wide TextBlock style so inheritance applies).
            window.Foreground = Brushes.White;
            window.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            window.FontSize = 14;
            window.SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                int on = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));

                if (Environment.OSVersion.Version.Build < 22621) return;
                int mica = DWMSBT_MAINWINDOW;
                if (DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref mica, sizeof(int)) != 0) return;

                // Mica draws behind the client area only where WPF paints nothing.
                var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref margins);
                if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
                    target.BackgroundColor = Colors.Transparent;
                window.Background = Brushes.Transparent;
            };
        }

        /// <summary>
        /// The Windows accent colour, lightened the way dark mode shows it on
        /// buttons and progress (the raw accent is too dark on a dark surface).
        /// </summary>
        private static Color AccentForDark()
        {
            var fallback = Color.FromRgb(0x60, 0xCD, 0xFF); // Windows default blue, dark-mode shade
            try
            {
                if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "AccentColor", null) is int abgr)
                {
                    byte r = (byte)abgr, g = (byte)(abgr >> 8), b = (byte)(abgr >> 16);
                    return Lighten(Color.FromRgb(r, g, b), 0.45);
                }
            }
            catch
            {
                // Registry unavailable: use the default.
            }
            return fallback;
        }

        private static Color Lighten(Color c, double amount) => Color.FromRgb(
            (byte)(c.R + (255 - c.R) * amount),
            (byte)(c.G + (255 - c.G) * amount),
            (byte)(c.B + (255 - c.B) * amount));
    }
}
