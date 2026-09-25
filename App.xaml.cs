using FileFox.Helpers;
using FileFox.Services;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FileFox;

public partial class App : Application
{
    public const string AppName = "FileFox FTP Client";
    public const string WindowTitle = "FileFox FTP Client";
    public const string RestoreMessageName = "FileFox_Restore_9D3C47A1";

    private const string MutexName = @"Local\FileFox_SingleInstance_9D3C47A1";
    private static Mutex? _mutex;
    private Window? _window;

    public App()
    {
        InitializeComponent();

        AppLogger.Initialize();
        UnhandledException += App_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        bool createdNew;
        _mutex = new Mutex(true, MutexName, out createdNew);
        if (!createdNew)
        {
            AppLogger.LogAction("Secondary_Instance_Detected");
            RestoreExistingInstance();
            Current.Exit();
            return;
        }

        var startupManager = new StartupManager();
        startupManager.SyncFromSettings();

        var mainWindow = new MainWindow();
        _window = mainWindow;
        mainWindow.Activate();

        var commandArguments = Environment.GetCommandLineArgs();
        var startedWithWindows = commandArguments.Any(argument =>
            string.Equals(argument, "--startup", StringComparison.OrdinalIgnoreCase));
        if (startedWithWindows)
        {
            mainWindow.HideToTray();
            AppLogger.LogAction("Startup_Launch_Hidden");
        }
        else
        {
            AppLogger.LogAction("Application_Launched", new Dictionary<string, object>
            {
                ["arguments"] = commandArguments
            });
        }
    }

    private static void RestoreExistingInstance()
    {
        try
        {
            var windowHandle = Win32Window.FindWindow(null, WindowTitle);
            if (windowHandle == IntPtr.Zero)
            {
                return;
            }

            var message = Win32Window.RegisterWindowMessage(RestoreMessageName);
            if (message != 0)
            {
                Win32Window.PostMessage(windowHandle, message, IntPtr.Zero, IntPtr.Zero);
            }

            Win32Window.ShowWindow(windowHandle, Win32Window.SwMaximize);
            Win32Window.ShowWindow(windowHandle, Win32Window.SwShow);
            Win32Window.SetForegroundWindow(windowHandle);
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "RestoreExistingInstance");
        }
    }

    private static void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        AppLogger.LogException(e.Exception, "App.Current.UnhandledException");
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLogger.LogException(e.Exception, "TaskScheduler.UnobservedTaskException");
        e.SetObserved();
    }
}
