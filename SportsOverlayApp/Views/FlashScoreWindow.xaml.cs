using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SportsOverlayApp.Models;
using SportsOverlayApp.Services;

namespace SportsOverlayApp.Views
{
    /// <summary>
    /// Embedded FlashScore browser. Normally hidden; a timer scrapes the
    /// starred games off the page so no external browser tab is needed.
    /// Shown on demand so the user can log in and star/unstar games; closing
    /// only hides it, scraping continues in the background.
    /// </summary>
    public partial class FlashScoreWindow : Window
    {
        private readonly DispatcherTimer scrapeTimer;
        private readonly string scrapeScript;
        private string startUrl;

        public event Action<List<GameData>>? GamesScraped;

        public FlashScoreWindow(string url)
        {
            InitializeComponent();
            startUrl = url;
            scrapeScript = File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Resources", "scraper.js"));
            scrapeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            scrapeTimer.Tick += async (s, e) => await ScrapeAsync();
            Closing += OnClosing;
        }

        /// <summary>Initializes WebView2 with a persistent profile (cookies, login, local stars).</summary>
        public async Task InitializeAsync()
        {
            var dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SportsOverlay", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
            await Browser.EnsureCoreWebView2Async(env);
            Browser.CoreWebView2.IsMuted = true;
            Browser.CoreWebView2.Navigate(startUrl);
            scrapeTimer.Start();
        }

        private async Task ScrapeAsync()
        {
            if (Browser.CoreWebView2 == null) return;
            try
            {
                var raw = await Browser.CoreWebView2.ExecuteScriptAsync(scrapeScript);
                var json = JsonConvert.DeserializeObject<string>(raw);
                if (string.IsNullOrEmpty(json)) return;
                GamesScraped?.Invoke(GameParser.FromJArray(JArray.Parse(json)));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Embedded scrape error: {ex.Message}");
            }
        }

        public void ShowForUser()
        {
            ShowInTaskbar = true;
            ShowActivated = true;
            WindowState = WindowState.Normal;
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - Width) / 2;
            Top = area.Top + (area.Height - Height) / 2;
            Show();
            Activate();
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            // Keep scraping in the background; remember where the user navigated.
            e.Cancel = true;
            var url = Browser.CoreWebView2?.Source;
            if (!string.IsNullOrEmpty(url))
            {
                startUrl = url!;
                var prefs = CacheService.LoadPreferences();
                prefs.FlashScoreUrl = url!;
                CacheService.SavePreferences(prefs);
            }
            Hide();
        }

        public void Shutdown()
        {
            scrapeTimer.Stop();
            Closing -= OnClosing;
            Close();
        }
    }
}
