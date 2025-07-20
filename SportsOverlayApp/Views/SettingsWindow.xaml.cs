using System.Collections.Generic;
using System.Windows;
using SportsOverlayApp.Models;
using SportsOverlayApp.Utils;

namespace SportsOverlayApp.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
        }

        public void ApplyUserPreferences(UserPreferences preferences)
        {
            ThemeManager.ApplyLiquidGlassTheme(this, preferences);
            this.Opacity = preferences.OverlayOpacity;
        }

        public void UpdateGameData(List<GameData> gameData)
        {
            if (gameData.Count == 0)
            {
                MatchTitle.Text = "No matches";
                Score.Text = "-";
                Status.Text = "";
                return;
            }

            var match = gameData[0];
            MatchTitle.Text = $"{match.HomeTeam} vs {match.AwayTeam}";
            Score.Text = match.Score;
            Status.Text = match.Status + " " + match.Time;

            if (match.IsLive && match.Score != "0-0")
            {
                Notifications.PlayGoalSound();
            }
        }
    }
}
