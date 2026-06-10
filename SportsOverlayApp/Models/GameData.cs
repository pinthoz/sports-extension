using System;
using System.Collections.Generic;

namespace SportsOverlayApp.Models
{
    public class GameData
    {
        public string Id { get; set; } = "";
        public string Sport { get; set; } = "football";
        public string Title { get; set; } = "";
        public string Competition { get; set; } = "";
        public string Status { get; set; } = "";
        public string Score { get; set; } = "0-0";
        public string Time { get; set; } = "";
        public string HomeTeam { get; set; } = "";
        public string AwayTeam { get; set; } = "";
        // Per-period scores: halves in football, sets in tennis/volleyball, etc.
        public List<string> HomeParts { get; set; } = new();
        public List<string> AwayParts { get; set; } = new();
        // Current game points in a live tennis match (15/30/40).
        public string HomePoints { get; set; } = "";
        public string AwayPoints { get; set; } = "";
        // Which side is serving in a live tennis match: "home", "away" or "".
        public string Serving { get; set; } = "";
        public bool IsLive { get; set; }
        public bool IsFinished { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }
}
