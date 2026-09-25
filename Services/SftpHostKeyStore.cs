using System;
using Windows.Storage;

namespace FileFox.Services;

public static class SftpHostKeyStore
{
    public static string? GetTrustedFingerprint(string host, int port)
    {
        try
        {
            return ApplicationData.Current.LocalSettings.Values[GetKey(host, port)] as string;
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "SftpHostKeyStore_Read");
            return null;
        }
    }

    public static void Trust(string host, int port, string fingerprint)
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[GetKey(host, port)] = fingerprint;
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "SftpHostKeyStore_Write");
            throw;
        }
    }

    private static string GetKey(string host, int port) => $"SftpHostKey:{host.Trim().ToLowerInvariant()}:{port}";
}

public sealed class HostKeyNotTrustedException : Exception
{
    public HostKeyNotTrustedException(string host, int port, string fingerprint)
        : base($"The SFTP host key for {host}:{port} is not trusted.")
    {
        Host = host;
        Port = port;
        Fingerprint = fingerprint;
    }

    public string Host { get; }

    public int Port { get; }

    public string Fingerprint { get; }
}
