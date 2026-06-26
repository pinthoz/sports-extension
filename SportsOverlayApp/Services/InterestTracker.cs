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
        // "star" (starred on FlashScore — the passive default signal),
        // "like" (explicit ♥ in the popup) or "pin" (picked via the popup).
        public string Source { get; set; } = "like";
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Learns which games the user cares about — primarily from the games they
    /// star on FlashScore (recorded passively as they show up in the feed),
    /// plus explicit likes and manual picks — and recommends matching games
    /// once a few days of history exist.
    /// </summary>
    public class InterestTracker
    {
        // Recommendations stay off until the history spans a few days, so the
        // first day of likes doesn't immediately start reshuffling the bar.
        // A day only counts once it has at least MinRecordsPerDay records.
        private const int MinRecordsPerDay = 4;
        private const int MinDistinctDays = 3;

        private const double LikeWeight = 2.0, StarWeight = 1.0, PinWeight = 1.0;
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

        /// <summary>
        /// Records a game starred on FlashScore as a passive interest signal.
        /// The feed only ever contains starred games, so this is called for
        /// every scraped game; the once-per-game-per-day guard keeps the 2.5s
        /// scrape loop from flooding the history.
        /// </summary>
        public void RecordStar(string gameId, string sport, string competition, string home, string away)
        {
            if (records.Any(r => r.GameId == gameId && r.Source == "star"
                                 && r.Timestamp.Date == DateTime.Today))
                return;
            records.Add(new GameInterest
            {
                GameId = gameId,
                Sport = sport,
                Competition = competition,
                HomeTeam = home,
                AwayTeam = away,
                Source = "star"
            });
            Save();
        }

        public bool IsRecommended(string sport, string competition, string home, string away) =>
            HasEnoughData && Score(sport, competition, home, away) >= RecommendThreshold;

        /// <summary>
        /// Affinity score of a game against the learned profile: strong for a
        /// matching team, weaker (and capped) for a matching competition or
        /// sport. Used both to flag recommendations and to rank candidates.
        /// </summary>
        public double Score(string sport, string competition, string home, string away)
        {
            double teamScore = 0, compScore = 0, sportScore = 0;
            foreach (var r in records)
            {
                var w = r.Source switch
                {
                    "like" => LikeWeight,
                    "star" => StarWeight,
                    _ => PinWeight
                };
                if (Mentions(r, home) || Mentions(r, away))
                    teamScore += w;
                else if (r.Competition != "" && Same(r.Competition, competition))
                    compScore += w * 0.5;
                else if (Same(r.Sport, sport))
                    sportScore += w * 0.1;
            }
            return teamScore
                   + Math.Min(compScore, MaxCompetitionScore)
                   + Math.Min(sportScore, MaxSportScore);
        }

        public bool MeetsThreshold(double score) => score >= RecommendThreshold;

        /// <summary>
        /// Sports the user follows, most-recorded first. Empty until there is
        /// enough history, so discovery stays idle until the profile is usable.
        /// </summary>
        public IReadOnlyList<string> FollowedSports()
        {
            if (!HasEnoughData)
                return Array.Empty<string>();
            return records
                .Where(r => r.Sport != "")
                .GroupBy(r => r.Sport, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .ToList();
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
