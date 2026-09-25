using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace FileFox.Services;

public sealed class LocalBookmarkService
{
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly string _filePath = Path.Combine(AppStorageService.DataFolder, "local_bookmarks.json");
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public async Task<List<string>> LoadAsync()
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
            {
                return new List<string>();
            }

            var json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<string>>(json, _jsonOptions) ?? new List<string>();
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "LoadLocalBookmarks");
            return new List<string>();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveAsync(IEnumerable<string> bookmarks)
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var distinctList = bookmarks
                .Where(b => !string.IsNullOrWhiteSpace(b))
                .Select(b => b.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(b => b)
                .ToList();

            var json = JsonSerializer.Serialize(distinctList, _jsonOptions);
            await File.WriteAllTextAsync(_filePath, json).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "SaveLocalBookmarks");
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
