using FileFox.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FileFox.Services;

public sealed class SiteManagerService
{
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly string _filePath = Path.Combine(AppStorageService.DataFolder, "sites.json");
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public async Task<IReadOnlyList<ConnectionProfile>> LoadAsync()
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
            {
                return Array.Empty<ConnectionProfile>();
            }

            var json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<ConnectionProfile>>(json, _jsonOptions) ?? new List<ConnectionProfile>();
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "SiteManager_Load");
            return Array.Empty<ConnectionProfile>();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveAsync(ConnectionProfile profile)
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var sites = new List<ConnectionProfile>();
            if (File.Exists(_filePath))
            {
                var existingJson = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
                sites = JsonSerializer.Deserialize<List<ConnectionProfile>>(existingJson, _jsonOptions) ?? sites;
            }

            var index = sites.FindIndex(site => site.Id == profile.Id);
            if (index >= 0)
            {
                sites[index] = profile;
            }
            else
            {
                sites.Add(profile);
            }

            var json = JsonSerializer.Serialize(sites.OrderBy(site => site.Name), _jsonOptions);
            await File.WriteAllTextAsync(_filePath, json).ConfigureAwait(false);
            AppLogger.LogAction("Site_Saved", new Dictionary<string, object>
            {
                ["name"] = profile.Name,
                ["host"] = profile.Host,
                ["protocol"] = profile.Protocol.ToString()
            });
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "SiteManager_Save");
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task DeleteAsync(ConnectionProfile profile)
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            var existingJson = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
            var sites = JsonSerializer.Deserialize<List<ConnectionProfile>>(existingJson, _jsonOptions) ?? new List<ConnectionProfile>();

            var countBefore = sites.Count;
            sites.RemoveAll(site => site.Id == profile.Id);

            if (sites.Count != countBefore)
            {
                var json = JsonSerializer.Serialize(sites.OrderBy(site => site.Name), _jsonOptions);
                await File.WriteAllTextAsync(_filePath, json).ConfigureAwait(false);
                AppLogger.LogAction("Site_Deleted", new Dictionary<string, object>
                {
                    ["id"] = profile.Id,
                    ["name"] = profile.Name
                });
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "SiteManager_Delete");
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
