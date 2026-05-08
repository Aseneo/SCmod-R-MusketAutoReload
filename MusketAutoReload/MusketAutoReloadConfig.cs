using System;
using System.Text.Json;
using Engine;

namespace Game {
    public class MusketAutoReloadConfigData {
        public bool EnableReloadCooldown { get; set; } = true;
    }

    public static class MusketAutoReloadConfig {
        public static bool EnableReloadCooldown = true;

        public static string ConfigPath => Storage.CombinePaths(ModsManager.ModsPath, "MusketAutoReloadConfig.json");

        public static void Load() {
            MusketAutoReloadConfigData data = new();
            if (Storage.FileExists(ConfigPath)) {
                try {
                    string json = Storage.ReadAllText(ConfigPath);
                    data = JsonSerializer.Deserialize<MusketAutoReloadConfigData>(json) ?? new();
                }
                catch (Exception e) {
                    Log.Warning($"MusketAutoReload: Failed to load config, using defaults. {e.Message}");
                }
            }
            ApplyData(data);
            Save(data);
        }

        static void Save(MusketAutoReloadConfigData data) {
            try {
                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                Storage.WriteAllText(ConfigPath, json);
            }
            catch (Exception e) {
                Log.Warning($"MusketAutoReload: Failed to save config. {e.Message}");
            }
        }

        public static void ApplyData(MusketAutoReloadConfigData data) {
            EnableReloadCooldown = data.EnableReloadCooldown;
        }
    }
}
