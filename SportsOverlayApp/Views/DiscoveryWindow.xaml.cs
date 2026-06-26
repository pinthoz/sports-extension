using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// Hidden browser that powers recommendations. It rotates through the
    /// FlashScore pages of the sports the user follows, scraping every game
    /// (not just starred ones) so the recommendation engine has candidates to
    /// score. Shares the main embedded browser's user-data folder, so the
    /// user's FlashScore login carries over.
    /// </summary>
    public partial class DiscoveryWindow : Window
    {
        // Canonical sport -> FlashScore URL slug, where they differ.
        private static readonly Dictionary<string, string> SportSlug = new(StringComparer.OrdinalIgnoreCase)
        {
            ["motorsport"] = "auto-racing",
            ["rugby"] = "rugby-union",
        };

        private readonly DispatcherTimer timer;
        private readonly string discoverScript;
        private List<string> sportUrls = new();
        private int index;
        private bool ready;

        // Latest candidates per sport, so a fresh scrape of one sport replaces
        // only that sport's games and the union is what gets published.
        private readonly Dictionary<string, List<GameData>> bySport = new();

        /// <summary>Supplies the current set of followed sports (re-read each rotation).</summary>
        public Func<IReadOnlyList<string>>? SportsProvider;

        public event Action<List<GameData>>? CandidatesScraped;

        public DiscoveryWindow()
        {
            InitializeComponent();
            // Run the normal scraper in discovery mode (also returns unstarred games).
            var scraper = File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Resources", "scraper.js"));
            discoverScript = "window.__discoverMode = true;\n" + scraper;
            // One sport per tick: scrape what finished loading, then move on.
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            timer.Tick += async (s, e) => await TickAsync();
        }

        public async Task InitializeAsync()
        {
            // Its own profile folder: discovery only reads public sport pages
            // (no login needed), and a separate folder avoids any clash with the
            // main embedded browser sharing one WebView2 user-data directory.
            var dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SportsOverlay", "WebView2-Discovery");
            var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
            await Browser.EnsureCoreWebView2Async(env);
            Browser.CoreWebView2.IsMuted = true;
            ready = true;
            RefreshSports();
            if (sportUrls.Count > 0)
                Browser.CoreWebView2.Navigate(sportUrls[0]);
            timer.Start();
        }

        private void RefreshSports()
        {
            var sports = SportsProvider?.Invoke() ?? Array.Empty<string>();
            var urls = sports
                .Select(sp => SportSlug.TryGetValue(sp, out var slug) ? slug : sp)
                .Where(slug => slug != "")
                .Distinct()
                .Select(slug => $"https://www.flashscore.com/{slug}/")
                .ToList();

            if (urls.SequenceEqual(sportUrls)) return;
            sportUrls = urls;
            index = 0;
            // Drop candidates for sports no longer followed.
            foreach (var key in bySport.Keys.Where(k => !sports.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
                bySport.Remove(key);
        }

        private async Task TickAsync()
        {
            if (!ready || Browser.CoreWebView2 == null) return;
            if (index == 0) RefreshSports();
            if (sportUrls.Count == 0) return;

            try
            {
                // Scrape the page that has been loading since the last tick.
                var raw = await Browser.CoreWebView2.ExecuteScriptAsync(discoverScript);
                var json = JsonConvert.DeserializeObject<string>(raw);
                if (!string.IsNullOrEmpty(json))
                {
                    var games = GameParser.FromJArray(JArray.Parse(json));
                    var sport = SportFromUrl(Browser.CoreWebView2.Source);
                    if (sport != "")
                    {
                        bySport[sport] = games;
                        CandidatesScraped?.Invoke(bySport.Values.SelectMany(g => g).ToList());
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Discovery scrape error: {ex.Message}");
            }

            // Advance to the next sport and let it load before the next tick.
            index = (index + 1) % sportUrls.Count;
            Browser.CoreWebView2.Navigate(sportUrls[index]);
        }

        private static string SportFromUrl(string url)
        {
            try
            {
                var slug = new Uri(url).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                var pair = SportSlug.FirstOrDefault(kv => string.Equals(kv.Value, slug, StringComparison.OrdinalIgnoreCase));
                return pair.Key ?? slug;
            }
            catch
            {
                return "";
            }
        }

        public void Shutdown()
        {
            timer.Stop();
            Close();
        }
    }
}
