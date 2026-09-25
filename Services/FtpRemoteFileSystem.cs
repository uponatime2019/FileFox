using FileFox.Models;
using FluentFTP;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FileFox.Services;

public sealed class FtpRemoteFileSystem : IRemoteFileSystem
{
    private readonly AsyncFtpClient _client;
    private readonly ConnectionProfile _profile;

    public FtpRemoteFileSystem(ConnectionProfile profile)
    {
        _profile = profile;
        _client = new AsyncFtpClient(profile.Host, profile.Username, profile.Password, profile.Port);
        _client.Config.EncryptionMode = profile.Protocol switch
        {
            ConnectionProtocol.FtpsExplicit => FtpEncryptionMode.Explicit,
            ConnectionProtocol.FtpsImplicit => FtpEncryptionMode.Implicit,
            _ => FtpEncryptionMode.None
        };
        _client.Config.DataConnectionType = FtpDataConnectionType.AutoPassive;
        _client.Config.ConnectTimeout = 15_000;
        _client.Config.DataConnectionConnectTimeout = 15_000;
        _client.Config.ReadTimeout = 30_000;
        _client.Config.ValidateAnyCertificate = false;
    }

    public bool IsConnected => _client.IsConnected;

    public string ProtocolName => _profile.Protocol switch
    {
        ConnectionProtocol.FtpsExplicit => "FTP over TLS (explicit)",
        ConnectionProtocol.FtpsImplicit => "FTP over TLS (implicit)",
        _ => "FTP"
    };

    public Task ConnectAsync(CancellationToken cancellationToken) => _client.Connect(cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken) => _client.Disconnect(cancellationToken);

    public async Task<IReadOnlyList<RemoteFileItem>> ListAsync(string path, CancellationToken cancellationToken)
    {
        var listing = await _client.GetListing(path, FtpListOption.Auto, cancellationToken).ConfigureAwait(false);
        return listing
            .Where(item => item.Name is not "." and not "..")
            .Select(item => new RemoteFileItem
            {
                Name = item.Name,
                FullPath = item.FullName,
                IsDirectory = item.Type == FtpObjectType.Directory,
                Size = item.Size,
                Modified = item.Modified == default ? default : new DateTimeOffset(item.Modified),
                Permissions = item.RawPermissions ?? string.Empty
            })
            .OrderByDescending(item => item.IsDirectory)
            .ThenBy(item => item.Name.StartsWith('.'))
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task UploadFileAsync(string localPath, string remotePath, IProgress<TransferProgressInfo> progress, CancellationToken cancellationToken)
    {
        var totalBytes = new System.IO.FileInfo(localPath).Length;
        var adapter = new Progress<FtpProgress>(value => progress.Report(new TransferProgressInfo
        {
            TransferredBytes = value.TransferredBytes,
            TotalBytes = totalBytes,
            Percentage = value.Progress
        }));
        await _client.UploadFile(localPath, remotePath, FtpRemoteExists.Overwrite, true, FtpVerify.None, adapter, cancellationToken).ConfigureAwait(false);
    }

    public async Task DownloadFileAsync(string remotePath, string localPath, IProgress<TransferProgressInfo> progress, CancellationToken cancellationToken)
    {
        var totalBytes = await _client.GetFileSize(remotePath, -1, cancellationToken).ConfigureAwait(false);
        var adapter = new Progress<FtpProgress>(value => progress.Report(new TransferProgressInfo
        {
            TransferredBytes = value.TransferredBytes,
            TotalBytes = totalBytes,
            Percentage = value.Progress
        }));
        await _client.DownloadFile(localPath, remotePath, FtpLocalExists.Overwrite, FtpVerify.None, adapter, cancellationToken).ConfigureAwait(false);
    }

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken) => _client.CreateDirectory(path, true, cancellationToken);

    public Task DeleteAsync(RemoteFileItem item, CancellationToken cancellationToken)
    {
        return item.IsDirectory
            ? _client.DeleteDirectory(item.FullPath, cancellationToken)
            : _client.DeleteFile(item.FullPath, cancellationToken);
    }

    public Task RenameAsync(RemoteFileItem item, string destinationPath, CancellationToken cancellationToken)
    {
        return item.IsDirectory
            ? _client.MoveDirectory(item.FullPath, destinationPath, FtpRemoteExists.Overwrite, cancellationToken)
            : _client.MoveFile(item.FullPath, destinationPath, FtpRemoteExists.Overwrite, cancellationToken);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
