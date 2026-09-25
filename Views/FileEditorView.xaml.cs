using FileFox.Models;
using FileFox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FileFox.Views;

public sealed partial class FileEditorView : UserControl
{
    private readonly string _tempLocalPath;
    private readonly string _remotePath;
    private readonly IRemoteFileSystem _remote;

    public event EventHandler? CloseRequested;

    public FileEditorView(string tempLocalPath, string remotePath, IRemoteFileSystem remote)
    {
        InitializeComponent();
        _tempLocalPath = tempLocalPath;
        _remotePath = remotePath;
        _remote = remote;

        FileNameText.Text = Path.GetFileName(remotePath);
        RemotePathText.Text = $"({remotePath})";

        Loaded += FileEditorView_Loaded;
    }

    private async void FileEditorView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= FileEditorView_Loaded;
        await LoadFileContentAsync();
    }

    private async Task LoadFileContentAsync()
    {
        try
        {
            if (File.Exists(_tempLocalPath))
            {
                var content = await File.ReadAllTextAsync(_tempLocalPath, Encoding.UTF8);
                FileContentTextBox.Text = content;
                StatusInfoText.Text = $"Loaded {content.Length} character(s) from {_remotePath}";
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "LoadFileContent");
            StatusInfoText.Text = $"Error reading file: {ex.Message}";
        }
    }

    private async void SaveAndUpload_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusInfoText.Text = "Saving local changes...";
            await File.WriteAllTextAsync(_tempLocalPath, FileContentTextBox.Text, Encoding.UTF8);

            StatusInfoText.Text = $"Uploading changes to {_remotePath}...";
            var progress = new Progress<TransferProgressInfo>(info =>
            {
                StatusInfoText.Text = $"Uploading: {info.Percentage:0}% ({FileSizeFormatter.Format(info.TransferredBytes)} / {FileSizeFormatter.Format(info.TotalBytes)})";
            });

            await _remote.UploadFileAsync(_tempLocalPath, _remotePath, progress, CancellationToken.None);
            StatusInfoText.Text = "Upload complete! Closing tab...";

            AppLogger.LogAction("FileEditor_SaveAndUpload", new System.Collections.Generic.Dictionary<string, object>
            {
                ["remotePath"] = _remotePath
            });

            await Task.Delay(300);
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "SaveAndUploadFile");
            StatusInfoText.Text = $"Upload failed: {ex.Message}";
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
