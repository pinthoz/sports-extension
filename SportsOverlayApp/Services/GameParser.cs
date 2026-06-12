using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SportsOverlayApp.Models;

namespace SportsOverlayApp.Services
{
    /// <summary>
    /// Converts scraped game JSON (from the browser extension or the embedded
    /// FlashScore browser. Both produce the same shape) into GameData.
    /// </summary>
    public static class GameParser
    {
        public static List<GameData> FromJArray(JArray? array)
        {
            var games = new List<GameData>();
            foreach (var g in array ?? new JArray())
            {
                var home = g["home"]?.ToString() ?? "";
                var away = g["away"]?.ToString() ?? "";
                var homeScore = g["homeScore"]?.ToString() ?? "-";
                var awayScore = g["awayScore"]?.ToString() ?? "-";

                var gameData = new GameData
                {
                    Id = g["id"]?.ToString() ?? $"{home}-{away}",
                    Sport = g["sport"]?.ToString() ?? "football",
                    Title = $"{home} vs {away}",
                    HomeTeam = home,
                    AwayTeam = away,
                    Score = $"{homeScore}-{awayScore}",
                    HomeParts = (g["homeParts"] as JArray)?.ToObject<List<string>>() ?? new List<string>(),
                    AwayParts = (g["awayParts"] as JArray)?.ToObject<List<string>>() ?? new List<string>(),
                    HomePoints = g["homePoints"]?.ToString() ?? "",
                    AwayPoints = g["awayPoints"]?.ToString() ?? "",
                    Serving = g["serving"]?.ToString() ?? "",
                    Time = g["stage"]?.ToString() ?? "",
                    Status = g["isFinished"]?.Value<bool>() == true ? "Finished"
                           : g["isLive"]?.Value<bool>() == true ? "Live"
                           : "Scheduled",
                    IsLive = g["isLive"]?.Value<bool>() ?? false,
                    IsFinished = g["isFinished"]?.Value<bool>() ?? false,
                    Competition = g["competition"]?.ToString() ?? "",
                    LastUpdated = DateTime.Now
                };
                gameData.Ranking = (g["ranking"] as JArray)?.ToObject<List<RankingEntry>>()
                                   ?? new List<RankingEntry>();
                if (gameData.Ranking.Count > 0)
                {
                    // Ranking events: the "score" is the leader's time, and the
                    // title is the session name, not "X vs Y".
                    gameData.Title = g["title"]?.ToString() ?? gameData.Title;
                    gameData.Score = gameData.Ranking[0].Time;
                }
                ExtractTennisPoints(gameData);
                games.Add(gameData);
            }
            return games;
        }

        private static readonly Regex GamePoints = new("^(0|15|30|40|A|AD)$", RegexOptions.IgnoreCase);

        // Safety net for scrapers that still deliver the live game points
        // (0/15/30/40/A) as a trailing "set" column instead of as points.
        private static void ExtractTennisPoints(GameData g)
        {
            if (g.Sport != "tennis" || !g.IsLive || g.HomePoints != "" || g.AwayPoints != "")
                return;
            if (g.HomeParts.Count == 0 || g.HomeParts.Count != g.AwayParts.Count)
                return;

            var home = g.HomeParts[^1];
            var away = g.AwayParts[^1];
            if (GamePoints.IsMatch(home) && GamePoints.IsMatch(away))
            {
                g.HomePoints = home;
                g.AwayPoints = away;
                g.HomeParts.RemoveAt(g.HomeParts.Count - 1);
                g.AwayParts.RemoveAt(g.AwayParts.Count - 1);
            }
        }
    }
}
