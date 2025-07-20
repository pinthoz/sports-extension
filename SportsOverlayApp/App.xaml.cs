using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using SportsOverlayApp.Models;
using SportsOverlayApp.Services;
using SportsOverlayApp.Views;

namespace SportsOverlayApp
{
    public partial class App : System.Windows.Application
    {
        private NotifyIcon trayIcon;
        private MainWindow overlay;
        private DispatcherTimer updateTimer;
        private bool isOverlayMode = true;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Initialize overlay window
            overlay = new MainWindow();
            overlay.Hide();

            // Setup system tray
            SetupSystemTray();

            // Initialize update timer for real-time data
            SetupUpdateTimer();

            // Load user preferences
            var preferences = CacheService.LoadPreferences();
            overlay.ApplyUserPreferences(preferences);
        }

        private void SetupSystemTray()
        {
            trayIcon = new NotifyIcon()
            {
                Icon = CreateTrayIcon(),
                Visible = true,
                Text = "Sports Overlay - Click to toggle"
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Show Overlay", null, (s, a) => ToggleOverlay());
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Settings", null, (s, a) => OpenSettings());
            contextMenu.Items.Add("Refresh Data", null, (s, a) => RefreshData());
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Exit", null, (s, a) => Shutdown());

            trayIcon.ContextMenuStrip = contextMenu;
            trayIcon.Click += (s, args) => 
            {
                if (((MouseEventArgs)args).Button == MouseButtons.Left)
                    ToggleOverlay();
            };
        }

        private void SetupUpdateTimer()
        {
            updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5) // Update every 5 seconds
            };
            updateTimer.Tick += async (s, e) => await UpdateGameData();
            updateTimer.Start();
        }

        private System.Drawing.Icon CreateTrayIcon()
        {
            // Create a simple icon programmatically
            var bitmap = new System.Drawing.Bitmap(16, 16);
            using (var g = System.Drawing.Graphics.FromImage(bitmap))
            {
                g.Clear(System.Drawing.Color.Transparent);
                g.FillEllipse(System.Drawing.Brushes.DodgerBlue, 2, 2, 12, 12);
                g.DrawString("S", new System.Drawing.Font("Arial", 8, System.Drawing.FontStyle.Bold), 
                           System.Drawing.Brushes.White, 4, 1);
            }
            return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
        }

        private void ToggleOverlay()
        {
            if (overlay.IsVisible)
                overlay.Hide();
            else
                overlay.Show();
        }

        private void OpenSettings()
        {
            var settingsWindow = new SettingsWindow();
            if (settingsWindow.ShowDialog() == true)
            {
                var preferences = CacheService.LoadPreferences();
                overlay.ApplyUserPreferences(preferences);
                RefreshData();
            }
        }

        private async void RefreshData()
        {
            await UpdateGameData();
        }

        private async System.Threading.Tasks.Task UpdateGameData()
        {
            try
            {
                var gameData = await ApiService.GetLiveMatchesAsync();
                Dispatcher.Invoke(() => overlay.UpdateGameData(gameData));
                
                // Update tray tooltip with latest score
                if (gameData.Count > 0)
                {
                    var latestGame = gameData[0];
                    trayIcon.Text = $"Sports Overlay - {latestGame.Title}: {latestGame.Score}";
                }
            }
            catch (Exception ex)
            {
                // Handle API errors gracefully
                System.Diagnostics.Debug.WriteLine($"API Error: {ex.Message}");
            }
        }

        private void ShutdownApp()
        {
            updateTimer?.Stop();
            trayIcon?.Dispose();
            Current.ShutdownApp();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            updateTimer?.Stop();
            trayIcon?.Dispose();
            base.OnExit(e);
        }
    }
}