using FileFox.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FileFox.Services;

public sealed class TransferQueueService
{
    private readonly SemaphoreSlim _queueLock = new(1, 1);

    public ObservableCollection<TransferItem> Items { get; } = new();

    public Task EnqueueUploadAsync(IRemoteFileSystem remote, string localPath, string remotePath)
    {
        var item = new TransferItem
        {
            Direction = TransferDirection.Upload,
            Name = Path.GetFileName(localPath),
            LocalPath = localPath,
            RemotePath = remotePath
        };
        Items.Insert(0, item);
        return RunAsync(item, (progress, token) => remote.UploadFileAsync(localPath, remotePath, progress, token));
    }

    public Task EnqueueDownloadAsync(IRemoteFileSystem remote, string remotePath, string localPath)
    {
        var item = new TransferItem
        {
            Direction = TransferDirection.Download,
            Name = Path.GetFileName(localPath),
            LocalPath = localPath,
            RemotePath = remotePath
        };
        Items.Insert(0, item);
        return RunAsync(item, (progress, token) => remote.DownloadFileAsync(remotePath, localPath, progress, token));
    }

    public void CancelAll()
    {
        foreach (var item in Items)
        {
            item.Cancel();
        }
    }

    public void ClearCompleted()
    {
        for (var index = Items.Count - 1; index >= 0; index--)
        {
            if (Items[index].State is TransferState.Completed or TransferState.Failed or TransferState.Canceled)
            {
                Items.RemoveAt(index);
            }
        }
    }

    private async Task RunAsync(
        TransferItem item,
        Func<IProgress<TransferProgressInfo>, CancellationToken, Task> operation)
    {
        try
        {
            await _queueLock.WaitAsync(item.CancellationToken);
            try
            {
                item.State = TransferState.Running;
                item.StatusText = "Transferring";
                var progress = new Progress<TransferProgressInfo>(value =>
                {
                    item.TransferredBytes = value.TransferredBytes;
                    item.TotalBytes = value.TotalBytes;
                    item.Progress = value.Percentage;
                    item.StatusText = value.TotalBytes > 0 ? item.DisplayBytes : "Transferring";
                });

                AppLogger.LogAction("Transfer_Started", TransferParameters(item));
                await operation(progress, item.CancellationToken);
                item.Progress = 100;
                item.State = TransferState.Completed;
                item.StatusText = "Completed";
                AppLogger.LogAction("Transfer_Completed", TransferParameters(item));
            }
            finally
            {
                _queueLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            item.State = TransferState.Canceled;
            item.StatusText = "Canceled";
            AppLogger.LogAction("Transfer_Canceled", TransferParameters(item));
        }
        catch (Exception ex)
        {
            item.State = TransferState.Failed;
            item.StatusText = ex.Message;
            AppLogger.LogException(ex, $"Transfer_{item.Direction}");
        }
    }

    private static Dictionary<string, object> TransferParameters(TransferItem item)
    {
        return new Dictionary<string, object>
        {
            ["direction"] = item.Direction.ToString(),
            ["name"] = item.Name,
            ["localPath"] = item.LocalPath,
            ["remotePath"] = item.RemotePath
        };
    }
}
