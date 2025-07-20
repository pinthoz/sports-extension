namespace SportsOverlayApp.Models
{
    public class UserPreferences
    {
        public string FavoriteTeam { get; set; } = "Manchester United";
        public bool UseDarkTheme { get; set; } = false;
        public double OverlayOpacity { get; set; } = 0.9;
        public bool EnableNotifications { get; set; } = true;
        public bool AutoHide { get; set; } = false;
        public string PreferredLeague { get; set; } = "Premier League";
        public int UpdateInterval { get; set; } = 5; // seconds
    }
}
