namespace SportsOverlayApp.Models
{
    public enum BarPosition
    {
        Bottom,  // docked just above the taskbar
        Top,     // docked to the top edge of the screen
        Taskbar  // compact, floating over the taskbar's empty space
    }

    public enum DataSource
    {
        BuiltIn,  // embedded FlashScore browser inside the app (no tab needed)
        Extension // browser extension streaming over the local WebSocket
    }

    public class UserPreferences
    {
        public bool UseDarkTheme { get; set; } = true;
        public double OverlayOpacity { get; set; } = 0.95;
        public bool EnableNotifications { get; set; } = true;
        public BarPosition BarPosition { get; set; } = BarPosition.Taskbar;
        public int WebSocketPort { get; set; } = 8787;
        public bool StartWithWindows { get; set; } = false;
        // Horizontal position of the pills when floating over the taskbar (drag to move).
        // Left default clears the weather widget; right default clears the tray + clock.
        public double TaskbarOffsetX { get; set; } = 170;
        public double TaskbarOffsetRight { get; set; } = 280;
        public DataSource DataSource { get; set; } = DataSource.BuiltIn;
        // Page the embedded browser opens; the favourites page aggregates
        // starred games across all sports.
        public string FlashScoreUrl { get; set; } = "https://www.flashscore.com/favourites/";
        // When on (and using the built-in browser), a hidden discovery browser
        // scans the sports you follow for games to recommend without starring.
        public bool EnableRecommendations { get; set; } = true;
        // Max recommended (not-yet-starred) games to surface at once.
        public int MaxRecommendations { get; set; } = 6;
    }
}
