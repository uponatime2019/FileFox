using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace FileFox.Services;

public static class AppLogger
{
    private static readonly object SyncRoot = new();
    private static readonly string LogFilePath;

    static AppLogger()
    {
        try
        {
            var logsDirectory = Path.Combine(AppStorageService.DataFolder, "logs");
            Directory.CreateDirectory(logsDirectory);
            Debug.WriteLine($"[AppLogger] Log directory: {logsDirectory}");

            var unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var fileName = $"app_session_{unixTimestamp}.txt";
            LogFilePath = Path.Combine(logsDirectory, fileName);
            AppStorageService.SetSetting("LastSessionLogFile", fileName);
            WriteEntry("Logger_Initialized", new Dictionary<string, object>
            {
                ["logFile"] = LogFilePath
            });
        }
        catch (Exception ex)
        {
            LogFilePath = string.Empty;
            Debug.WriteLine("[Logger Error] Failed to initialize logger: " + ex.Message);
        }
    }

    public static void Initialize()
    {
        try
        {
            LogAction("Application_Starting");
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[Logger Error] " + ex.Message);
        }
    }

    public static void LogAction(string actionName, Dictionary<string, object>? parameters = null)
    {
        try
        {
            WriteEntry(actionName, parameters);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[Logger Error] " + ex.Message);
        }
    }

    public static void LogException(Exception ex, string context)
    {
        try
        {
            var exceptions = new List<Dictionary<string, object?>>();
            Exception? current = ex;
            while (current != null)
            {
                exceptions.Add(new Dictionary<string, object?>
                {
                    ["type"] = current.GetType().FullName,
                    ["message"] = current.Message,
                    ["stackTrace"] = current.StackTrace
                });
                current = current.InnerException;
            }

            WriteEntry("Unhandled_Exception", new Dictionary<string, object>
            {
                ["context"] = context,
                ["exceptionChain"] = exceptions,
                ["fullException"] = ex.ToString()
            });
        }
        catch (Exception logEx)
        {
            Debug.WriteLine("[Logger Error] Failed to log exception: " + logEx.Message);
        }
    }

    private static void WriteEntry(string actionName, Dictionary<string, object>? parameters)
    {
        try
        {
            var entry = new Dictionary<string, object?>
            {
                ["timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                ["action"] = actionName,
                ["parameters"] = parameters ?? new Dictionary<string, object>()
            };
            var line = JsonSerializer.Serialize(entry);

            lock (SyncRoot)
            {
                try
                {
                    if (!string.IsNullOrEmpty(LogFilePath))
                    {
                        File.AppendAllText(LogFilePath, line + Environment.NewLine);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[Logger Error] " + ex.Message);
                }
            }

            Debug.WriteLine(line);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[Logger Error] " + ex.Message);
        }
    }
}
