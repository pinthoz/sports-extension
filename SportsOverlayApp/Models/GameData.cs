using System;
using System.Collections.Generic;

namespace SportsOverlayApp.Models
{
    /// <summary>One row of a ranking event (F1 practice, MotoGP race, ...).</summary>
    public class RankingEntry
    {
        public string Rank { get; set; } = "";
        public string Name { get; set; } = "";
        public string Team { get; set; } = "";
        public string Time { get; set; } = "";
        public string Laps { get; set; } = "";
        // Nationality flag, as a lowercase ISO 3166-1 alpha-2 code (e.g. "gb").
        public string Flag { get; set; } = "";
    }

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
        // Nationality flag for each side, as a lowercase ISO2 code (tennis players, F1 drivers, ...).
        public string HomeFlag { get; set; } = "";
        public string AwayFlag { get; set; } = "";
        // Team/participant crest image URL, when FlashScore renders one.
        public string HomeLogoUrl { get; set; } = "";
        public string AwayLogoUrl { get; set; } = "";
        // Per-period scores: halves in football, sets in tennis/volleyball, etc.
        public List<string> HomeParts { get; set; } = new();
        public List<string> AwayParts { get; set; } = new();
        // Current game points in a live tennis match (15/30/40).
        public string HomePoints { get; set; } = "";
        public string AwayPoints { get; set; } = "";
        // Which side is serving in a live tennis match: "home", "away" or "".
        public string Serving { get; set; } = "";
        // Classification of a ranking event (motorsport); empty for duel sports.
        public List<RankingEntry> Ranking { get; set; } = new();
        public bool IsLive { get; set; }
        public bool IsFinished { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }
}
