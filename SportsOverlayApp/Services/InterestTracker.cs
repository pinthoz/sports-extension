using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
        // "like" (explicit ♥ in the popup), "pin" (picked via the popup), or
        // "dislike" (the ✕ on a recommendation — a negative signal).
        public string Source { get; set; } = "like";
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>One line of the learned profile, as shown in the Interests window.</summary>
    public class ProfileEntry
    {
        public string Kind { get; set; } = "";   // "team", "competition" or "dislike"
        public string Name { get; set; } = "";
        public string Sport { get; set; } = "";
        public double Weight { get; set; }
        public string GameId { get; set; } = ""; // dislikes only
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
        // Interest fades: a signal loses half its weight every HalfLifeDays, so
        // recommendations follow what you star lately rather than months ago.
        // Active favourites re-record daily (see RecordStar), so they stay fresh;
        // only abandoned interests decay away.
        private const double HalfLifeDays = 30.0;
        // Well below the threshold, so a disliked game is never recommended and
        // always sorts last when ranking candidates.
        private const double DislikeScore = -1000.0;

        private static readonly string filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SportsOverlay", "interests.json");

        private readonly List<GameInterest> records = Load();

        // Dislikes are negative signals, so they don't count as "interest" for
        // the gate or for deciding which sports to scan.
        public bool HasEnoughData =>
            records.Where(r => r.Source != "dislike")
                   .GroupBy(r => r.Timestamp.Date).Count(d => d.Count() >= MinRecordsPerDay)
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
                records.Add(Create(gameId, sport, competition, home, away, "like"));
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
            records.Add(Create(gameId, sport, competition, home, away, "pin"));
            Save();
        }

        /// <summary>
        /// Records that the user is not interested in a recommended game (the ✕
        /// on a recommendation). Persisted, so the game — and any game featuring
        /// the same team/player — is kept out of future recommendations.
        /// </summary>
        public void RecordDislike(string gameId, string sport, string competition, string home, string away)
        {
            if (records.Any(r => r.GameId == gameId && r.Source == "dislike"))
                return;
            records.Add(Create(gameId, sport, competition, home, away, "dislike"));
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
            records.Add(Create(gameId, sport, competition, home, away, "star"));
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
            competition = CleanCompetition(competition);
            home = CleanTeam(home);
            away = CleanTeam(away);

            // A dislike hard-excludes that exact matchup. It stays pairing-
            // specific (not per-player) so disliking a doubles pair never
            // suppresses a player you like elsewhere.
            foreach (var r in records)
                if (r.Source == "dislike" && (Mentions(r, home) || Mentions(r, away)))
                    return DislikeScore;

            double teamScore = 0, compScore = 0, sportScore = 0;
            foreach (var (r, w) in WeightedSignals())
            {
                if (SharesParticipant(r, home, away))
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

        public double Threshold => RecommendThreshold;

        /// <summary>
        /// Positive signals with their current weight. Stars re-record daily
        /// while a game sits in Favourites (keeping it fresh), so each game
        /// counts once per source at its newest record — otherwise a game
        /// starred three days ahead would weigh three times as much.
        /// </summary>
        private IEnumerable<(GameInterest record, double weight)> WeightedSignals()
        {
            var latest = LatestSignal();
            return records
                .Where(r => r.Source != "dislike")
                .GroupBy(r => (r.GameId, r.Source))
                .Select(g => g.OrderByDescending(r => r.Timestamp).First())
                .Select(r => (r, (r.Source switch
                {
                    "like" => LikeWeight,
                    "star" => StarWeight,
                    _ => PinWeight
                }) * Recency(r, latest))) // older signals count for less
                .ToList();
        }

        /// <summary>
        /// What the model has learned, for display: every team/player and
        /// competition with the weight it currently carries, strongest first.
        /// A team's weight is what a game featuring it scores on team affinity
        /// alone, so anything at or above <see cref="Threshold"/> is recommended.
        /// </summary>
        public IReadOnlyList<ProfileEntry> Profile()
        {
            var entries = new Dictionary<(string kind, string name), ProfileEntry>();
            void Add(string kind, string name, string sport, double w)
            {
                if (name == "") return;
                var key = (kind, name.ToLowerInvariant());
                if (!entries.TryGetValue(key, out var e))
                    entries[key] = e = new ProfileEntry { Kind = kind, Name = name, Sport = sport };
                e.Weight += w;
            }

            foreach (var (r, w) in WeightedSignals())
            {
                foreach (var p in Players(r.HomeTeam).Concat(Players(r.AwayTeam))
                                                     .Distinct(StringComparer.OrdinalIgnoreCase))
                    Add("team", p, r.Sport, w);
                Add("competition", r.Competition, r.Sport, w * 0.5);
            }
            foreach (var e in entries.Values.Where(e => e.Kind == "competition"))
                e.Weight = Math.Min(e.Weight, MaxCompetitionScore);
            foreach (var r in records.Where(r => r.Source == "dislike"))
                entries[("dislike", $"{r.HomeTeam} vs {r.AwayTeam}".ToLowerInvariant())] = new ProfileEntry
                {
                    Kind = "dislike",
                    Name = $"{r.HomeTeam} vs {r.AwayTeam}",
                    Sport = r.Sport,
                    GameId = r.GameId
                };

            return entries.Values.OrderByDescending(e => e.Weight).ToList();
        }

        /// <summary>
        /// Removes what the model learned about a team/player or competition
        /// (every positive record involving it), or lifts a single dislike.
        /// </summary>
        public void Forget(ProfileEntry entry)
        {
            switch (entry.Kind)
            {
                case "team":
                    records.RemoveAll(r => r.Source != "dislike"
                        && Players(r.HomeTeam).Concat(Players(r.AwayTeam)).Any(p => Same(p, entry.Name)));
                    break;
                case "competition":
                    records.RemoveAll(r => r.Source != "dislike" && Same(r.Competition, entry.Name));
                    break;
                case "dislike":
                    records.RemoveAll(r => r.Source == "dislike" && r.GameId == entry.GameId);
                    break;
            }
            Save();
        }

        /// <summary>
        /// Sports the user follows, most-recorded first. Empty until there is
        /// enough history, so discovery stays idle until the profile is usable.
        /// </summary>
        public IReadOnlyList<string> FollowedSports()
        {
            if (!HasEnoughData)
                return Array.Empty<string>();
            return records
                .Where(r => r.Sport != "" && r.Source != "dislike")
                .GroupBy(r => r.Sport, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .ToList();
        }

        // Exponential decay by age: 1.0 for the newest signal, 0.5 at HalfLifeDays
        // older, and so on. Age is measured from the latest recorded signal, not
        // from today, so time the app sat unused doesn't erase the profile — it
        // should come back as it was, then shift as new stars arrive.
        private static double Recency(GameInterest r, DateTime latest)
        {
            var ageDays = (latest - r.Timestamp).TotalDays;
            return ageDays <= 0 ? 1.0 : Math.Pow(2.0, -ageDays / HalfLifeDays);
        }

        private DateTime LatestSignal()
        {
            var latest = DateTime.MinValue;
            foreach (var r in records)
                if (r.Source != "dislike" && r.Timestamp > latest)
                    latest = r.Timestamp;
            return latest == DateTime.MinValue ? DateTime.Now : latest;
        }

        private static bool Mentions(GameInterest r, string team) =>
            team != "" && (Same(r.HomeTeam, team) || Same(r.AwayTeam, team));

        private static readonly char[] PlayerSep = { '/' };

        // Individual players in a participant name ("Borges N. / Cabral F." -> 2).
        private static IEnumerable<string> Players(string team) =>
            team.Split(PlayerSep, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim());

        // True if the record and the candidate share any player, so a positive
        // signal on a player follows them to their other games — a doubles match
        // with a different partner, or their singles — and vice versa.
        private static bool SharesParticipant(GameInterest r, string home, string away)
        {
            var recorded = Players(r.HomeTeam).Concat(Players(r.AwayTeam)).ToList();
            foreach (var c in Players(home).Concat(Players(away)))
                foreach (var rp in recorded)
                    if (c.Length > 0 && Same(c, rp))
                        return true;
            return false;
        }

        private static bool Same(string a, string b) =>
            string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        private static GameInterest Create(string gameId, string sport, string competition,
                                           string home, string away, string source) =>
            new GameInterest
            {
                GameId = gameId,
                Sport = sport,
                Competition = CleanCompetition(competition),
                HomeTeam = CleanTeam(home),
                AwayTeam = CleanTeam(away),
                Source = source
            };

        // Scraped names sometimes carry row markup text: a knockout badge
        // ("SpainAdvancing to next round: Spain") or a tennis set marker
        // ("van Assche L.SET"). European competitions also tag clubs with their
        // country ("Benfica (Por)") where the domestic league doesn't. Left in,
        // none of these match the plain name.
        private static readonly Regex AdvancingSuffix = new(@"Advancing to next round:.*$", RegexOptions.IgnoreCase);
        private static readonly Regex SetSuffix = new(@"(?<=\.)SET$");
        private static readonly Regex CountryTag = new(@"\s*\([A-Z][a-z]{2}\)$");

        private static string CleanTeam(string team)
        {
            var t = AdvancingSuffix.Replace(team ?? "", "").Trim();
            t = SetSuffix.Replace(t, "");
            return CountryTag.Replace(t, "").Trim();
        }

        // The same competition arrives with or without a trailing sport tag
        // ("...SINGLES: (Tennis)" vs "...SINGLES:"); drop it so both match.
        private static readonly Regex SportTag = new(@"\s*\([A-Za-z ]+\)\s*$");

        private static string CleanCompetition(string competition) =>
            SportTag.Replace(competition ?? "", "").Trim();

        // Weekly copies of the history, so one bad write or a wiped folder
        // doesn't lose months of learning. The newest few are kept.
        private static readonly string backupDir = Path.Combine(
            Path.GetDirectoryName(filePath)!, "backups");
        private const int BackupEveryDays = 7, BackupsKept = 8;

        private static List<GameInterest> Load()
        {
            var loaded = Read(filePath);
            if (loaded != null)
                BackupIfDue();
            else
                // Unreadable (or missing) history: fall back to the newest
                // backup rather than starting over and overwriting it.
                loaded = Backups().Select(Read).FirstOrDefault(l => l != null) ?? new List<GameInterest>();

            // Also repairs records saved before the names were cleaned.
            foreach (var r in loaded)
            {
                r.Competition = CleanCompetition(r.Competition);
                r.HomeTeam = CleanTeam(r.HomeTeam);
                r.AwayTeam = CleanTeam(r.AwayTeam);
            }
            return loaded;
        }

        private static List<GameInterest>? Read(string path)
        {
            try
            {
                if (File.Exists(path))
                    return JsonConvert.DeserializeObject<List<GameInterest>>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Interest Load Error ({path}): {ex.Message}");
            }
            return null;
        }

        // Newest first; the date in the name sorts chronologically.
        private static IEnumerable<string> Backups() =>
            Directory.Exists(backupDir)
                ? Directory.GetFiles(backupDir, "interests-*.json").OrderByDescending(f => f)
                : Enumerable.Empty<string>();

        private static void BackupIfDue()
        {
            try
            {
                var newest = Backups().FirstOrDefault();
                if (newest != null
                    && DateTime.Now - File.GetLastWriteTime(newest) < TimeSpan.FromDays(BackupEveryDays))
                    return;
                Directory.CreateDirectory(backupDir);
                File.Copy(filePath, Path.Combine(backupDir, $"interests-{DateTime.Now:yyyyMMdd}.json"), true);
                foreach (var old in Backups().Skip(BackupsKept))
                    File.Delete(old);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Interest Backup Error: {ex.Message}");
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                // Write aside, then swap in, so a crash mid-write can't leave a
                // truncated file that fails to load.
                var tmp = filePath + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(records, Formatting.Indented));
                File.Move(tmp, filePath, true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Interest Save Error: {ex.Message}");
            }
        }
    }
}
