using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SportsOverlayApp.Services;
using SportsOverlayApp.Utils;

namespace SportsOverlayApp.Views
{
    /// <summary>
    /// Shows what the recommendation model has learned: each team/player and
    /// competition with the weight it currently carries, plus the dislikes and
    /// the explicitly followed teams. Any line can be forgotten or followed,
    /// which edits the history directly.
    /// </summary>
    public partial class InterestsWindow : Window
    {
        private readonly InterestTracker interests;

        public InterestsWindow(InterestTracker interests)
        {
            InitializeComponent();
            FluentWindow.Apply(this);
            this.interests = interests;
            Refresh();
        }

        private void Refresh()
        {
            var profile = interests.Profile();
            var threshold = interests.Threshold;
            // Bars share one scale so teams and competitions compare directly.
            var max = Math.Max(threshold, profile.Select(e => e.Weight).DefaultIfEmpty(0).Max());
            var accent = (Brush)FindResource("AccentBrush");
            var neutral = (Brush)FindResource("TextTertiary");

            var follows = interests.Follows()
                .Select(f => new FollowVm(f.name, f.sport))
                .ToList();
            FollowList.ItemsSource = follows;
            NoFollows.Visibility = follows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var teams = profile.Where(e => e.Kind == "team")
                .Select(e => new ProfileRowVm(e, threshold, max, interests.IsFollowed(e.Name), accent, neutral))
                .ToList();
            TeamList.ItemsSource = teams;
            NoTeams.Visibility = teams.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var competitions = profile.Where(e => e.Kind == "competition")
                .Select(e => new ProfileRowVm(e, threshold, max, false, accent, neutral))
                .ToList();
            CompetitionList.ItemsSource = competitions;
            NoCompetitions.Visibility = competitions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var dislikes = profile.Where(e => e.Kind == "dislike")
                .Select(e => new ProfileRowVm(e, threshold, max, false, accent, neutral))
                .ToList();
            DislikeList.ItemsSource = dislikes;
            NoDislikes.Visibility = dislikes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var strong = profile.Count(e => e.Kind == "team" && e.Weight >= threshold);
            if (interests.HasEnoughData)
            {
                StatusIcon.Text = ""; // CheckMark
                StatusTitle.Text = $"Recommendations are on · {strong} strong enough on their own";
                StatusText.Text = "Teams with a full, coloured bar get recommended by themselves; the rest add up with " +
                                  "competition and sport. Interest halves every 30 days, counted from your latest activity.";
            }
            else
            {
                StatusIcon.Text = ""; // Info
                StatusTitle.Text = "Still learning";
                StatusText.Text = "Recommendations start after a few days of starred games. Teams you follow are shown right away.";
            }
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

        private void Follow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not ProfileRowVm row) return;
            if (interests.IsFollowed(row.Entry.Name))
                interests.Unfollow(row.Entry.Name);
            else
                interests.Follow(row.Entry.Name, row.Entry.Sport);
            Refresh();
        }

        private void Unfollow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not FollowVm follow) return;
            interests.Unfollow(follow.Name);
            Refresh();
        }

        private void FollowAdd_Click(object sender, RoutedEventArgs e) => FollowTyped();

        private void FollowInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) FollowTyped();
        }

        private void FollowTyped()
        {
            var name = FollowInput.Text.Trim();
            if (name == "") return;
            var sport = SportPicker.Children.OfType<RadioButton>()
                .FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? "football";
            interests.Follow(name, sport);
            FollowInput.Clear();
            Refresh();
        }
    }

    public class FollowVm
    {
        public string Name { get; }
        public string SportIcon { get; }

        public FollowVm(string name, string sport)
        {
            Name = name;
            SportIcon = GameChipVm.IconFor(sport);
        }
    }

    public class ProfileRowVm
    {
        public ProfileEntry Entry { get; }
        public string Name { get; }
        public string SportIcon { get; }
        public double BarValue { get; }
        public Brush BarBrush { get; }
        public string BarTip { get; }
        public string WeightText { get; }
        public Brush FollowBrush { get; }
        private static readonly Brush Unfollowed = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
        public string FollowTip { get; }
        public Visibility FollowVisibility { get; }

        public ProfileRowVm(ProfileEntry e, double threshold, double max, bool followed, Brush accent, Brush neutral)
        {
            Entry = e;
            Name = e.Kind == "competition" ? GameChipVm.PrettyCompetition(e.Name) : e.Name;
            SportIcon = GameChipVm.IconFor(e.Sport);
            bool strong = e.Kind == "team" && e.Weight >= threshold;
            BarValue = Math.Min(1, e.Weight / max);
            BarBrush = strong ? accent : neutral;
            BarTip = strong ? "Recommended on its own" : "Adds to recommendations";
            WeightText = e.Weight.ToString("0.0");
            // Following is offered on learned teams/players only.
            FollowVisibility = e.Kind == "team" ? Visibility.Visible : Visibility.Collapsed;
            FollowBrush = followed ? accent : Unfollowed;
            FollowTip = followed ? "Following: click to unfollow" : "Follow: always show this team's games";
        }
    }
}
