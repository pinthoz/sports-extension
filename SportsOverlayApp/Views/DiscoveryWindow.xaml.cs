using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
    /// Hidden browser that powers recommendations. It rotates through the
    /// FlashScore pages of the sports the user follows, today and then
    /// tomorrow, scraping every game (not just starred ones) so the
    /// recommendation engine has candidates to score. Uses its own profile:
    /// these public pages need no login.
    /// </summary>
    public partial class DiscoveryWindow : Window
    {
        // Canonical sport -> FlashScore URL slug, where they differ.
        private static readonly Dictionary<string, string> SportSlug = new(StringComparer.OrdinalIgnoreCase)
        {
            ["motorsport"] = "auto-racing",
            ["rugby"] = "rugby-union",
        };

        // Today plus this many following days are scanned per sport: the bar
        // uses today and tomorrow, the weekly agenda the whole range.
        private const int DaysAhead = 6;

        // Each sport gets two pages per rotation: today (so live scores of
        // recommended games stay fresh) and one later day, which cycles
        // through 1..DaysAhead across rotations to fill the week.
        private const int PagesPerSport = 2;
        private int laterDay = 1;

        // Clicks FlashScore's "Next day" arrow n times. It switches the list
        // client-side (no navigation), so the page's URL no longer names the
        // sport; the current step is tracked here instead. Rapid clicks are
        // dropped, hence the spacing (6 clicks take ~4s of the 8s tick).
        private static string NextDaysScript(int n) =>
            "(() => { if (!document.querySelector(\"[data-day-picker-arrow='next']\")) return false;" +
            $" let i = 0; const go = () => {{ const b = document.querySelector(\"[data-day-picker-arrow='next']\");" +
            $" if (b) b.click(); if (++i < {n}) setTimeout(go, 700); }}; go(); return true; }})()";

        private readonly DispatcherTimer timer;
        private readonly string discoverScript;
        private List<string> sports = new();
        private int step; // sport index * PagesPerSport + page (0 = today, 1 = laterDay)
        private bool ready;
        private bool dayShifted; // the last day jump actually started

        // Latest candidates per (sport, day), so a fresh scrape of one page
        // replaces only that page's games and the union is what gets published.
        private readonly Dictionary<(string sport, int day), List<GameData>> byPage = new();

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
            // One page per tick: scrape what finished loading, then move on.
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            timer.Tick += async (s, e) => await TickAsync();
        }

        public async Task InitializeAsync()
        {
            var dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SportsOverlay", "WebView2-Discovery");
            var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
            await Browser.EnsureCoreWebView2Async(env);
            Browser.CoreWebView2.IsMuted = true;
            await AdBlocker.ApplyAsync(Browser.CoreWebView2);
            ready = true;
            RefreshSports();
            if (sports.Count > 0)
                Browser.CoreWebView2.Navigate(UrlFor(sports[0]));
            timer.Start();
        }

        private static string UrlFor(string sport) =>
            $"https://www.flashscore.com/{(SportSlug.TryGetValue(sport, out var slug) ? slug : sport)}/";

        private int StepCount => sports.Count * PagesPerSport;

        private void RefreshSports()
        {
            var current = (SportsProvider?.Invoke() ?? Array.Empty<string>())
                .Where(sp => sp != "")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (current.SequenceEqual(sports, StringComparer.OrdinalIgnoreCase)) return;
            sports = current;
            step = 0;
            // Drop candidates for sports no longer followed.
            foreach (var key in byPage.Keys.Where(k => !sports.Contains(k.sport, StringComparer.OrdinalIgnoreCase)).ToList())
                byPage.Remove(key);
        }

        private async Task TickAsync()
        {
            if (!ready || Browser.CoreWebView2 == null) return;
            if (step == 0) RefreshSports();
            if (sports.Count == 0) return;

            var sport = sports[step / PagesPerSport];
            var day = step % PagesPerSport == 0 ? 0 : laterDay;

            try
            {
                // Scrape the page that has been loading since the last tick.
                // A later day only counts if the date picker really moved.
                if (day == 0 || dayShifted)
                {
                    var raw = await Browser.CoreWebView2.ExecuteScriptAsync(discoverScript);
                    var json = JsonConvert.DeserializeObject<string>(raw);
                    if (!string.IsNullOrEmpty(json))
                    {
                        var games = GameParser.FromJArray(JArray.Parse(json));
                        foreach (var g in games)
                            g.KickOff = KickOffTime(g, day);
                        if (day > 0)
                            games = LaterDay(games, day, byPage.GetValueOrDefault((sport, 0)));
                        byPage[(sport, day)] = games;
                        CandidatesScraped?.Invoke(byPage.Values.SelectMany(g => g).ToList());
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Discovery scrape error: {ex.Message}");
            }

            // Advance: a later day of the same sport is a few clicks away; a new
            // sport is a fresh navigation (which always opens on today). After
            // a full rotation, the next rotation looks one day further ahead.
            step = (step + 1) % StepCount;
            if (step == 0)
                laterDay = laterDay % DaysAhead + 1;
            if (step % PagesPerSport == 0)
            {
                dayShifted = false;
                Browser.CoreWebView2.Navigate(UrlFor(sports[step / PagesPerSport]));
            }
            else
            {
                try
                {
                    dayShifted = await Browser.CoreWebView2.ExecuteScriptAsync(NextDaysScript(laterDay)) == "true";
                }
                catch
                {
                    dayShifted = false;
                }
            }
        }

        // A scheduled game's stage is its kick-off, "HH:mm" in the browser's
        // (the user's) time zone, sometimes with a marker glued on ("14:30FRO",
        // result-only coverage). Live/finished games have no future start.
        private static readonly Regex KickOffStage = new(@"^(\d{1,2}):(\d{2})");

        private static DateTime? KickOffTime(GameData g, int day)
        {
            if (g.IsLive || g.IsFinished) return null;
            var m = KickOffStage.Match(g.Time.Trim());
            if (!m.Success) return null;
            int h = int.Parse(m.Groups[1].Value), min = int.Parse(m.Groups[2].Value);
            return h < 24 && min < 60 ? DateTime.Today.AddDays(day).AddHours(h).AddMinutes(min) : null;
        }

        /// <summary>
        /// Tags games from a later day. Their stage is just a kick-off time, so
        /// it gets the weekday prepended to not read as today. Games also seen
        /// on today's page are dropped: that means the clicks didn't switch the
        /// list in time and this is still today.
        /// </summary>
        private static List<GameData> LaterDay(List<GameData> games, int day, List<GameData>? today)
        {
            var seen = new HashSet<string>(today?.Select(g => g.Id) ?? Enumerable.Empty<string>());
            var weekday = DayLabel.Prefix(DateTime.Today.AddDays(day));
            var result = new List<GameData>();
            foreach (var g in games)
            {
                if (seen.Contains(g.Id)) continue;
                g.DayOffset = day;
                if (!g.IsLive && !g.IsFinished)
                    g.Time = $"{weekday} {g.Time}";
                result.Add(g);
            }
            return result;
        }

        public void Shutdown()
        {
            timer.Stop();
            Close();
        }
    }
}
