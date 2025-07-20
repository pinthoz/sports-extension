using System;

namespace SportsOverlayApp.Models
{
    public class GameData
    {
        public string Title { get; set; } = "";
        public string Competition { get; set; } = "";
        public string Status { get; set; } = "";
        public string Score { get; set; } = "0-0";
        public string Time { get; set; } = "";
        public string HomeTeam { get; set; } = "";
        public string AwayTeam { get; set; } = "";
        public bool IsLive { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }
}