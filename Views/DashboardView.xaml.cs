using FileFox.Models;
using FileFox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace FileFox.Views;

public sealed partial class DashboardView : UserControl
{
    private readonly SiteManagerService _siteManager = new();
    private readonly StartupManager _startupManager = new();
    private bool _suppressProtocolChange;

    public event EventHandler<ConnectionProfile>? ConnectRequested;
    public event EventHandler<ConnectionProfile>? DeleteSiteRequested;
    public event EventHandler? ThemeToggleRequested;

    public ObservableCollection<ConnectionProfile> SavedSites { get; } = new();
    public ObservableCollection<ConnectionProfile> FilteredSavedSites { get; } = new();

    public DashboardView()
    {
        InitializeComponent();
        Loaded += DashboardView_Loaded;
    }

    private async void DashboardView_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadSavedSitesAsync();
        InitializeSettingsUI();
    }

    private void InitializeSettingsUI()
    {
        try
        {
            StartWithWindowsToggle.IsOn = _startupManager.SavedPreference;
            var closeToTray = AppStorageService.GetSetting<bool>("CloseToTray", true);
            CloseToTrayToggle.IsOn = closeToTray;
            UpdateThemeButtonState(ThemeService.CurrentTheme);
        }
        catch { }
    }

    public void UpdateThemeButtonState(ElementTheme theme)
    {
        if (theme == ElementTheme.Dark)
        {
            ThemeToggleIcon.Glyph = "\uE708";
            ThemeToggleText.Text = "Dark Mode";
        }
        else
        {
            ThemeToggleIcon.Glyph = "\uE706";
            ThemeToggleText.Text = "Light Mode";
        }
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        ThemeToggleRequested?.Invoke(this, EventArgs.Empty);
    }

    private void StartWithWindowsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _startupManager.SetAutoStart(StartWithWindowsToggle.IsOn);
    }

    private void CloseToTrayToggle_Toggled(object sender, RoutedEventArgs e)
    {
        AppStorageService.SetSetting("CloseToTray", CloseToTrayToggle.IsOn);
    }

    private void ToggleSettingsExpand_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsBodyGrid.Visibility == Visibility.Visible)
        {
            SettingsBodyGrid.Visibility = Visibility.Collapsed;
            ToggleSettingsExpandIcon.Glyph = "\uE70D";
        }
        else
        {
            SettingsBodyGrid.Visibility = Visibility.Visible;
            ToggleSettingsExpandIcon.Glyph = "\uE70E";
        }
    }

    public async Task LoadSavedSitesAsync()
    {
        var sites = await _siteManager.LoadAsync();
        SavedSites.Clear();
        foreach (var site in sites)
        {
            SavedSites.Add(site);
        }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredSavedSites.Clear();
        var query = SearchBox?.Text?.Trim() ?? string.Empty;
        var matching = string.IsNullOrWhiteSpace(query)
            ? SavedSites
            : SavedSites.Where(s =>
                s.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.Host.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.Username.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var site in matching)
        {
            FilteredSavedSites.Add(site);
        }

        if (EmptySitesTextBlock != null)
        {
            EmptySitesTextBlock.Visibility = FilteredSavedSites.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public ConnectionProfile BuildProfileFromInputs()
    {
        var selectedSite = SiteComboBox.SelectedItem as ConnectionProfile;
        var protocol = SelectedProtocol();
        var host = HostTextBox.Text.Trim();
        var remotePath = selectedSite?.RemotePath ?? "/";

        if (Uri.TryCreate(host, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            host = uri.Host;
            remotePath = Uri.UnescapeDataString(uri.AbsolutePath);
            protocol = uri.Scheme.ToLowerInvariant() switch
            {
                "sftp" => ConnectionProtocol.Sftp,
                "ftps" => ConnectionProtocol.FtpsExplicit,
                _ => protocol
            };

            if (!uri.IsDefaultPort)
            {
                PortTextBox.Text = uri.Port.ToString();
            }
            else
            {
                PortTextBox.Text = ConnectionProfile.DefaultPort(protocol).ToString();
            }
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("Please enter a server host name.");
        }

        if (!int.TryParse(PortTextBox.Text, out var port) || port is < 1 or > 65535)
        {
            throw new InvalidOperationException("Please enter a valid port number (1-65535).");
        }

        var username = UsernameTextBox.Text.Trim();
        var password = PasswordInput.Password;
        if (protocol == ConnectionProtocol.Sftp && string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("SFTP requires a username.");
        }

        if (protocol != ConnectionProtocol.Sftp && string.IsNullOrWhiteSpace(username))
        {
            username = "anonymous";
            if (string.IsNullOrEmpty(password))
            {
                password = "anonymous@";
            }
        }

        return new ConnectionProfile
        {
            Id = selectedSite?.Id ?? Guid.NewGuid(),
            Name = selectedSite?.Name ?? (string.IsNullOrWhiteSpace(host) ? "New Connection" : host),
            Protocol = protocol,
            Host = host,
            Port = port,
            Username = username,
            Password = password,
            RemotePath = remotePath,
            LocalPath = selectedSite?.LocalPath ?? string.Empty
        };
    }

    private ConnectionProtocol SelectedProtocol()
    {
        if (ProtocolComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            Enum.TryParse<ConnectionProtocol>(tag, out var protocol))
        {
            return protocol;
        }
        return ConnectionProtocol.Ftp;
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = BuildProfileFromInputs();
            var existing = SavedSites.FirstOrDefault(s => s.Id == profile.Id);
            if (existing == null)
            {
                var nameBox = new TextBox
                {
                    Header = "Site Name",
                    Text = string.IsNullOrWhiteSpace(profile.Name) ? profile.Host : profile.Name,
                    SelectionStart = 0,
                    SelectionLength = string.IsNullOrWhiteSpace(profile.Name) ? profile.Host.Length : profile.Name.Length
                };
                var dialog = new ContentDialog
                {
                    Title = "Save Connection Profile",
                    Content = nameBox,
                    PrimaryButtonText = "Save & Connect",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = XamlRoot
                };

                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                {
                    return;
                }

                profile.Name = string.IsNullOrWhiteSpace(nameBox.Text) ? profile.Host : nameBox.Text.Trim();
            }

            await _siteManager.SaveAsync(profile);
            await LoadSavedSitesAsync();

            ConnectRequested?.Invoke(this, profile);
        }
        catch (Exception ex)
        {
            ShowErrorDialog("Connection validation failed", ex.Message);
        }
    }

    private void ConnectSite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ConnectionProfile profile)
        {
            ConnectRequested?.Invoke(this, profile);
        }
    }

    private void EditSite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ConnectionProfile site)
        {
            PopulateFormFromSite(site);
        }
    }

    private void FillSite_Click(object sender, RoutedEventArgs e) => EditSite_Click(sender, e);

    private void PopulateFormFromSite(ConnectionProfile site)
    {
        SiteComboBox.SelectedItem = SavedSites.FirstOrDefault(s => s.Id == site.Id);
        _suppressProtocolChange = true;
        try
        {
            ProtocolComboBox.SelectedIndex = site.Protocol switch
            {
                ConnectionProtocol.FtpsExplicit => 1,
                ConnectionProtocol.FtpsImplicit => 2,
                ConnectionProtocol.Sftp => 3,
                _ => 0
            };
            HostTextBox.Text = site.Host;
            PortTextBox.Text = site.Port.ToString();
            UsernameTextBox.Text = site.Username;
            PasswordInput.Password = site.Password ?? string.Empty;
        }
        finally
        {
            _suppressProtocolChange = false;
        }
    }

    private async void DeleteSite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ConnectionProfile site)
        {
            var confirm = new ContentDialog
            {
                Title = "Delete saved connection?",
                Content = $"Are you sure you want to delete saved connection '{site.DisplayName}'?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            if (await confirm.ShowAsync() == ContentDialogResult.Primary)
            {
                DeleteSiteRequested?.Invoke(this, site);
            }
        }
    }

    private void ClearForm_Click(object sender, RoutedEventArgs e)
    {
        SiteComboBox.SelectedItem = null;
        HostTextBox.Text = string.Empty;
        PortTextBox.Text = "21";
        UsernameTextBox.Text = string.Empty;
        PasswordInput.Password = string.Empty;
        ProtocolComboBox.SelectedIndex = 0;
    }

    private void SiteComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SiteComboBox.SelectedItem is ConnectionProfile site)
        {
            PopulateFormFromSite(site);
        }
    }

    private void ProtocolComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressProtocolChange && PortTextBox != null)
        {
            PortTextBox.Text = ConnectionProfile.DefaultPort(SelectedProtocol()).ToString();
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void SavedSitesListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (SavedSitesListView.SelectedItem is ConnectionProfile site)
        {
            ConnectRequested?.Invoke(this, site);
        }
    }



    private async void ShowErrorDialog(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }
}
