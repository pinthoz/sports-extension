using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SportsOverlayApp.Models;
using SportsOverlayApp.Services;
using SportsOverlayApp.Utils;

namespace SportsOverlayApp.Views
{
    public partial class MainWindow : Window
    {
        // A side of the taskbar holds 2 slots: a football chip takes 1 slot
        // (2 per side), a set-based chip like tennis takes 2 (1 per side).
        private const int SlotsPerSide = 2;

        private readonly ObservableCollection<GameChipVm> allGames = new();
        private readonly ObservableCollection<GameChipVm> leftGames = new();
        private readonly ObservableCollection<GameChipVm> rightGames = new();
        private readonly HashSet<string> dismissed = new();
        private readonly List<string> manualPicks = new();   // user-pinned, highest priority
        private readonly HashSet<string> manualHidden = new(); // user-unchecked, never auto-shown
        private UserPreferences preferences = new();
        private DispatcherTimer? topmostTimer;

        public MainWindow()
        {
            InitializeComponent();
            LeftList.ItemsSource = leftGames;
            RightList.ItemsSource = rightGames;
            PickerList.ItemsSource = allGames;
            SystemParameters.StaticPropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SystemParameters.WorkArea))
                    Dispatcher.Invoke(Reposition);
            };
            // The taskbar is itself a topmost window and will paint over us after
            // certain shell events; periodically re-assert our z-order so the
            // pills stay visible when floating over the taskbar.
            SourceInitialized += (s, e) =>
            {
                topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                topmostTimer.Tick += (s2, e2) =>
                {
                    if (IsVisible && preferences.BarPosition == BarPosition.Taskbar)
                        ReassertTopmost();
                };
                topmostTimer.Start();
            };
        }

        public void ApplyUserPreferences(UserPreferences prefs)
        {
            preferences = prefs;
            Opacity = prefs.OverlayOpacity;
            var pillBrush = prefs.UseDarkTheme
                ? new SolidColorBrush(Color.FromArgb(0xD9, 0x18, 0x18, 0x20))
                : new SolidColorBrush(Color.FromArgb(0xD9, 0xF0, 0xF2, 0xF8));
            foreach (var pill in new[] { LeftPill, RightPill, ControlPill, PopupPill })
                pill.Background = pillBrush;
            ConnectionLabel.Foreground = new SolidColorBrush(prefs.UseDarkTheme
                ? Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0xAA, 0x20, 0x20, 0x28));
            Reposition();
        }

        private bool InTaskbarMode => preferences.BarPosition == BarPosition.Taskbar
                                      && TryGetTaskbarBounds(out _, out _);

        /// <summary>
        /// Positions the window: full-width docked to an edge of the work area, or
        /// spanning the taskbar with a pill anchored to each side (Taskbar mode).
        /// The window background is transparent, so clicks between the pills fall
        /// through to the taskbar.
        /// </summary>
        public void Reposition()
        {
            var area = SystemParameters.WorkArea;

            if (preferences.BarPosition == BarPosition.Taskbar && TryGetTaskbarBounds(out var tbTop, out var tbHeight))
            {
                ConnectionLabel.Visibility = Visibility.Collapsed; // the dot is enough
                Height = Math.Max(28, tbHeight - 8);
                Top = tbTop + (tbHeight - Height) / 2;
                Left = 0;
                Width = SystemParameters.PrimaryScreenWidth;
                LeftGroup.Margin = new Thickness(preferences.TaskbarOffsetX, 0, 0, 0);
                RightGroup.Margin = new Thickness(0, 0, preferences.TaskbarOffsetRight, 0);
                ReassertTopmost();
            }
            else
            {
                ConnectionLabel.Visibility = Visibility.Visible;
                Height = 40;
                Left = area.Left;
                Width = area.Width;
                LeftGroup.Margin = new Thickness(8, 0, 0, 0);
                RightGroup.Margin = new Thickness(0, 0, 8, 0);
                Top = preferences.BarPosition == BarPosition.Top
                    ? area.Top
                    : area.Bottom - Height;
            }
            RefreshAssignments();
        }

        /// <summary>Finds the taskbar strip on the primary screen (top or bottom edge).</summary>
        private static bool TryGetTaskbarBounds(out double top, out double height)
        {
            var area = SystemParameters.WorkArea;
            var screenHeight = SystemParameters.PrimaryScreenHeight;

            if (area.Bottom < screenHeight) // taskbar at the bottom (default)
            {
                top = area.Bottom;
                height = screenHeight - area.Bottom;
                return true;
            }
            if (area.Top > 0) // taskbar at the top
            {
                top = 0;
                height = area.Top;
                return true;
            }
            // Vertical or auto-hidden taskbar: no horizontal strip to float over.
            top = height = 0;
            return false;
        }

        // Dragging the pills along the taskbar

        private string? draggingSide;
        private Point dragStart;
        private double dragStartOffset;

        private void Pill_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!InTaskbarMode || sender is not Border pill) return;
            draggingSide = pill.Tag as string;
            dragStart = e.GetPosition(this);
            dragStartOffset = draggingSide == "left"
                ? preferences.TaskbarOffsetX
                : preferences.TaskbarOffsetRight;
            pill.CaptureMouse();
        }

        private void Pill_MouseMove(object sender, MouseEventArgs e)
        {
            if (draggingSide == null || e.LeftButton != MouseButtonState.Pressed) return;
            var dx = e.GetPosition(this).X - dragStart.X;
            if (draggingSide == "left")
            {
                var offset = Math.Max(0, Math.Min(dragStartOffset + dx, Width - LeftGroup.ActualWidth));
                preferences.TaskbarOffsetX = offset;
                LeftGroup.Margin = new Thickness(offset, 0, 0, 0);
            }
            else
            {
                var offset = Math.Max(0, Math.Min(dragStartOffset - dx, Width - RightGroup.ActualWidth));
                preferences.TaskbarOffsetRight = offset;
                RightGroup.Margin = new Thickness(0, 0, offset, 0);
            }
        }

        private void Pill_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (draggingSide == null) return;
            (sender as Border)?.ReleaseMouseCapture();
            draggingSide = null;
            CacheService.SavePreferences(preferences);
        }

        // Topmost re-assert

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint flags);

        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010;

        private void ReassertTopmost()
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
                SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        public void SetConnected(bool connected)
        {
            ConnectionDot.Fill = new SolidColorBrush(connected
                ? Color.FromRgb(0x4C, 0xAF, 0x50)
                : Color.FromRgb(0xE5, 0x39, 0x35));
            ConnectionLabel.Text = connected ? "live" : "offline";
        }

        // Game data

        public void UpdateGameData(List<GameData> data)
        {
            var incomingIds = new HashSet<string>(data.Select(g => g.Id));

            // Forget dismissals/picks for games that left the feed entirely.
            dismissed.RemoveWhere(id => !incomingIds.Contains(id));
            manualHidden.RemoveWhere(id => !incomingIds.Contains(id));
            manualPicks.RemoveAll(id => !incomingIds.Contains(id));

            for (int i = allGames.Count - 1; i >= 0; i--)
            {
                if (!incomingIds.Contains(allGames[i].Id))
                    allGames.RemoveAt(i);
            }

            foreach (var game in data)
            {
                if (dismissed.Contains(game.Id))
                    continue;

                var chip = allGames.FirstOrDefault(c => c.Id == game.Id);
                if (chip == null)
                {
                    allGames.Add(GameChipVm.From(game));
                    continue;
                }

                bool scored = chip.Score != game.Score && game.Score.Any(char.IsDigit);
                chip.Update(game);
                if (scored)
                {
                    chip.FlashGoal(Dispatcher);
                    if (preferences.EnableNotifications)
                        Notifications.PlayGoalSound();
                }
            }

            RefreshAssignments();
        }

        /// <summary>
        /// Decides which games are visible and on which side. User picks come
        /// first, then live games, then upcoming, then finished. It packs items
        /// into the left side, then the right.
        /// </summary>
        private void RefreshAssignments()
        {
            var ordered = manualPicks
                .Select(id => allGames.FirstOrDefault(g => g.Id == id))
                .Where(g => g != null)
                .Cast<GameChipVm>()
                .Concat(allGames
                    .Where(g => !manualPicks.Contains(g.Id) && !manualHidden.Contains(g.Id))
                    .OrderBy(g => g.IsFinished ? 2 : g.IsLive ? 0 : 1))
                .ToList();

            bool split = InTaskbarMode;
            int leftFree = split ? SlotsPerSide : int.MaxValue;
            int rightFree = split ? SlotsPerSide : 0;

            var left = new List<GameChipVm>();
            var right = new List<GameChipVm>();
            foreach (var game in ordered)
            {
                if (game.Slots <= leftFree) { left.Add(game); leftFree -= game.Slots; }
                else if (game.Slots <= rightFree) { right.Add(game); rightFree -= game.Slots; }
            }

            Sync(leftGames, left);
            Sync(rightGames, right);

            foreach (var game in allGames)
                game.IsShown = left.Contains(game) || right.Contains(game);

            LeftPill.Visibility = left.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            RightPill.Visibility = right.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            OverflowToggle.Visibility = allGames.Count > left.Count + right.Count
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private static void Sync(ObservableCollection<GameChipVm> target, List<GameChipVm> desired)
        {
            if (target.SequenceEqual(desired)) return;
            target.Clear();
            foreach (var item in desired)
                target.Add(item);
        }

        private void Dismiss_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not GameChipVm chip) return;
            dismissed.Add(chip.Id);
            manualPicks.Remove(chip.Id);
            allGames.Remove(chip);
            RefreshAssignments();
            e.Handled = true;
        }

        private void PickerItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox box || box.DataContext is not GameChipVm chip) return;
            if (box.IsChecked == true)
            {
                manualHidden.Remove(chip.Id);
                manualPicks.Remove(chip.Id);
                manualPicks.Insert(0, chip.Id); // newest pick wins the space fight
            }
            else
            {
                manualPicks.Remove(chip.Id);
                manualHidden.Add(chip.Id);
            }
            RefreshAssignments();
        }
    }

    public class GameChipVm : INotifyPropertyChanged
    {
        private static readonly Dictionary<string, string> SportIcons = new()
        {
            ["football"] = "⚽",
            ["futsal"] = "⚽",
            ["tennis"] = "\U0001F3BE",
            ["basketball"] = "\U0001F3C0",
            ["hockey"] = "\U0001F3D2",
            ["american-football"] = "\U0001F3C8",
            ["baseball"] = "⚾",
            ["handball"] = "\U0001F93E",
            ["rugby"] = "\U0001F3C9",
            ["rugby-league"] = "\U0001F3C9",
            ["volleyball"] = "\U0001F3D0",
            ["beach-volleyball"] = "\U0001F3D0",
            ["cricket"] = "\U0001F3CF",
            ["darts"] = "\U0001F3AF",
            ["snooker"] = "\U0001F3B1",
            ["boxing"] = "\U0001F94A",
            ["mma"] = "\U0001F94A",
            ["table-tennis"] = "\U0001F3D3",
            ["badminton"] = "\U0001F3F8",
            ["esports"] = "\U0001F3AE",
            ["motorsport"] = "\U0001F3CE"
        };

        // Sports whose chip stays narrow (no per-set detail shown).
        private static readonly HashSet<string> NarrowSports = new() { "football", "futsal" };

        public string Id { get; private set; } = "";
        public string Sport { get; private set; } = "football";

        /// <summary>Taskbar space the chip takes: narrow sports 1, set-based sports 2.</summary>
        public int Slots => NarrowSports.Contains(Sport) ? 1 : 2;

        private string sportIcon = "", homeTeam = "", awayTeam = "", score = "", time = "";
        private string partsDisplay = "", pointsDisplay = "", summary = "";
        private bool isLive, isFinished, justScored, isShown, servingHome, servingAway;

        public string SportIcon { get => sportIcon; set => Set(ref sportIcon, value, nameof(SportIcon)); }
        public string HomeTeam { get => homeTeam; set => Set(ref homeTeam, value, nameof(HomeTeam)); }
        public string AwayTeam { get => awayTeam; set => Set(ref awayTeam, value, nameof(AwayTeam)); }
        public string Score { get => score; set => Set(ref score, value, nameof(Score)); }
        public string Time { get => time; set => Set(ref time, value, nameof(Time)); }
        public string PartsDisplay { get => partsDisplay; set => Set(ref partsDisplay, value, nameof(PartsDisplay)); }
        public string PointsDisplay { get => pointsDisplay; set => Set(ref pointsDisplay, value, nameof(PointsDisplay)); }
        public string Summary { get => summary; set => Set(ref summary, value, nameof(Summary)); }
        public bool IsLive { get => isLive; set => Set(ref isLive, value, nameof(IsLive)); }
        public bool IsFinished { get => isFinished; set => Set(ref isFinished, value, nameof(IsFinished)); }
        public bool JustScored { get => justScored; set => Set(ref justScored, value, nameof(JustScored)); }
        public bool IsShown { get => isShown; set => Set(ref isShown, value, nameof(IsShown)); }
        public bool ServingHome { get => servingHome; set => Set(ref servingHome, value, nameof(ServingHome)); }
        public bool ServingAway { get => servingAway; set => Set(ref servingAway, value, nameof(ServingAway)); }

        public static GameChipVm From(GameData g)
        {
            var vm = new GameChipVm { Id = g.Id };
            vm.Update(g);
            return vm;
        }

        public void Update(GameData g)
        {
            Sport = g.Sport;
            SportIcon = SportIcons.TryGetValue(g.Sport, out var icon) ? icon : "\U0001F3C5";
            HomeTeam = Abbreviate(g.HomeTeam);
            AwayTeam = Abbreviate(g.AwayTeam);
            Score = g.Score;
            Time = AbbrevStage(g.Time);
            IsLive = g.IsLive;
            IsFinished = g.IsFinished;
            ServingHome = g.Serving == "home";
            ServingAway = g.Serving == "away";
            PartsDisplay = FormatParts(g);
            PointsDisplay = g.HomePoints != "" || g.AwayPoints != ""
                ? $"{g.HomePoints}-{g.AwayPoints}"
                : "";
            Summary = $"{SportIcon} {g.HomeTeam} {Score} {g.AwayTeam}  {Time}";
        }

        // Shortens long club names so four football chips fit on the taskbar:
        // leading words collapse to initials until the name fits.
        private static string Abbreviate(string name, int max = 11)
        {
            if (name.Length <= max) return name;
            var words = name.Split(' ');
            for (int i = 0; i < words.Length - 1; i++)
            {
                if (words[i].Length > 2 && char.IsLetter(words[i][0]))
                    words[i] = words[i][0] + ".";
                var candidate = string.Join(" ", words);
                if (candidate.Length <= max) return candidate;
            }
            var s = string.Join(" ", words);
            return s.Length <= max ? s : s.Substring(0, max - 3).TrimEnd() + "...";
        }

        private static string AbbrevStage(string stage)
        {
            var s = stage.Trim();
            var lower = s.ToLowerInvariant();
            if (lower.Contains("half") || lower.StartsWith("intervalo")) return "HT";
            if (lower.StartsWith("finished") || lower.StartsWith("terminado")
                || lower.StartsWith("encerrado") || lower.StartsWith("after")) return "FT";
            if (lower.StartsWith("set ")) return "S" + s.Substring(4);
            return s;
        }

        // Football halves would just clutter the chip; for set-based sports
        // (tennis, volleyball, ...) the per-set games are the whole story.
        private static string FormatParts(GameData g)
        {
            if (NarrowSports.Contains(g.Sport))
                return "";
            bool tennis = g.Sport == "tennis";
            var pairs = g.HomeParts
                .Zip(g.AwayParts, (h, a) =>
                    $"{(tennis ? TennisPart(h) : h)}-{(tennis ? TennisPart(a) : a)}")
                .ToList();
            return pairs.Count > 0 ? $"({string.Join(" ", pairs)})" : "";
        }

        // Fallback for tiebreak scores that arrive concatenated ("79" = 7 games,
        // 9 tiebreak points): render the tiebreak as superscript, e.g. 7⁹.
        // Tiebreaks only exist on 6/7-game sets or 10+ super tiebreaks, so any
        // other value (e.g. game points like "30") is left untouched.
        private static string TennisPart(string v)
        {
            if (v.Length < 2 || !v.All(char.IsDigit)) return v;
            if (v[0] == '6' || v[0] == '7')
                return v[0] + ToSuperscript(v.Substring(1));
            if (v[0] == '1' && v.Length >= 3)
                return v.Substring(0, 2) + ToSuperscript(v.Substring(2));
            return v;
        }

        private static string ToSuperscript(string digits)
        {
            const string sup = "⁰¹²³⁴⁵⁶⁷⁸⁹";
            return new string(digits.Select(c => char.IsDigit(c) ? sup[c - '0'] : c).ToArray());
        }

        public void FlashGoal(Dispatcher dispatcher)
        {
            JustScored = true;
            var timer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background,
                (s, e) =>
                {
                    JustScored = false;
                    ((DispatcherTimer)s!).Stop();
                }, dispatcher);
            timer.Start();
        }

        private void Set<T>(ref T field, T value, string name)
        {
            if (!Equals(field, value))
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
