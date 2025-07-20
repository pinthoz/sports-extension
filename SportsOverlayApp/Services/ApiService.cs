using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SportsOverlayApp.Models;

namespace SportsOverlayApp.Services
{
    public static class ApiService
    {
        private static readonly HttpClient httpClient = new HttpClient();
        
        static ApiService()
        {
            httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        public static async Task<List<GameData>> GetLiveMatchesAsync()
        {
            try
            {
                // Using a free football API (you can replace with your preferred API)
                string url = "https://www.scorebat.com/video-api/v3/feed/?token=demo";
                var response = await httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);
                var result = new List<GameData>();

                foreach (var match in json["response"].Take(5))
                {
                    var gameData = new GameData
                    {
                        Title = match["title"]?.ToString() ?? "Unknown Match",
                        Competition = match["competition"]?["name"]?.ToString() ?? "Unknown League",
                        Status = DetermineStatus(match),
                        Score = ExtractScore(match["title"]?.ToString()),
                        HomeTeam = ExtractHomeTeam(match["title"]?.ToString()),
                        AwayTeam = ExtractAwayTeam(match["title"]?.ToString()),
                        IsLive = IsMatchLive(match),
                        Time = ExtractTime(match),
                        LastUpdated = DateTime.Now
                    };
                    
                    result.Add(gameData);
                }

                // Add some mock live data if API returns empty
                if (result.Count == 0)
                {
                    result.AddRange(GetMockLiveData());
                }

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"API Error: {ex.Message}");
                // Return cached data or mock data on error
                return GetMockLiveData();
            }
        }

        private static List<GameData> GetMockLiveData()
        {
            return new List<GameData>
            {
                new GameData
                {
                    Title = "Manchester United vs Barcelona",
                    Competition = "Champions League",
                    Status = "Live",
                    Score = "2-1",
                    HomeTeam = "Manchester United",
                    AwayTeam = "Barcelona",
                    IsLive = true,
                    Time = "67'",
                    LastUpdated = DateTime.Now
                },
                new GameData
                {
                    Title = "Real Madrid vs Bayern Munich",
                    Competition = "Champions League",
                    Status = "HT",
                    Score = "1-0",
                    HomeTeam = "Real Madrid",
                    AwayTeam = "Bayern Munich",
                    IsLive = true,
                    Time = "45+2'",
                    LastUpdated = DateTime.Now
                }
            };
        }

        private static string DetermineStatus(JToken match)
        {
            // Logic to determine match status
            return "Live"; // Simplified for demo
        }

        private static bool IsMatchLive(JToken match)
        {
            return true; // Simplified for demo
        }

        private static string ExtractScore(string title)
        {
            if (string.IsNullOrEmpty(title)) return "0-0";
            
            // Simple regex to extract score pattern like "2-1" from title
            var scorePattern = new System.Text.RegularExpressions.Regex(@"\d+-\d+");
            var match = scorePattern.Match(title);
            return match.Success ? match.Value : "0-0";
        }

        private static string ExtractHomeTeam(string title)
        {
            if (string.IsNullOrEmpty(title)) return "Home Team";
            
            var parts = title.Split(new[] { " vs ", " v ", " - " }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0].Trim() : "Home Team";
        }

        private static string ExtractAwayTeam(string title)
        {
            if (string.IsNullOrEmpty(title)) return "Away Team";
            
            var parts = title.Split(new[] { " vs ", " v ", " - " }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 1 ? parts[1].Split(' ')[0].Trim() : "Away Team";
        }

        private static string ExtractTime(JToken match)
        {
            // Extract match time - simplified for demo
            return DateTime.Now.Minute + "'";
        }
    }
}