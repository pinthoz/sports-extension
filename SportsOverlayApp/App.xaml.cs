using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using SportsOverlayApp.Models;
using SportsOverlayApp.Services;
using SportsOverlayApp.Utils;
using SportsOverlayApp.Views;

namespace SportsOverlayApp
{
    public partial class App : System.Windows.Application
    {
        private NotifyIcon? trayIcon;
        private MainWindow? overlay;
        private FlashScoreWindow? flashWindow;
        private DiscoveryWindow? discoveryWindow;
        private ScoreServer? scoreServer;
        private DispatcherTimer? staleTimer;
        private DateTime lastDataAt = DateTime.MinValue;
        private UserPreferences preferences = new();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            preferences = CacheService.LoadPreferences();

            overlay = new MainWindow();
            overlay.ApplyUserPreferences(preferences);

            // Show the last cached scores until fresh data arrives.
            var cached = CacheService.LoadGameData();
            if (cached.Count > 0)
                overlay.UpdateGameData(cached);

            SetupSystemTray();
            StartScoreServer(preferences.WebSocketPort);
            SetupStaleTimer();

            overlay.Show();
            overlay.Reposition();

            if (preferences.DataSource == DataSource.BuiltIn)
            {
                _ = StartEmbeddedScraperAsync();
                if (preferences.EnableRecommendations)
                    _ = StartDiscoveryScraperAsync();
            }
        }

        /// <summary>Creates the hidden discovery browser that scrapes broad sport
        /// pages to find recommendable (not-yet-starred) games.</summary>
        private async System.Threading.Tasks.Task StartDiscoveryScraperAsync()
        {
            if (discoveryWindow != null || overlay == null) return;
            try
            {
                discoveryWindow = new DiscoveryWindow
                {
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    Left = -32000,
                    Top = -32000
                };
                discoveryWindow.SportsProvider = () => overlay.FollowedSports();
                discoveryWindow.CandidatesScraped += games =>
                    Dispatcher.Invoke(() => overlay?.UpdateCandidates(games));
                discoveryWindow.Show();
                await discoveryWindow.InitializeAsync();
                discoveryWindow.Hide();
            }
            catch (Exception ex)
            {
                discoveryWindow = null;
                System.Diagnostics.Debug.WriteLine($"Discovery browser failed to start: {ex.Message}");
            }
        }

        /// <summary>Handles scraped games coming from either source; only the
        /// source selected in Settings actually drives the bar.</summary>
        private void OnGamesReceived(List<GameData> games, DataSource source)
        {
            if (preferences.DataSource != source)
                return;

            lastDataAt = DateTime.Now;
            Dispatcher.Invoke(() =>
            {
                overlay?.UpdateGameData(games);
                overlay?.SetConnected(true);
                UpdateTrayTooltip(games);
            });
            CacheService.SaveGameData(games);
        }

        private void StartScoreServer(int port)
        {
            scoreServer?.Dispose();
            scoreServer = new ScoreServer();

            scoreServer.GamesReceived += games => OnGamesReceived(games, DataSource.Extension);
            scoreServer.ConnectionChanged += connected =>
            {
                if (preferences.DataSource == DataSource.Extension)
                    Dispatcher.Invoke(() => overlay?.SetConnected(connected));
            };

            try
            {
                scoreServer.Start(port);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebSocket server error: {ex.Message}");
                if (preferences.DataSource == DataSource.Extension)
                {
                    System.Windows.MessageBox.Show(
                        $"Could not start the local WebSocket server on port {port}:\n{ex.Message}\n\n" +
                        "Change the port in Settings (and in the extension popup) and try again.",
                        "Sports Overlay", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        /// <summary>Creates the hidden embedded FlashScore browser. The window is
        /// briefly shown off-screen because WebView2 needs a live HWND to initialize.</summary>
        private async System.Threading.Tasks.Task StartEmbeddedScraperAsync()
        {
            if (flashWindow != null) return;
            try
            {
                flashWindow = new FlashScoreWindow(preferences.FlashScoreUrl)
                {
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    Left = -32000,
                    Top = -32000
                };
                flashWindow.GamesScraped += games => OnGamesReceived(games, DataSource.BuiltIn);
                flashWindow.Show();
                await flashWindow.InitializeAsync();
                flashWindow.Hide();
            }
            catch (Exception ex)
            {
                flashWindow = null;
                System.Windows.MessageBox.Show(
                    $"Could not start the embedded FlashScore browser:\n{ex.Message}\n\n" +
                    "Make sure the WebView2 Runtime is installed, or switch the data source " +
                    "to \"Browser extension\" in Settings.",
                    "Sports Overlay", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void OpenFlashScoreWindow()
        {
            if (flashWindow == null)
                await StartEmbeddedScraperAsync();
            flashWindow?.ShowForUser();
        }

        // Marks the bar as offline when the extension stops sending data
        // (tab closed, browser closed) even if the socket never closed cleanly.
        private void SetupStaleTimer()
        {
            staleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            staleTimer.Tick += (s, e) =>
            {
                if (DateTime.Now - lastDataAt > TimeSpan.FromSeconds(30))
                    overlay?.SetConnected(false);
            };
            staleTimer.Start();
        }

        private void UpdateTrayTooltip(List<GameData> games)
        {
            if (trayIcon == null) return;
            if (games.Count > 0)
            {
                var g = games[0];
                var text = $"{g.HomeTeam} {g.Score} {g.AwayTeam} ({g.Time})";
                // NotifyIcon.Text is limited to 127 chars.
                trayIcon.Text = text.Length > 127 ? text.Substring(0, 127) : text;
            }
            else
            {
                trayIcon.Text = "Sports Overlay: no starred games";
            }
        }

        private void SetupSystemTray()
        {
            trayIcon = new NotifyIcon
            {
                Icon = CreateTrayIcon(),
                Visible = true,
                Text = "Sports Overlay"
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Choose Games (FlashScore)...", null, (s, a) => OpenFlashScoreWindow());
            contextMenu.Items.Add("Show/Hide Bar", null, (s, a) => ToggleOverlay());
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Settings", null, (s, a) => OpenSettings());
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Exit", null, (s, a) => ShutdownApp());

            trayIcon.ContextMenuStrip = contextMenu;
            trayIcon.Click += (s, args) =>
            {
                if (((MouseEventArgs)args).Button == MouseButtons.Left)
                    ToggleOverlay();
            };
        }

        private System.Drawing.Icon CreateTrayIcon()
        {
            try
            {
                var icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "app.ico");
                if (System.IO.File.Exists(icoPath))
                    return new System.Drawing.Icon(icoPath, 16, 16);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Tray icon load error: {ex.Message}");
            }

            // Fallback: simple drawn icon if app.ico is missing.
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
            if (overlay == null) return;
            if (overlay.IsVisible)
                overlay.Hide();
            else
            {
                overlay.Show();
                overlay.Reposition();
            }
        }

        private void OpenSettings()
        {
            var settingsWindow = new SettingsWindow();
            if (settingsWindow.ShowDialog() == true)
            {
                preferences = CacheService.LoadPreferences();
                overlay?.ApplyUserPreferences(preferences);
                StartupManager.SetStartWithWindows(preferences.StartWithWindows);
                StartScoreServer(preferences.WebSocketPort);
                if (preferences.DataSource == DataSource.BuiltIn && flashWindow == null)
                    _ = StartEmbeddedScraperAsync();
            }
        }

        private void ShutdownApp()
        {
            staleTimer?.Stop();
            scoreServer?.Dispose();
            flashWindow?.Shutdown();
            discoveryWindow?.Shutdown();
            trayIcon?.Dispose();
            Current.Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            staleTimer?.Stop();
            scoreServer?.Dispose();
            trayIcon?.Dispose();
            base.OnExit(e);
        }
    }
}
