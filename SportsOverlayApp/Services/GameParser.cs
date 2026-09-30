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
                var home = StripWinnerMark(g["home"]?.ToString() ?? "");
                var away = StripWinnerMark(g["away"]?.ToString() ?? "");
                var homeScore = g["homeScore"]?.ToString() ?? "-";
                var awayScore = g["awayScore"]?.ToString() ?? "-";

                var gameData = new GameData
                {
                    Id = g["id"]?.ToString() ?? $"{home}-{away}",
                    Sport = g["sport"]?.ToString() ?? "football",
                    Title = $"{home} vs {away}",
                    HomeTeam = home,
                    AwayTeam = away,
                    HomeFlag = g["homeFlag"]?.ToString() ?? "",
                    AwayFlag = g["awayFlag"]?.ToString() ?? "",
                    HomeFlag2 = g["homeFlag2"]?.ToString() ?? "",
                    AwayFlag2 = g["awayFlag2"]?.ToString() ?? "",
                    HomeLogoUrl = g["homeLogo"]?.ToString() ?? "",
                    AwayLogoUrl = g["awayLogo"]?.ToString() ?? "",
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
                    Starred = g["starred"]?.Value<bool>() ?? true,
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
                games.Add(gameData);
            }
            return games;
        }

        // When a tennis match ends, FlashScore puts a small "SET" badge in the
        // winner's name cell and its text gets glued to the name
        // ("Djokovic N.SET"; in doubles possibly "Cabral F.SET / Tracy J.").
        private static readonly Regex WinnerMark = new(@"(?<=\.)SET(?=\s*(/|$))");

        public static string StripWinnerMark(string name) => WinnerMark.Replace(name, "");
    }
}
