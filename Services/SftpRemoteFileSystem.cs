using FileFox.Models;
using Renci.SshNet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FileFox.Services;

public sealed class SftpRemoteFileSystem : IRemoteFileSystem
{
    private readonly ConnectionProfile _profile;
    private readonly SftpClient _client;
    private string? _presentedFingerprint;
    private bool _hostKeyTrusted;

    public SftpRemoteFileSystem(ConnectionProfile profile)
    {
        _profile = profile;
        _client = new SftpClient(profile.Host, profile.Port, profile.Username, profile.Password)
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30),
            OperationTimeout = TimeSpan.FromSeconds(30)
        };
        _client.HostKeyReceived += (_, args) =>
        {
            _presentedFingerprint = args.FingerPrintSHA256;
            var trusted = SftpHostKeyStore.GetTrustedFingerprint(profile.Host, profile.Port);
            _hostKeyTrusted = string.Equals(trusted, args.FingerPrintSHA256, StringComparison.Ordinal);
            args.CanTrust = _hostKeyTrusted;
        };
    }

    public bool IsConnected => _client.IsConnected;

    public string ProtocolName => "SFTP";

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (!_hostKeyTrusted && !string.IsNullOrWhiteSpace(_presentedFingerprint))
        {
            throw new HostKeyNotTrustedException(_profile.Host, _profile.Port, _presentedFingerprint!);
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_client.IsConnected)
            {
                _client.Disconnect();
            }
        }, cancellationToken);
    }

    public Task<IReadOnlyList<RemoteFileItem>> ListAsync(string path, CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<RemoteFileItem>>(() => _client.ListDirectory(path)
            .Where(item => item.Name is not "." and not "..")
            .Select(item => new RemoteFileItem
            {
                Name = item.Name,
                FullPath = item.FullName,
                IsDirectory = item.IsDirectory,
                Size = item.Attributes.Size,
                Modified = new DateTimeOffset(item.LastWriteTimeUtc, TimeSpan.Zero),
                Permissions = item.Attributes.ToString() ?? string.Empty
            })
            .OrderByDescending(item => item.IsDirectory)
            .ThenBy(item => item.Name.StartsWith('.'))
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray(), cancellationToken);
    }

    public Task UploadFileAsync(string localPath, string remotePath, IProgress<TransferProgressInfo> progress, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var totalBytes = new FileInfo(localPath).Length;
            using var stream = File.OpenRead(localPath);
            _client.UploadFile(stream, remotePath, true, transferred =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report(new TransferProgressInfo
                {
                    TransferredBytes = (long)transferred,
                    TotalBytes = totalBytes,
                    Percentage = totalBytes == 0 ? 100 : transferred * 100d / (ulong)totalBytes
                });
            });
        }, cancellationToken);
    }

    public Task DownloadFileAsync(string remotePath, string localPath, IProgress<TransferProgressInfo> progress, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var totalBytes = (long)_client.GetAttributes(remotePath).Size;
            using var stream = File.Open(localPath, FileMode.Create, FileAccess.Write, FileShare.None);
            _client.DownloadFile(remotePath, stream, transferred =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report(new TransferProgressInfo
                {
                    TransferredBytes = (long)transferred,
                    TotalBytes = totalBytes,
                    Percentage = totalBytes == 0 ? 100 : transferred * 100d / (ulong)totalBytes
                });
            });
        }, cancellationToken);
    }

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        return Task.Run(() => _client.CreateDirectory(path), cancellationToken);
    }

    public Task DeleteAsync(RemoteFileItem item, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            if (item.IsDirectory)
            {
                _client.DeleteDirectory(item.FullPath);
            }
            else
            {
                _client.DeleteFile(item.FullPath);
            }
        }, cancellationToken);
    }

    public Task RenameAsync(RemoteFileItem item, string destinationPath, CancellationToken cancellationToken)
    {
        return Task.Run(() => _client.RenameFile(item.FullPath, destinationPath), cancellationToken);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
