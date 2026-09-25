using System;
using System.Collections.Generic;
using System.Linq;

namespace FileFox.Helpers;

public static class RemotePath
{
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        var segments = new List<string>();
        foreach (var segment in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(segment);
        }

        return segments.Count == 0 ? "/" : "/" + string.Join('/', segments);
    }

    public static string Combine(string parent, string child)
    {
        if (string.IsNullOrWhiteSpace(child))
        {
            return Normalize(parent);
        }

        return Normalize($"{Normalize(parent).TrimEnd('/')}/{child}");
    }

    public static string Parent(string path)
    {
        var normalized = Normalize(path);
        if (normalized == "/")
        {
            return normalized;
        }

        var lastSeparator = normalized.LastIndexOf('/');
        return lastSeparator <= 0 ? "/" : normalized[..lastSeparator];
    }

    public static string Name(string path)
    {
        var normalized = Normalize(path);
        return normalized == "/" ? "/" : normalized.Split('/').Last();
    }
}
