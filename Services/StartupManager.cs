using Microsoft.Win32;
using System;
using System.IO;
using System.Reflection;
using Windows.Storage;

namespace FileFox.Services;

public sealed class StartupManager
{
    private const string RunKeyName = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "FileFox";
    private const string SettingName = "IsRunAtStartup";

    public bool IsAutoStartEnabled
    {
        get
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyName, false);
                return key?.GetValue(AppName) is string value && !string.IsNullOrWhiteSpace(value);
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "StartupManager_Read");
                return false;
            }
        }
    }

    public bool SavedPreference
    {
        get
        {
            try
            {
                return ApplicationData.Current.LocalSettings.Values[SettingName] is bool value && value;
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "StartupManager_ReadPreference");
                return false;
            }
        }
    }

    public void SetAutoStart(bool enable)
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[SettingName] = enable;
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyName, true);
            if (key == null)
            {
                return;
            }

            if (enable)
            {
                var executablePath = GetExecutablePath();
                if (!string.IsNullOrWhiteSpace(executablePath))
                {
                    key.SetValue(AppName, $"\"{executablePath}\" --startup");
                }
            }
            else
            {
                key.DeleteValue(AppName, false);
            }

            AppLogger.LogAction("Startup_Setting_Changed", new System.Collections.Generic.Dictionary<string, object>
            {
                ["enabled"] = enable
            });
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "StartupManager_Write");
        }
    }

    public void SyncFromSettings()
    {
        SetAutoStart(SavedPreference);
    }

    private static string GetExecutablePath()
    {
        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                executablePath = Assembly.GetExecutingAssembly().Location;
            }

            return string.IsNullOrWhiteSpace(executablePath) ? string.Empty : Path.GetFullPath(executablePath);
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "StartupManager_GetExecutablePath");
            return string.Empty;
        }
    }
}
