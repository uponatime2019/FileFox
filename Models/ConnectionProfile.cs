using System;
using System.Text.Json.Serialization;

namespace FileFox.Models;

public enum ConnectionProtocol
{
    Ftp,
    FtpsExplicit,
    FtpsImplicit,
    Sftp
}

public sealed class ConnectionProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public ConnectionProtocol Protocol { get; set; } = ConnectionProtocol.Ftp;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 21;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string RemotePath { get; set; } = "/";

    public string LocalPath { get; set; } = string.Empty;

    public System.Collections.Generic.List<string> RemoteBookmarks { get; set; } = new();

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Host : $"{Name} — {Host}";

    public ConnectionProfile CloneWithoutPassword()
    {
        return new ConnectionProfile
        {
            Id = Id,
            Name = Name,
            Protocol = Protocol,
            Host = Host,
            Port = Port,
            Username = Username,
            RemotePath = RemotePath
        };
    }

    public static int DefaultPort(ConnectionProtocol protocol)
    {
        return protocol switch
        {
            ConnectionProtocol.Sftp => 22,
            ConnectionProtocol.FtpsImplicit => 990,
            _ => 21
        };
    }
}
