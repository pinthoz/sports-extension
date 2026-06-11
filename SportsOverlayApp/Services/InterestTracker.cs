using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace SportsOverlayApp.Services
{
    public class GameInterest
    {
        public string GameId { get; set; } = "";
        public string Sport { get; set; } = "";
        public string Competition { get; set; } = "";
        public string HomeTeam { get; set; } = "";
        public string AwayTeam { get; set; } = "";
        public string Source { get; set; } = "like"; // "like" (explicit ♥) or "pin" (picked via the popup)
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Learns which games the user cares about from explicit likes and manual
    /// picks, and recommends matching games once a few days of history exist.
    /// </summary>
    public class InterestTracker
    {
        // Recommendations stay off until the history spans a few days, so the
        // first day of likes doesn't immediately start reshuffling the bar.
        // A day only counts once it has at least MinRecordsPerDay records.
        private const int MinRecordsPerDay = 5;
        private const int MinDistinctDays = 3;

        private const double LikeWeight = 2.0, PinWeight = 1.0;
        // A liked team appearing again is enough on its own; competition and
        // sport affinity are capped so e.g. ten football likes don't end up
        // recommending every football game.
        private const double MaxCompetitionScore = 1.5, MaxSportScore = 0.5;
        private const double RecommendThreshold = 2.0;

        private static readonly string filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SportsOverlay", "interests.json");

        private readonly List<GameInterest> records = Load();

        public bool HasEnoughData =>
            records.GroupBy(r => r.Timestamp.Date).Count(d => d.Count() >= MinRecordsPerDay)
                >= MinDistinctDays;

        public bool IsLiked(string gameId) =>
            records.Any(r => r.GameId == gameId && r.Source == "like");

        /// <summary>Likes or unlikes a game; returns the new liked state.</summary>
        public bool ToggleLike(string gameId, string sport, string competition, string home, string away)
        {
            bool liked;
            if (IsLiked(gameId))
            {
                records.RemoveAll(r => r.GameId == gameId && r.Source == "like");
                liked = false;
            }
            else
            {
                records.Add(new GameInterest
                {
                    GameId = gameId,
                    Sport = sport,
                    Competition = competition,
                    HomeTeam = home,
                    AwayTeam = away,
                    Source = "like"
                });
                liked = true;
            }
            Save();
            return liked;
        }

        /// <summary>Counts a manual pick as a (weaker) interest signal, once per game per day.</summary>
        public void RecordPin(string gameId, string sport, string competition, string home, string away)
        {
            if (records.Any(r => r.GameId == gameId && r.Source == "pin"
                                 && r.Timestamp.Date == DateTime.Today))
                return;
            records.Add(new GameInterest
            {
                GameId = gameId,
                Sport = sport,
                Competition = competition,
                HomeTeam = home,
                AwayTeam = away,
                Source = "pin"
            });
            Save();
        }

        public bool IsRecommended(string sport, string competition, string home, string away)
        {
            if (!HasEnoughData)
                return false;

            double teamScore = 0, compScore = 0, sportScore = 0;
            foreach (var r in records)
            {
                var w = r.Source == "like" ? LikeWeight : PinWeight;
                if (Mentions(r, home) || Mentions(r, away))
                    teamScore += w;
                else if (r.Competition != "" && Same(r.Competition, competition))
                    compScore += w * 0.5;
                else if (Same(r.Sport, sport))
                    sportScore += w * 0.1;
            }
            var score = teamScore
                        + Math.Min(compScore, MaxCompetitionScore)
                        + Math.Min(sportScore, MaxSportScore);
            return score >= RecommendThreshold;
        }

        private static bool Mentions(GameInterest r, string team) =>
            team != "" && (Same(r.HomeTeam, team) || Same(r.AwayTeam, team));

        private static bool Same(string a, string b) =>
            string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        private static List<GameInterest> Load()
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    return JsonConvert.DeserializeObject<List<GameInterest>>(json) ?? new List<GameInterest>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Interest Load Error: {ex.Message}");
            }
            return new List<GameInterest>();
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                File.WriteAllText(filePath, JsonConvert.SerializeObject(records, Formatting.Indented));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Interest Save Error: {ex.Message}");
            }
        }
    }
}
