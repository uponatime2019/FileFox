using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FileFox.Services;

/// <summary>
/// Provides cross-platform local storage and settings persistence for unpackaged WinUI 3 execution.
/// </summary>
public static class AppStorageService
{
    private static readonly object SyncLock = new();
    private static readonly string DataDirectory;
    private static readonly string TempDirectory;
    private static readonly string SettingsFilePath;
    private static readonly Dictionary<string, JsonElement> Settings = new();

    static AppStorageService()
    {
        DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileFox");
        TempDirectory = Path.Combine(Path.GetTempPath(), "FileFox");
        SettingsFilePath = Path.Combine(DataDirectory, "settings.json");

        try
        {
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(TempDirectory);
            Directory.CreateDirectory(Path.Combine(DataDirectory, "logs"));
        }
        catch { }

        LoadSettings();
    }

    public static string DataFolder => DataDirectory;
    public static string TempFolder => TempDirectory;

    public static T GetSetting<T>(string key, T defaultValue = default!)
    {
        lock (SyncLock)
        {
            if (Settings.TryGetValue(key, out var element))
            {
                try
                {
                    var targetType = typeof(T);
                    if (targetType == typeof(bool))
                    {
                        return (T)(object)element.GetBoolean();
                    }
                    if (targetType == typeof(string))
                    {
                        return (T)(object)(element.GetString() ?? string.Empty);
                    }
                    if (targetType == typeof(int))
                    {
                        return (T)(object)element.GetInt32();
                    }
                    if (targetType == typeof(long))
                    {
                        return (T)(object)element.GetInt64();
                    }

                    var deserialized = JsonSerializer.Deserialize<T>(element.GetRawText());
                    return deserialized ?? defaultValue;
                }
                catch
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }
    }

    public static void SetSetting<T>(string key, T value)
    {
        lock (SyncLock)
        {
            try
            {
                var json = JsonSerializer.Serialize(value);
                using var doc = JsonDocument.Parse(json);
                Settings[key] = doc.RootElement.Clone();
                SaveSettings();
            }
            catch { }
        }
    }

    private static void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (loaded != null)
                {
                    foreach (var (k, v) in loaded)
                    {
                        Settings[k] = v.Clone();
                    }
                }
            }
        }
        catch { }
    }

    private static void SaveSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch { }
    }
}
