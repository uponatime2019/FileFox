using FileFox.Helpers;
using FileFox.Models;
using FileFox.Services;
using FileFox.Views;
using H.NotifyIcon;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Windows.Graphics;
using Windows.Storage;
using WinRT.Interop;

namespace FileFox;

public sealed partial class MainWindow : Window
{
    private const string CloseToTraySetting = "CloseToTray";
    private const nuint SubclassId = 101;

    private readonly SiteManagerService _siteManager = new();
    private readonly StartupManager _startupManager = new();
    private TaskbarIcon? _taskbarIcon;
    private Win32Window.SubclassProc? _subclassProc;
    private uint _restoreMessageId;
    private IntPtr _windowHandle;
    private bool _isExiting;
    private bool _closeToTray = true;

    public MainWindow()
    {
        TrayOpenCommand = new SimpleCommand(RestoreAndActivateWindow);
        TrayExitCommand = new SimpleCommand(RequestExit);

        InitializeComponent();

        Title = App.WindowTitle;
        LoadWindowSettings();
        ConfigureWindow();
        InitializeSubclassing();
        InitializeTrayIcon();

        RootGrid.Loaded += RootGrid_Loaded;
        AppWindow.Closing += AppWindow_Closing;
    }

    public ICommand TrayOpenCommand { get; }
    public ICommand TrayExitCommand { get; }

    public void HideToTray()
    {
        try
        {
            AppWindow.Hide();
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "HideToTray");
        }
    }

    public void RestoreAndActivateWindow()
    {
        try
        {
            AppWindow.Show();
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
            Activate();
            if (_windowHandle != IntPtr.Zero)
            {
                Win32Window.ShowWindow(_windowHandle, Win32Window.SwMaximize);
                Win32Window.ShowWindow(_windowHandle, Win32Window.SwShow);
                Win32Window.SetForegroundWindow(_windowHandle);
            }

            AppLogger.LogAction("Window_Restored");
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "RestoreAndActivateWindow");
        }
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= RootGrid_Loaded;
        ThemeService.ApplyTheme(RootGrid, ThemeService.CurrentTheme);
        DashboardViewControl.UpdateThemeButtonState(ThemeService.CurrentTheme);
        AppLogger.LogAction("MainWindow_Loaded");
    }

    private void ConfigureWindow()
    {
        try
        {
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "ConfigureWindow");
        }
    }

    private void LoadWindowSettings()
    {
        try
        {
            var value = ApplicationData.Current.LocalSettings.Values[CloseToTraySetting];
            _closeToTray = value is not bool enabled || enabled;
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "LoadWindowSettings");
        }
    }

    private async void MainTabView_AddTabButtonClick(TabView sender, object args)
    {
        try
        {
            var savedSites = await _siteManager.LoadAsync();
            if (savedSites.Count == 0)
            {
                MainTabView.SelectedItem = DashboardTab;
                var dialog = new ContentDialog
                {
                    Title = "No Saved Connections",
                    Content = "You do not have any saved connection profiles yet. Please create and save a connection profile on the Dashboard first.",
                    CloseButtonText = "OK",
                    XamlRoot = RootGrid.XamlRoot
                };
                await dialog.ShowAsync();
                return;
            }

            var menu = new MenuFlyout();

            var newConnectionItem = new MenuFlyoutItem
            {
                Text = "Dashboard (New connection)",
                Icon = new FontIcon { Glyph = "\uF246" }
            };
            newConnectionItem.Click += (_, _) =>
            {
                MainTabView.SelectedItem = DashboardTab;
            };
            menu.Items.Add(newConnectionItem);
            menu.Items.Add(new MenuFlyoutSeparator());

            foreach (var site in savedSites)
            {
                var siteItem = new MenuFlyoutItem
                {
                    Text = $"{site.DisplayName} ({site.Protocol} • {site.Host}:{site.Port})",
                    Icon = new FontIcon { Glyph = "\uE8AF" }
                };
                var capturedSite = site;
                siteItem.Click += (_, _) =>
                {
                    OpenConnectionTab(capturedSite);
                };
                menu.Items.Add(siteItem);
            }

            menu.ShowAt(sender, new FlyoutShowOptions
            {
                Placement = FlyoutPlacementMode.BottomEdgeAlignedRight
            });
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "AddTabButtonClick");
            MainTabView.SelectedItem = DashboardTab;
        }
    }

    private async void MainTabView_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Item is TabViewItem tabItem)
        {
            if (tabItem.Content is ConnectionSessionView sessionView)
            {
                await sessionView.DisconnectInternalAsync(false);
            }
            sender.TabItems.Remove(tabItem);
        }
    }

    private void DashboardView_ConnectRequested(object? sender, ConnectionProfile profile)
    {
        OpenConnectionTab(profile);
    }

    private void OpenConnectionTab(ConnectionProfile profile)
    {
        var sessionView = new ConnectionSessionView(profile, _windowHandle);

        string tabTitle = string.IsNullOrWhiteSpace(profile.Name) || string.Equals(profile.Name, profile.Host, StringComparison.OrdinalIgnoreCase)
            ? profile.Host
            : $"{profile.Name} ({profile.Host})";

        IconSource iconSource;
        try
        {
            var faviconUrl = $"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(profile.Host)}&sz=32";
            iconSource = new BitmapIconSource
            {
                UriSource = new Uri(faviconUrl),
                ShowAsMonochrome = false
            };
        }
        catch
        {
            iconSource = new FontIconSource { Glyph = "\uE8AF" };
        }

        var tabItem = new TabViewItem
        {
            Header = tabTitle,
            Content = sessionView,
            IsClosable = true,
            IconSource = iconSource
        };

        sessionView.TitleChanged += (s, newTitle) =>
        {
            tabItem.Header = newTitle;
        };

        sessionView.EditFileRequested += async (s, args) =>
        {
            await OpenFileEditorTabAsync(args.FileItem, args.RemoteSystem);
        };

        MainTabView.TabItems.Add(tabItem);
        MainTabView.SelectedItem = tabItem;
    }

    private async Task OpenFileEditorTabAsync(RemoteFileItem fileItem, IRemoteFileSystem remote)
    {
        try
        {
            var tempDir = Path.Combine(ApplicationData.Current.TemporaryFolder.Path, "FileFoxEditors");
            Directory.CreateDirectory(tempDir);
            var tempFilePath = Path.Combine(tempDir, $"{Guid.NewGuid():N}_{fileItem.Name}");

            var progress = new Progress<TransferProgressInfo>();
            await remote.DownloadFileAsync(fileItem.FullPath, tempFilePath, progress, CancellationToken.None);

            var editorView = new FileEditorView(tempFilePath, fileItem.FullPath, remote);
            var tabItem = new TabViewItem
            {
                Header = fileItem.Name,
                Content = editorView,
                IsClosable = true,
                IconSource = new FontIconSource { Glyph = "\uE70F" }
            };

            editorView.CloseRequested += (_, _) =>
            {
                MainTabView.TabItems.Remove(tabItem);
                try
                {
                    if (File.Exists(tempFilePath))
                    {
                        File.Delete(tempFilePath);
                    }
                }
                catch { }
            };

            MainTabView.TabItems.Add(tabItem);
            MainTabView.SelectedItem = tabItem;
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "OpenFileEditorTab");
            await ShowErrorAsync("Error opening file", ex.Message);
        }
    }

    private async void DashboardView_DeleteSiteRequested(object? sender, ConnectionProfile profile)
    {
        try
        {
            await _siteManager.DeleteAsync(profile);
            await DashboardViewControl.LoadSavedSitesAsync();
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "DeleteSiteRequested");
            await ShowErrorAsync("Could not delete site", ex.Message);
        }
    }

    private void DashboardView_ThemeToggleRequested(object? sender, EventArgs e)
    {
        var newTheme = ThemeService.ToggleTheme(RootGrid);
        DashboardViewControl.UpdateThemeButtonState(newTheme);
    }

    private void RequestExit() => _ = ExitApplicationAsync();

    private async Task ExitApplicationAsync()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;

        foreach (var item in MainTabView.TabItems.OfType<TabViewItem>())
        {
            if (item.Content is ConnectionSessionView sessionView)
            {
                await sessionView.DisconnectInternalAsync(false);
            }
        }

        DisposeSubclassing();
        try
        {
            _taskbarIcon?.Dispose();
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "TrayIcon_Dispose");
        }

        AppLogger.LogAction("Application_Exit");
        Close();
        Application.Current.Exit();
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isExiting)
        {
            return;
        }

        if (_closeToTray)
        {
            args.Cancel = true;
            sender.Hide();
            AppLogger.LogAction("Window_Hidden_To_Tray");
            return;
        }

        _isExiting = true;
        foreach (var item in MainTabView.TabItems.OfType<TabViewItem>())
        {
            if (item.Content is ConnectionSessionView sessionView)
            {
                _ = sessionView.DisconnectInternalAsync(false);
            }
        }
        DisposeSubclassing();
        try
        {
            _taskbarIcon?.Dispose();
        }
        catch
        {
        }
    }

    private void InitializeTrayIcon()
    {
        try
        {
            _taskbarIcon = new TaskbarIcon
            {
                ToolTipText = App.AppName,
                ContextMenuMode = ContextMenuMode.SecondWindow,
                IconSource = new BitmapImage(new Uri("ms-appx:///Assets/AppIcon.ico")),
                LeftClickCommand = TrayOpenCommand,
                NoLeftClickDelay = true
            };

            var menu = new MenuFlyout { AreOpenCloseAnimationsEnabled = false };
            var openItem = new MenuFlyoutItem
            {
                Text = $"Open {App.AppName}",
                FontFamily = new FontFamily("Segoe UI"),
                Icon = new FontIcon { Glyph = "\uE8A7" }
            };
            openItem.Click += (_, _) => RestoreAndActivateWindow();
            menu.Items.Add(openItem);

            var exitItem = new MenuFlyoutItem
            {
                Text = "Exit",
                FontFamily = new FontFamily("Segoe UI"),
                Icon = new FontIcon { Glyph = "\uE711" }
            };
            exitItem.Click += (_, _) => RequestExit();
            menu.Items.Add(exitItem);

            _taskbarIcon.ContextFlyout = menu;
            RootGrid.Children.Add(_taskbarIcon);
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "TrayIcon_Initialize");
        }
    }

    private void InitializeSubclassing()
    {
        try
        {
            _windowHandle = WindowNative.GetWindowHandle(this);
            _restoreMessageId = Win32Window.RegisterWindowMessage(App.RestoreMessageName);
            if (_windowHandle == IntPtr.Zero || _restoreMessageId == 0)
            {
                return;
            }

            _subclassProc = WindowSubclassCallback;
            Win32Window.SetWindowSubclass(_windowHandle, _subclassProc, SubclassId, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "WindowSubclass_Initialize");
        }
    }

    private IntPtr WindowSubclassCallback(
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        nuint subclassId,
        IntPtr referenceData)
    {
        if (_restoreMessageId != 0 && message == _restoreMessageId)
        {
            DispatcherQueue.TryEnqueue(RestoreAndActivateWindow);
            return IntPtr.Zero;
        }

        return Win32Window.DefSubclassProc(hWnd, message, wParam, lParam);
    }

    private void DisposeSubclassing()
    {
        try
        {
            if (_subclassProc != null && _windowHandle != IntPtr.Zero)
            {
                Win32Window.RemoveWindowSubclass(_windowHandle, _subclassProc, SubclassId);
                _subclassProc = null;
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "WindowSubclass_Dispose");
        }
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "Close",
            XamlRoot = RootGrid.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
