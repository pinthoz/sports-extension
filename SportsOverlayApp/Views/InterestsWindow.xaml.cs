using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using SportsOverlayApp.Services;

namespace SportsOverlayApp.Views
{
    /// <summary>
    /// Shows what the recommendation model has learned: each team/player and
    /// competition with the weight it currently carries, plus the dislikes.
    /// Any line can be forgotten, which edits the history directly.
    /// </summary>
    public partial class InterestsWindow : Window
    {
        private readonly InterestTracker interests;

        public InterestsWindow(InterestTracker interests)
        {
            InitializeComponent();
            this.interests = interests;
            Refresh();
        }

        private void Refresh()
        {
            var profile = interests.Profile();
            var threshold = interests.Threshold;
            // Bars share one scale so teams and competitions compare directly.
            var max = Math.Max(threshold, profile.Select(e => e.Weight).DefaultIfEmpty(0).Max());

            TeamList.ItemsSource = profile.Where(e => e.Kind == "team")
                .Select(e => new ProfileRowVm(e, threshold, max)).ToList();
            CompetitionList.ItemsSource = profile.Where(e => e.Kind == "competition")
                .Select(e => new ProfileRowVm(e, threshold, max)).ToList();
            DislikeList.ItemsSource = profile.Where(e => e.Kind == "dislike")
                .Select(e => new ProfileRowVm(e, threshold, max)).ToList();

            var recommended = profile.Count(e => e.Kind == "team" && e.Weight >= threshold);
            StatusText.Text = interests.HasEnoughData
                ? $"Recommendations are on. {recommended} team(s)/player(s) are strong enough " +
                  $"(weight ≥ {threshold:0.#}, highlighted) to be recommended on their own; " +
                  "the rest add up with competition and sport. Signals halve in weight every 30 days, counted from your latest activity."
                : "Not enough history yet: recommendations start after a few days of starred games.";
        }

        private void Forget_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not ProfileRowVm row) return;
            var entry = row.Entry;
            if (entry.Kind != "dislike")
            {
                var answer = MessageBox.Show(
                    $"Forget everything learned about \"{entry.Name}\"? This removes its stars, likes and picks from the history.",
                    "Forget", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                if (answer != MessageBoxResult.OK) return;
            }
            interests.Forget(entry);
            Refresh();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }

    public class ProfileRowVm
    {
        private const double BarMax = 90;
        private static readonly Brush Strong = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        private static readonly Brush Weak = new SolidColorBrush(Color.FromRgb(0x5C, 0x8D, 0xD6));

        public ProfileEntry Entry { get; }
        public string Label { get; }
        public string WeightText { get; }
        public double BarWidth { get; }
        public Brush BarBrush { get; }
        public Brush Foreground { get; }
        public Visibility BarVisibility { get; }
        public string ActionText { get; }
        public string ActionTip { get; }

        public ProfileRowVm(ProfileEntry e, double threshold, double max)
        {
            Entry = e;
            var icon = e.Sport switch { "football" => "⚽ ", "tennis" => "\U0001F3BE ", _ => "" };
            Label = icon + e.Name;
            bool dislike = e.Kind == "dislike";
            bool strong = e.Kind == "team" && e.Weight >= threshold;
            WeightText = dislike ? "" : e.Weight.ToString("0.00");
            BarWidth = dislike ? 0 : BarMax * Math.Min(1, e.Weight / max);
            BarBrush = strong ? Strong : Weak;
            Foreground = strong ? Strong : Brushes.White;
            BarVisibility = dislike ? Visibility.Collapsed : Visibility.Visible;
            ActionText = dislike ? "Undo" : "Forget";
            ActionTip = dislike
                ? "Allow this matchup to be recommended again"
                : "Remove what was learned about this";
        }
    }
}
