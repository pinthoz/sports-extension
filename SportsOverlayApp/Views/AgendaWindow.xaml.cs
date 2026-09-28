using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SportsOverlayApp.Models;
using SportsOverlayApp.Utils;

namespace SportsOverlayApp.Views
{
    /// <summary>
    /// The week ahead: upcoming games that are starred, feature a followed
    /// team, or are recommended, grouped by day. Each can be opened or added
    /// to a calendar. Fills in as the discovery browser scans each day.
    /// </summary>
    public partial class AgendaWindow : Window
    {
        // Games have no published end time; this is long enough for football
        // and a typical tennis match.
        private static readonly TimeSpan EventLength = TimeSpan.FromHours(2);

        private readonly MainWindow overlay;
        private readonly DispatcherTimer refreshTimer;

        public event Action<string>? OpenMatch;

        public AgendaWindow(MainWindow overlay)
        {
            InitializeComponent();
            FluentWindow.Apply(this);
            this.overlay = overlay;
            // Discovery keeps adding days in the background; keep up with it.
            refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            refreshTimer.Tick += (s, e) => Refresh();
            refreshTimer.Start();
            Closed += (s, e) => refreshTimer.Stop();
            Refresh();
        }

        private void Refresh()
        {
            var items = overlay.Agenda();
            var accent = (Brush)FindResource("AccentBrush");
            var days = items
                .GroupBy(a => a.Game.KickOff!.Value.Date)
                .Select(d => new AgendaDayVm(d.Key, d.Select(a => new AgendaRowVm(a, accent)).ToList()))
                .ToList();
            DayList.ItemsSource = days;

            bool empty = days.Count == 0;
            EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            Legend.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            StatusText.Text = empty
                ? "Upcoming games you follow, starred or recommended."
                : $"{items.Count} upcoming game{(items.Count == 1 ? "" : "s")} over the next {days.Count} day{(days.Count == 1 ? "" : "s")}. " +
                  "Click one for details.";
        }

        private static GameData? GameOf(object sender) =>
            ((sender as FrameworkElement)?.DataContext as AgendaRowVm)?.Game;

        private void Row_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (GameOf(sender) is { } g) OpenMatch?.Invoke(g.Id);
        }

        private void Ics_Click(object sender, RoutedEventArgs e)
        {
            if (GameOf(sender) is { } g) AddToCalendarFile(g);
        }

        private void Google_Click(object sender, RoutedEventArgs e)
        {
            if (GameOf(sender) is { } g) AddToGoogleCalendar(g);
        }

        private static string EventTitle(GameData g) => $"{g.HomeTeam} vs {g.AwayTeam}";

        // iCalendar text: escape backslash, separators and newlines.
        private static string IcsText(string s) =>
            s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n");

        private static string UtcStamp(DateTime local) =>
            local.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        /// <summary>Writes a one-event .ics and opens it in the default calendar app.</summary>
        private static void AddToCalendarFile(GameData g)
        {
            var start = g.KickOff!.Value;
            var ics = new StringBuilder()
                .AppendLine("BEGIN:VCALENDAR")
                .AppendLine("VERSION:2.0")
                .AppendLine("PRODID:-//SportsOverlay//Agenda//EN")
                .AppendLine("BEGIN:VEVENT")
                .AppendLine($"UID:{g.Id}@sportsoverlay")
                .AppendLine($"DTSTAMP:{UtcStamp(DateTime.Now)}")
                .AppendLine($"DTSTART:{UtcStamp(start)}")
                .AppendLine($"DTEND:{UtcStamp(start + EventLength)}")
                .AppendLine($"SUMMARY:{IcsText(EventTitle(g))}")
                .AppendLine($"DESCRIPTION:{IcsText(g.Competition)}")
                .AppendLine("END:VEVENT")
                .AppendLine("END:VCALENDAR")
                .ToString();
            try
            {
                var dir = Path.Combine(Path.GetTempPath(), "SportsOverlay");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"{g.Id}.ics");
                File.WriteAllText(path, ics);
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't open the calendar file: {ex.Message}", "Sports Overlay",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static void AddToGoogleCalendar(GameData g)
        {
            var start = g.KickOff!.Value;
            var url = "https://calendar.google.com/calendar/render?action=TEMPLATE"
                      + "&text=" + Uri.EscapeDataString(EventTitle(g))
                      + "&dates=" + UtcStamp(start) + "/" + UtcStamp(start + EventLength)
                      + "&details=" + Uri.EscapeDataString(g.Competition);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();
    }

    public class AgendaDayVm
    {
        public string Title { get; }
        public string Subtitle { get; }
        public List<AgendaRowVm> Games { get; }

        public AgendaDayVm(DateTime date, List<AgendaRowVm> games)
        {
            // The UI is in English, so dates are too (not the system's locale).
            var en = CultureInfo.GetCultureInfo("en-GB");
            var today = DateTime.Today;
            Title = date == today ? "Today" : date == today.AddDays(1) ? "Tomorrow" : date.ToString("dddd", en);
            Subtitle = date.ToString("d MMMM", en);
            Games = games;
        }
    }

    public class AgendaRowVm
    {
        private static readonly Brush FollowText = new SolidColorBrush(Color.FromRgb(0x6C, 0xCB, 0x5F));
        private static readonly Brush FollowFill = new SolidColorBrush(Color.FromArgb(0x2E, 0x6C, 0xCB, 0x5F));
        private static readonly Brush StarText = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F));
        private static readonly Brush StarFill = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xD5, 0x4F));
        private static readonly Brush NeutralFill = new SolidColorBrush(Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF));

        public GameData Game { get; }
        public string Time { get; }
        public string Home { get; }
        public string Away { get; }
        public string? HomeLogo { get; }
        public string? AwayLogo { get; }
        public string Competition { get; }
        public string SportIcon { get; }
        public string TagText { get; }
        public Brush TagBrush { get; }
        public Brush TagFill { get; }

        public AgendaRowVm(AgendaItem item, Brush accent)
        {
            var g = item.Game;
            Game = g;
            Time = g.KickOff!.Value.ToString("HH:mm");
            Home = g.HomeTeam;
            Away = g.AwayTeam;
            HomeLogo = Logo(g.HomeLogoUrl, g.HomeFlag);
            AwayLogo = Logo(g.AwayLogoUrl, g.AwayFlag);
            Competition = GameChipVm.PrettyCompetition(g.Competition);
            SportIcon = GameChipVm.IconFor(g.Sport);
            (TagText, TagBrush, TagFill) = item.Followed ? ("Following", FollowText, FollowFill)
                                         : item.Starred ? ("Starred", StarText, StarFill)
                                         : ("For you", accent, NeutralFill);
        }

        // A club crest when there is one, else the player's/team's flag.
        private static string? Logo(string logoUrl, string flag)
        {
            if (logoUrl != "") return logoUrl;
            var url = GameChipVm.FlagUrl(flag);
            return url == "" ? null : url;
        }
    }
}
