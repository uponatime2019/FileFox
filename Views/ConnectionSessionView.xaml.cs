using FileFox.Helpers;
using FileFox.Models;
using FileFox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace FileFox.Views;

public sealed partial class ConnectionSessionView : UserControl
{
    private readonly TransferQueueService _transferQueue = new();
    private readonly SiteManagerService _siteManager = new();
    private readonly LocalBookmarkService _localBookmarkService = new();
    private readonly Queue<string> _messageLines = new();
    private List<string> _localBookmarks = new();
    private IRemoteFileSystem? _remote;
    private ConnectionProfile _profile;

    public event EventHandler<string>? TitleChanged;
    public event EventHandler<(RemoteFileItem FileItem, IRemoteFileSystem RemoteSystem)>? EditFileRequested;

    public ConnectionProfile Profile => _profile;
    public IntPtr WindowHandle { get; set; }

    public ObservableCollection<LocalFileItem> LocalItems { get; } = new();
    public ObservableCollection<RemoteFileItem> RemoteItems { get; } = new();
    public ObservableCollection<TransferItem> TransferItems => _transferQueue.Items;

    public ConnectionSessionView(ConnectionProfile profile, IntPtr windowHandle)
    {
        InitializeComponent();
        _profile = profile;
        WindowHandle = windowHandle;
        Loaded += ConnectionSessionView_Loaded;
    }

    private async void ConnectionSessionView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= ConnectionSessionView_Loaded;
        _localBookmarks = await _localBookmarkService.LoadAsync();

        if (!string.IsNullOrWhiteSpace(_profile.LocalPath) && Directory.Exists(_profile.LocalPath))
        {
            LocalPathTextBox.Text = _profile.LocalPath;
        }
        else
        {
            LocalPathTextBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        HostTitleText.Text = string.IsNullOrWhiteSpace(_profile.Name) ? _profile.Host : _profile.Name;
        HostIpText.Text = string.Equals(_profile.Name, _profile.Host, StringComparison.OrdinalIgnoreCase) ? string.Empty : $"[{_profile.Host}:{_profile.Port}]";
        ProtocolText.Text = $"({ProtocolDisplayName(_profile.Protocol)})";
        ProtocolLabel.Text = ProtocolDisplayName(_profile.Protocol);

        LoadFavicon(_profile.Host);

        UpdateLocalBookmarkState();
        UpdateRemoteBookmarkState();

        AppendMessage("Status", $"Initializing session for {_profile.Host}...");
        await RefreshLocalAsync();
        await ConnectAsync(_profile, true);
    }

    private void LoadFavicon(string host)
    {
        try
        {
            var faviconUrl = $"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(host)}&sz=32";
            var bitmap = new BitmapImage(new Uri(faviconUrl));
            bitmap.ImageFailed += (_, _) =>
            {
                FaviconImage.Visibility = Visibility.Collapsed;
            };
            bitmap.ImageOpened += (_, _) =>
            {
                FaviconImage.Visibility = Visibility.Visible;
            };
            FaviconImage.Source = bitmap;
        }
        catch
        {
            FaviconImage.Visibility = Visibility.Collapsed;
        }
    }

    public async Task<bool> ConnectAsync(ConnectionProfile profile, bool allowHostKeyPrompt)
    {
        _profile = profile;
        DisconnectTopButton.IsEnabled = false;
        ReconnectButton.IsEnabled = false;

        try
        {
            await DisconnectInternalAsync(false);
            SetConnectionStatus($"Connecting to {profile.Host}:{profile.Port}…", false);
            AppendMessage("Status", $"Connecting with {ProtocolDisplayName(profile.Protocol)} to {profile.Host}:{profile.Port}");

            var remote = RemoteFileSystemFactory.Create(profile);
            try
            {
                await remote.ConnectAsync(CancellationToken.None);
            }
            catch (HostKeyNotTrustedException ex) when (allowHostKeyPrompt)
            {
                remote.Dispose();
                var result = await ShowSftpHostKeyDialogAsync(ex);
                if (result != ContentDialogResult.Primary)
                {
                    SetConnectionStatus("Connection canceled: host key was not trusted", false);
                    ReconnectButton.IsEnabled = true;
                    return false;
                }

                SftpHostKeyStore.Trust(ex.Host, ex.Port, ex.Fingerprint);
                AppLogger.LogAction("Sftp_HostKey_Trusted", new Dictionary<string, object>
                {
                    ["host"] = ex.Host,
                    ["port"] = ex.Port,
                    ["fingerprint"] = ex.Fingerprint
                });

                return await ConnectAsync(profile, false);
            }
            catch
            {
                remote.Dispose();
                throw;
            }

            _remote = remote;
            RemotePathTextBox.IsEnabled = true;
            RemotePathTextBox.Text = RemotePath.Normalize(profile.RemotePath);
            DisconnectTopButton.IsEnabled = true;
            ReconnectButton.IsEnabled = true;

            SetConnectionStatus($"Connected to {profile.Host} using {remote.ProtocolName}", true);
            AppendMessage("Status", "Connection established.");
            TitleChanged?.Invoke(this, profile.DisplayName);

            AppLogger.LogAction("Connection_Established", new Dictionary<string, object>
            {
                ["host"] = profile.Host,
                ["port"] = profile.Port,
                ["protocol"] = profile.Protocol.ToString(),
                ["username"] = profile.Username
            });

            await RefreshRemoteAsync();
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "ConnectSession");
            AppendMessage("Error", ex.Message);
            SetConnectionStatus($"Connection failed: {ex.Message}", false);
            ReconnectButton.IsEnabled = true;
            await ShowErrorAsync("Connection failed", ex.Message);
            return false;
        }
    }

    private static string ProtocolDisplayName(ConnectionProtocol protocol)
    {
        return protocol switch
        {
            ConnectionProtocol.FtpsExplicit => "Explicit FTPS",
            ConnectionProtocol.FtpsImplicit => "Implicit FTPS",
            ConnectionProtocol.Sftp => "SFTP",
            _ => "FTP"
        };
    }

    private async void Reconnect_Click(object sender, RoutedEventArgs e)
    {
        await ConnectAsync(_profile, true);
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        await DisconnectInternalAsync(true);
    }

    public async Task DisconnectInternalAsync(bool logAction)
    {
        _transferQueue.CancelAll();
        var remote = _remote;
        _remote = null;

        if (remote != null)
        {
            try
            {
                await remote.DisconnectAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "Disconnect");
            }
            finally
            {
                remote.Dispose();
            }
        }

        RemoteItems.Clear();
        RemotePathTextBox.IsEnabled = false;
        DisconnectTopButton.IsEnabled = false;
        SetConnectionStatus("Disconnected", false);
        if (logAction && remote != null)
        {
            AppendMessage("Status", "Disconnected.");
            AppLogger.LogAction("Connection_Disconnected");
        }
    }

    private async Task RefreshLocalAsync()
    {
        try
        {
            var path = Path.GetFullPath(LocalPathTextBox.Text.Trim());
            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException($"Local folder not found: {path}");
            }

            var items = await Task.Run(() => EnumerateLocalItems(path));
            LocalPathTextBox.Text = path;
            LocalItems.Clear();
            foreach (var item in items)
            {
                LocalItems.Add(item);
            }
            _profile.LocalPath = path;
            _ = SaveProfilePathAsync();
            UpdateLocalBookmarkState();
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "RefreshLocal");
            AppendMessage("Error", ex.Message);
        }
    }

    private static IReadOnlyList<LocalFileItem> EnumerateLocalItems(string path)
    {
        var items = new List<LocalFileItem>();
        foreach (var directoryPath in Directory.EnumerateDirectories(path))
        {
            try
            {
                var directory = new DirectoryInfo(directoryPath);
                items.Add(new LocalFileItem
                {
                    Name = directory.Name,
                    FullPath = directory.FullName,
                    IsDirectory = true,
                    Modified = directory.LastWriteTime
                });
            }
            catch { }
        }

        foreach (var filePath in Directory.EnumerateFiles(path))
        {
            try
            {
                var file = new FileInfo(filePath);
                items.Add(new LocalFileItem
                {
                    Name = file.Name,
                    FullPath = file.FullName,
                    IsDirectory = false,
                    Size = file.Length,
                    Modified = file.LastWriteTime
                });
            }
            catch { }
        }

        return items
            .OrderByDescending(item => item.IsDirectory)
            .ThenBy(item => item.Name.StartsWith('.'))
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private async Task RefreshRemoteAsync()
    {
        var remote = _remote;
        if (remote == null || !remote.IsConnected)
        {
            AppendMessage("Status", "Connect to a server before browsing remote files.");
            return;
        }

        try
        {
            var path = RemotePath.Normalize(RemotePathTextBox.Text);
            var items = await remote.ListAsync(path, CancellationToken.None);
            RemotePathTextBox.Text = path;
            RemoteItems.Clear();
            foreach (var item in items)
            {
                RemoteItems.Add(item);
            }
            _profile.RemotePath = path;
            _ = SaveProfilePathAsync();
            UpdateRemoteBookmarkState();
            AppendMessage("Status", $"Listed {items.Count} remote item(s) in {path}");
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "RefreshRemote");
            AppendMessage("Error", ex.Message);
        }
    }

    private void UpdateLocalBookmarkState()
    {
        var current = LocalPathTextBox.Text.Trim();
        bool isBookmarked = _localBookmarks.Contains(current, StringComparer.OrdinalIgnoreCase);
        LocalBookmarkToggleButton.IsChecked = isBookmarked;
        LocalBookmarkIcon.Glyph = isBookmarked ? "\uE735" : "\uE734";
    }

    private void UpdateRemoteBookmarkState()
    {
        var current = RemotePath.Normalize(RemotePathTextBox.Text);
        bool isBookmarked = _profile.RemoteBookmarks != null && _profile.RemoteBookmarks.Contains(current, StringComparer.OrdinalIgnoreCase);
        RemoteBookmarkToggleButton.IsChecked = isBookmarked;
        RemoteBookmarkIcon.Glyph = isBookmarked ? "\uE735" : "\uE734";
    }

    private async void LocalBookmarkToggle_Click(object sender, RoutedEventArgs e)
    {
        var current = LocalPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(current)) return;

        if (LocalBookmarkToggleButton.IsChecked == true)
        {
            if (!_localBookmarks.Contains(current, StringComparer.OrdinalIgnoreCase))
            {
                _localBookmarks.Add(current);
                await _localBookmarkService.SaveAsync(_localBookmarks);
            }
        }
        else
        {
            _localBookmarks.RemoveAll(b => string.Equals(b, current, StringComparison.OrdinalIgnoreCase));
            await _localBookmarkService.SaveAsync(_localBookmarks);
        }
        UpdateLocalBookmarkState();
    }

    private async void RemoteBookmarkToggle_Click(object sender, RoutedEventArgs e)
    {
        var current = RemotePath.Normalize(RemotePathTextBox.Text);
        if (string.IsNullOrWhiteSpace(current)) return;

        _profile.RemoteBookmarks ??= new List<string>();

        if (RemoteBookmarkToggleButton.IsChecked == true)
        {
            if (!_profile.RemoteBookmarks.Contains(current, StringComparer.OrdinalIgnoreCase))
            {
                _profile.RemoteBookmarks.Add(current);
                await _siteManager.SaveAsync(_profile);
            }
        }
        else
        {
            _profile.RemoteBookmarks.RemoveAll(b => string.Equals(b, current, StringComparison.OrdinalIgnoreCase));
            await _siteManager.SaveAsync(_profile);
        }
        UpdateRemoteBookmarkState();
    }

    private void LocalBookmarksFlyout_Opening(object sender, object e)
    {
        LocalBookmarksFlyout.Items.Clear();
        if (_localBookmarks.Count == 0)
        {
            LocalBookmarksFlyout.Items.Add(new MenuFlyoutItem { Text = "No local bookmarks", IsEnabled = false });
            return;
        }

        foreach (var path in _localBookmarks.OrderBy(p => p))
        {
            var item = new MenuFlyoutItem
            {
                Text = path,
                Icon = new FontIcon { Glyph = "\uE8B7" }
            };
            var targetPath = path;
            item.Click += async (s, args) =>
            {
                LocalPathTextBox.Text = targetPath;
                await RefreshLocalAsync();
            };
            LocalBookmarksFlyout.Items.Add(item);
        }
    }

    private void RemoteBookmarksFlyout_Opening(object sender, object e)
    {
        RemoteBookmarksFlyout.Items.Clear();
        if (_profile.RemoteBookmarks == null || _profile.RemoteBookmarks.Count == 0)
        {
            RemoteBookmarksFlyout.Items.Add(new MenuFlyoutItem { Text = "No remote bookmarks", IsEnabled = false });
            return;
        }

        foreach (var path in _profile.RemoteBookmarks.OrderBy(p => p))
        {
            var item = new MenuFlyoutItem
            {
                Text = path,
                Icon = new FontIcon { Glyph = "\uE8B7" }
            };
            var targetPath = path;
            item.Click += async (s, args) =>
            {
                RemotePathTextBox.Text = targetPath;
                await RefreshRemoteAsync();
            };
            RemoteBookmarksFlyout.Items.Add(item);
        }
    }

    private async Task SaveProfilePathAsync()
    {
        try
        {
            if (_profile != null && !string.IsNullOrWhiteSpace(_profile.Host))
            {
                await _siteManager.SaveAsync(_profile);
            }
        }
        catch { }
    }

    private async void RefreshLocal_Click(object sender, RoutedEventArgs e) => await RefreshLocalAsync();
    private async void RefreshRemote_Click(object sender, RoutedEventArgs e) => await RefreshRemoteAsync();

    private void RemoteListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RemoteListView.SelectedItem is RemoteFileItem item && !item.IsDirectory && FileIconHelper.IsEditableTextFile(item.Name))
        {
            EditRemoteFileButton.Visibility = Visibility.Visible;
        }
        else
        {
            EditRemoteFileButton.Visibility = Visibility.Collapsed;
        }
    }

    private void EditRemoteFile_Click(object sender, RoutedEventArgs e)
    {
        if (_remote != null && RemoteListView.SelectedItem is RemoteFileItem item && !item.IsDirectory && FileIconHelper.IsEditableTextFile(item.Name))
        {
            EditFileRequested?.Invoke(this, (item, _remote));
        }
    }

    private async void LocalUp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var parent = Directory.GetParent(Path.GetFullPath(LocalPathTextBox.Text));
            if (parent != null)
            {
                LocalPathTextBox.Text = parent.FullName;
                await RefreshLocalAsync();
            }
        }
        catch (Exception ex)
        {
            AppendMessage("Error", ex.Message);
        }
    }

    private async void RemoteUp_Click(object sender, RoutedEventArgs e)
    {
        RemotePathTextBox.Text = RemotePath.Parent(RemotePathTextBox.Text);
        await RefreshRemoteAsync();
    }

    private async void LocalPathTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            await RefreshLocalAsync();
        }
    }

    private async void RemotePathTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            await RefreshRemoteAsync();
        }
    }

    private async void BrowseLocal_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder
            };
            picker.FileTypeFilter.Add("*");
            if (WindowHandle != IntPtr.Zero)
            {
                InitializeWithWindow.Initialize(picker, WindowHandle);
            }
            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                LocalPathTextBox.Text = folder.Path;
                await RefreshLocalAsync();
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "BrowseLocal");
            await ShowErrorAsync("Could not open folder picker", ex.Message);
        }
    }

    private async void LocalListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (LocalListView.SelectedItem is not LocalFileItem item)
        {
            return;
        }

        if (item.IsDirectory)
        {
            LocalPathTextBox.Text = item.FullPath;
            await RefreshLocalAsync();
        }
        else
        {
            await UploadItemsAsync(new[] { item });
        }
    }

    private async void RemoteListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (RemoteListView.SelectedItem is not RemoteFileItem item)
        {
            return;
        }

        if (item.IsDirectory)
        {
            RemotePathTextBox.Text = item.FullPath;
            await RefreshRemoteAsync();
        }
        else if (FileIconHelper.IsEditableTextFile(item.Name) && _remote != null)
        {
            EditFileRequested?.Invoke(this, (item, _remote));
        }
        else
        {
            await DownloadItemsAsync(new[] { item });
        }
    }

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        var items = LocalListView.SelectedItems.OfType<LocalFileItem>().Where(item => !item.IsDirectory).ToArray();
        await UploadItemsAsync(items);
    }

    private async Task UploadItemsAsync(IReadOnlyList<LocalFileItem> items)
    {
        var remote = _remote;
        if (remote == null || !remote.IsConnected)
        {
            await ShowErrorAsync("Not connected", "Connect to a server before uploading files.");
            return;
        }

        if (items.Count == 0)
        {
            AppendMessage("Status", "Select one or more local files to upload.");
            return;
        }

        var remoteDirectory = RemotePath.Normalize(RemotePathTextBox.Text);
        var tasks = items.Select(item =>
            _transferQueue.EnqueueUploadAsync(remote, item.FullPath, RemotePath.Combine(remoteDirectory, item.Name)));
        await Task.WhenAll(tasks);
        await RefreshRemoteAsync();
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        var items = RemoteListView.SelectedItems.OfType<RemoteFileItem>().Where(item => !item.IsDirectory).ToArray();
        await DownloadItemsAsync(items);
    }

    private async Task DownloadItemsAsync(IReadOnlyList<RemoteFileItem> items)
    {
        var remote = _remote;
        if (remote == null || !remote.IsConnected)
        {
            await ShowErrorAsync("Not connected", "Connect to a server before downloading files.");
            return;
        }

        if (items.Count == 0)
        {
            AppendMessage("Status", "Select one or more remote files to download.");
            return;
        }

        var localDirectory = Path.GetFullPath(LocalPathTextBox.Text);
        var tasks = items.Select(item =>
            _transferQueue.EnqueueDownloadAsync(remote, item.FullPath, Path.Combine(localDirectory, item.Name)));
        await Task.WhenAll(tasks);
        await RefreshLocalAsync();
    }

    private async void NewRemoteFolder_Click(object sender, RoutedEventArgs e)
    {
        var remote = _remote;
        if (remote == null || !remote.IsConnected)
        {
            await ShowErrorAsync("Not connected", "Connect to a server first.");
            return;
        }

        var nameBox = new TextBox { Header = "Folder name" };
        var dialog = new ContentDialog
        {
            Title = "Create remote folder",
            Content = nameBox,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(nameBox.Text))
        {
            return;
        }

        try
        {
            var path = RemotePath.Combine(RemotePathTextBox.Text, nameBox.Text.Trim());
            await remote.CreateDirectoryAsync(path, CancellationToken.None);
            AppLogger.LogAction("Remote_Directory_Created", new Dictionary<string, object> { ["path"] = path });
            await RefreshRemoteAsync();
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "CreateRemoteFolder");
            await ShowErrorAsync("Could not create folder", ex.Message);
        }
    }

    private async void RenameRemote_Click(object sender, RoutedEventArgs e)
    {
        var remote = _remote;
        if (remote == null || !remote.IsConnected || RemoteListView.SelectedItem is not RemoteFileItem item)
        {
            AppendMessage("Status", "Select a remote item to rename.");
            return;
        }

        var nameBox = new TextBox
        {
            Header = "New name",
            Text = item.Name,
            SelectionStart = 0,
            SelectionLength = item.Name.Length
        };
        var dialog = new ContentDialog
        {
            Title = "Rename remote item",
            Content = nameBox,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(nameBox.Text))
        {
            return;
        }

        try
        {
            var destination = RemotePath.Combine(RemotePath.Parent(item.FullPath), nameBox.Text.Trim());
            await remote.RenameAsync(item, destination, CancellationToken.None);
            AppLogger.LogAction("Remote_Item_Renamed", new Dictionary<string, object>
            {
                ["source"] = item.FullPath,
                ["destination"] = destination
            });
            await RefreshRemoteAsync();
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "RenameRemoteItem");
            await ShowErrorAsync("Could not rename item", ex.Message);
        }
    }

    private async void DeleteRemote_Click(object sender, RoutedEventArgs e)
    {
        var remote = _remote;
        var items = RemoteListView.SelectedItems.OfType<RemoteFileItem>().ToArray();
        if (remote == null || !remote.IsConnected || items.Length == 0)
        {
            AppendMessage("Status", "Select one or more remote items to delete.");
            return;
        }

        var confirm = new ContentDialog
        {
            Title = "Delete remote items?",
            Content = $"Permanently delete {items.Length} selected item(s)? Folders must be empty.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            foreach (var item in items)
            {
                await remote.DeleteAsync(item, CancellationToken.None);
            }

            AppLogger.LogAction("Remote_Items_Deleted", new Dictionary<string, object> { ["count"] = items.Length });
            await RefreshRemoteAsync();
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "DeleteRemoteItems");
            await ShowErrorAsync("Could not delete item", ex.Message);
        }
    }

    private void CancelTransfer_Click(object sender, RoutedEventArgs e)
    {
        if (TransferListView.SelectedItem is TransferItem item)
        {
            item.Cancel();
        }
    }

    private void ClearCompleted_Click(object sender, RoutedEventArgs e) => _transferQueue.ClearCompleted();

    private void SetConnectionStatus(string text, bool connected)
    {
        ConnectionStatusText.Text = text;
        var brush = new SolidColorBrush(connected
            ? Microsoft.UI.Colors.LimeGreen
            : Microsoft.UI.Colors.Gray);
        ConnectionStatusIcon.Foreground = brush;
        ConnectionHeaderStatusIcon.Foreground = brush;
    }

    private void AppendMessage(string category, string message)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => AppendMessage(category, message));
            return;
        }

        var line = $"{DateTime.Now:HH:mm:ss} [{category}] {message}";
        _messageLines.Enqueue(line);
        while (_messageLines.Count > 300)
        {
            _messageLines.Dequeue();
        }

        var builder = new StringBuilder();
        foreach (var entry in _messageLines)
        {
            builder.AppendLine(entry);
        }
        MessageLogTextBox.Text = builder.ToString();
        MessageLogTextBox.Select(MessageLogTextBox.Text.Length, 0);
    }

    private async Task<ContentDialogResult> ShowSftpHostKeyDialogAsync(HostKeyNotTrustedException exception)
    {
        var dialog = new ContentDialog
        {
            Title = "Trust this SFTP server?",
            Content = $"Server: {exception.Host}:{exception.Port}\nSHA-256 fingerprint:\n{exception.Fingerprint}\n\nVerify this fingerprint with the server administrator before continuing. It will be remembered for future connections.",
            PrimaryButtonText = "Trust and connect",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        return await dialog.ShowAsync();
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "Close",
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async void TransferListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (TransferListView.SelectedItem is TransferItem item)
        {
            await LaunchTransferItemAsync(item);
        }
    }

    private async void OpenTransferFile_Click(object sender, RoutedEventArgs e)
    {
        if (TransferListView.SelectedItem is TransferItem item)
        {
            await LaunchTransferItemAsync(item);
        }
    }

    private async void OpenTransferFolder_Click(object sender, RoutedEventArgs e)
    {
        if (TransferListView.SelectedItem is TransferItem item && !string.IsNullOrWhiteSpace(item.LocalPath))
        {
            try
            {
                var folderPath = Path.GetDirectoryName(item.LocalPath);
                if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
                {
                    var folder = await StorageFolder.GetFolderFromPathAsync(folderPath);
                    await Launcher.LaunchFolderAsync(folder);
                }
                else
                {
                    AppendMessage("Error", $"Folder does not exist: {folderPath}");
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OpenTransferFolder");
                AppendMessage("Error", $"Could not open folder: {ex.Message}");
            }
        }
    }

    private async Task LaunchTransferItemAsync(TransferItem item)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(item.LocalPath))
            {
                return;
            }

            if (File.Exists(item.LocalPath))
            {
                var storageFile = await StorageFile.GetFileFromPathAsync(item.LocalPath);
                if (storageFile != null)
                {
                    await Launcher.LaunchFileAsync(storageFile);
                    AppendMessage("Status", $"Launched file: {item.LocalPath}");
                }
            }
            else
            {
                AppendMessage("Error", $"File does not exist on disk: {item.LocalPath}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "LaunchTransferItem");
            AppendMessage("Error", $"Could not launch file: {ex.Message}");
        }
    }
}
