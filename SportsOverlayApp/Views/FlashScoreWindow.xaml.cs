using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using SportsOverlayApp.Utils;
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
        private readonly string startUrl;
        private bool cookieResetTried;

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
            await AdBlocker.ApplyAsync(Browser.CoreWebView2);
            Browser.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            Browser.CoreWebView2.Navigate(startUrl);
            scrapeTimer.Start();
        }

        private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                cookieResetTried = false;
                return;
            }
            // FlashScore answers 400 when the request's Cookie header grows too
            // large (tracking/consent cookies pile up in the persistent profile).
            // Drop the site's cookies and retry once; the user may need to log in again.
            if (e.HttpStatusCode != 400 || cookieResetTried) return;
            cookieResetTried = true;
            var manager = Browser.CoreWebView2.CookieManager;
            var cookies = await manager.GetCookiesAsync("https://www.flashscore.com");
            foreach (var cookie in cookies)
                manager.DeleteCookie(cookie);
            Browser.CoreWebView2.Navigate(startUrl);
        }

        private async Task ScrapeAsync()
        {
            if (Browser.CoreWebView2 == null) return;
            // Only the favourites page lists every starred game across all
            // sports. While the user browses other tabs to star games, hold the
            // bar's last good state instead of narrowing it to that one sport.
            var src = Browser.CoreWebView2.Source ?? "";
            if (src.IndexOf("favourites", StringComparison.OrdinalIgnoreCase) < 0
                && src.IndexOf("favorites", StringComparison.OrdinalIgnoreCase) < 0)
                return;
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

        // Opens FlashScore's own search panel and puts the cursor in its box.
        // FlashScore has no search URL; the header button opens the panel
        // (button.searchIcon; #search-window on some layouts). Retries while
        // the page is still loading.
        private const string OpenSearchScript =
            "(() => { let n = 0; const go = () => {" +
            " const btn = document.querySelector('button.searchIcon') || document.querySelector('#search-window');" +
            " if (!btn) { if (++n < 50) setTimeout(go, 100); return; }" +
            " btn.click(); let m = 0;" +
            " const focus = () => { const i = document.querySelector('input.searchInput__input');" +
            " if (i) { i.focus(); return; } if (++m < 30) setTimeout(focus, 100); };" +
            " focus(); }; go(); })()";

        /// <summary>Shows the window with FlashScore's search open, ready to type.</summary>
        public async Task OpenSearchAsync()
        {
            ShowForUser();
            if (Browser.CoreWebView2 == null) return;
            Browser.Focus(); // so typing goes to the page
            await Browser.CoreWebView2.ExecuteScriptAsync(OpenSearchScript);
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
            // Keep scraping in the background — but always from the favourites
            // page. The user can browse to any sport tab to star games; when
            // they close this window we navigate back to Favourites so every
            // starred game (across all sports) shows up on the bar, even if
            // they forgot to return to the Favourites tab first.
            e.Cancel = true;
            NavigateToFavourites();
            Hide();
        }

        private void NavigateToFavourites()
        {
            if (Browser.CoreWebView2 == null) return;
            var current = Browser.CoreWebView2.Source ?? "";
            // FlashScore uses both spellings depending on locale (favourites/favorites).
            bool onFavourites = current.Contains("favourites", StringComparison.OrdinalIgnoreCase)
                                || current.Contains("favorites", StringComparison.OrdinalIgnoreCase);
            if (!onFavourites)
                Browser.CoreWebView2.Navigate(startUrl);
        }

        public void Shutdown()
        {
            scrapeTimer.Stop();
            Closing -= OnClosing;
            Close();
        }
    }
}
