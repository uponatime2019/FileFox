using System;
using System.Globalization;

namespace FileFox.Models;

public sealed class LocalFileItem
{
    public string Name { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;

    public bool IsDirectory { get; set; }

    public long Size { get; set; }

    public DateTimeOffset Modified { get; set; }

    public string Glyph => FileIconHelper.GetGlyph(Name, IsDirectory);

    public string DisplaySize => IsDirectory ? string.Empty : FileSizeFormatter.Format(Size);

    public string DisplayModified => Modified == default ? string.Empty : Modified.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);
}

public sealed class RemoteFileItem
{
    public string Name { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;

    public bool IsDirectory { get; set; }

    public long Size { get; set; }

    public DateTimeOffset Modified { get; set; }

    public string Permissions { get; set; } = string.Empty;

    public string Glyph => FileIconHelper.GetGlyph(Name, IsDirectory);

    public string DisplaySize => IsDirectory ? string.Empty : FileSizeFormatter.Format(Size);

    public string DisplayModified => Modified == default ? string.Empty : Modified.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);
}

public static class FileIconHelper
{
    public static string GetGlyph(string fileName, bool isDirectory)
    {
        if (isDirectory)
        {
            return "\uE8B7"; // Folder
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "\uE8A5";
        }

        var ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            // Image files
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".svg" or ".webp" or ".ico" or ".tiff" or ".psd" or ".raw" => "\uEB9F",

            // Video files
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".flv" or ".webm" or ".m4v" or ".3gp" or ".mpg" or ".mpeg" => "\uE8B2",

            // Audio files
            ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a" or ".wma" or ".mid" or ".midi" => "\uE8D6",

            // Executables / Binaries
            ".exe" or ".msi" or ".bat" or ".cmd" or ".ps1" or ".vbs" or ".sh" or ".app" or ".apk" => "\uE756",

            // DLL / System libraries / Drivers
            ".dll" or ".sys" or ".drv" or ".ocx" or ".so" or ".dylib" or ".bin" or ".dat" => "\uE713",

            // Config / Settings / Code files
            ".json" or ".xml" or ".yaml" or ".yml" or ".config" or ".ini" or ".env" or ".conf" or ".toml" or ".properties" => "\uE943",

            // Archive / Zip files
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" or ".iso" or ".xz" => "\uF012",

            // Text files / Documents
            ".txt" or ".doc" or ".docx" or ".pdf" or ".rtf" or ".md" or ".log" or ".csv" or ".tsv" => "\uE8A5",

            // Code / Scripts
            ".cs" or ".js" or ".ts" or ".jsx" or ".tsx" or ".html" or ".css" or ".py" or ".cpp" or ".c" or ".h" or ".java" or ".php" or ".sql" => "\uE943",

            // Default file
            _ => "\uE8A5"
        };
    }

    public static bool IsEditableTextFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".txt" or ".json" or ".xml" or ".html" or ".htm" or ".css" or ".js" or ".ts" or ".jsx" or ".tsx" or
            ".cs" or ".py" or ".cpp" or ".c" or ".h" or ".java" or ".php" or ".sql" or ".md" or ".log" or
            ".env" or ".ini" or ".conf" or ".config" or ".yaml" or ".yml" or ".sh" or ".bat" or ".cmd" or
            ".ps1" or ".toml" or ".properties" or ".htaccess" or ".gitignore" or ".csv" or ".tsv" or "" => true,
            _ => false
        };
    }
}

public static class FileSizeFormatter
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    public static string Format(long bytes)
    {
        if (bytes < 0)
        {
            return string.Empty;
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} {Units[unit]}" : $"{value:0.##} {Units[unit]}";
    }
}
