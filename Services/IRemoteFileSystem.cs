using FileFox.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FileFox.Services;

public interface IRemoteFileSystem : IDisposable
{
    bool IsConnected { get; }

    string ProtocolName { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<RemoteFileItem>> ListAsync(string path, CancellationToken cancellationToken);

    Task UploadFileAsync(string localPath, string remotePath, IProgress<TransferProgressInfo> progress, CancellationToken cancellationToken);

    Task DownloadFileAsync(string remotePath, string localPath, IProgress<TransferProgressInfo> progress, CancellationToken cancellationToken);

    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken);

    Task DeleteAsync(RemoteFileItem item, CancellationToken cancellationToken);

    Task RenameAsync(RemoteFileItem item, string destinationPath, CancellationToken cancellationToken);
}

public static class RemoteFileSystemFactory
{
    public static IRemoteFileSystem Create(ConnectionProfile profile)
    {
        return profile.Protocol == ConnectionProtocol.Sftp
            ? new SftpRemoteFileSystem(profile)
            : new FtpRemoteFileSystem(profile);
    }
}
