using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;

namespace FileFox.Models;

public enum TransferDirection
{
    Upload,
    Download
}

public enum TransferState
{
    Queued,
    Running,
    Completed,
    Failed,
    Canceled
}

public sealed class TransferProgressInfo
{
    public long TransferredBytes { get; init; }

    public long TotalBytes { get; init; }

    public double Percentage { get; init; }
}

public sealed class TransferItem : INotifyPropertyChanged
{
    private readonly CancellationTokenSource _cancellation = new();
    private double _progress;
    private TransferState _state = TransferState.Queued;
    private string _statusText = "Queued";
    private long _transferredBytes;
    private long _totalBytes;

    public Guid Id { get; } = Guid.NewGuid();

    public TransferDirection Direction { get; set; }

    public string Name { get; set; } = string.Empty;

    public string LocalPath { get; set; } = string.Empty;

    public string RemotePath { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.Now;

    public CancellationToken CancellationToken => _cancellation.Token;

    public string DirectionGlyph => Direction == TransferDirection.Upload ? "\uE898" : "\uE896";

    public double Progress
    {
        get => _progress;
        set => SetField(ref _progress, Math.Clamp(value, 0, 100));
    }

    public TransferState State
    {
        get => _state;
        set => SetField(ref _state, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public long TransferredBytes
    {
        get => _transferredBytes;
        set
        {
            if (SetField(ref _transferredBytes, value))
            {
                OnPropertyChanged(nameof(DisplayBytes));
            }
        }
    }

    public long TotalBytes
    {
        get => _totalBytes;
        set
        {
            if (SetField(ref _totalBytes, value))
            {
                OnPropertyChanged(nameof(DisplayBytes));
            }
        }
    }

    public string DisplayBytes => TotalBytes > 0
        ? $"{FileSizeFormatter.Format(TransferredBytes)} / {FileSizeFormatter.Format(TotalBytes)}"
        : FileSizeFormatter.Format(TransferredBytes);

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Cancel()
    {
        if (State is TransferState.Queued or TransferState.Running)
        {
            _cancellation.Cancel();
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
