using Microsoft.UI.Xaml;
using System;

namespace FileFox.Services;

public static class ThemeService
{
    private const string ThemeSettingKey = "AppTheme_Setting";

    public static ElementTheme CurrentTheme
    {
        get
        {
            var value = AppStorageService.GetSetting<string>(ThemeSettingKey);
            if (Enum.TryParse<ElementTheme>(value, out var theme))
            {
                return theme;
            }
            return ElementTheme.Dark; // Default to sleek Dark mode
        }
    }

    public static void ApplyTheme(FrameworkElement element, ElementTheme theme)
    {
        element.RequestedTheme = theme;
        AppStorageService.SetSetting(ThemeSettingKey, theme.ToString());
    }

    public static ElementTheme ToggleTheme(FrameworkElement element)
    {
        var current = element.ActualTheme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
        var next = current == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        ApplyTheme(element, next);
        return next;
    }
}
