using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using SportsOverlayApp.Models;

namespace SportsOverlayApp.Services
{
    public static class CacheService
    {
        private static readonly string preferencesPath = "preferences.json";
        private static readonly string cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SportsOverlay");

        static CacheService()
        {
            if (!Directory.Exists(cacheDir))
                Directory.CreateDirectory(cacheDir);
        }

        public static void SavePreferences(UserPreferences preferences)
        {
            try
            {
                var fullPath = Path.Combine(cacheDir, preferencesPath);
                var json = JsonConvert.SerializeObject(preferences, Formatting.Indented);
                File.WriteAllText(fullPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Cache Save Error: {ex.Message}");
            }
        }

        public static UserPreferences LoadPreferences()
        {
            try
            {
                var fullPath = Path.Combine(cacheDir, preferencesPath);
                if (File.Exists(fullPath))
                {
                    var json = File.ReadAllText(fullPath);
                    return JsonConvert.DeserializeObject<UserPreferences>(json) ?? new UserPreferences();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Cache Load Error: {ex.Message}");
            }
            
            return new UserPreferences();
        }

        public static void SaveGameData(List<GameData> gameData)
        {
            try
            {
                var fullPath = Path.Combine(cacheDir, "gamedata.json");
                var json = JsonConvert.SerializeObject(gameData, Formatting.Indented);
                File.WriteAllText(fullPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Game Data Save Error: {ex.Message}");
            }
        }

        public static List<GameData> LoadGameData()
        {
            try
            {
                var fullPath = Path.Combine(cacheDir, "gamedata.json");
                if (File.Exists(fullPath))
                {
                    var json = File.ReadAllText(fullPath);
                    return JsonConvert.DeserializeObject<List<GameData>>(json) ?? new List<GameData>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Game Data Load Error: {ex.Message}");
            }
            
            return new List<GameData>();
        }
    }
}