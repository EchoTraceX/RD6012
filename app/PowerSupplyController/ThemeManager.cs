using System.Windows;
using System.Windows.Media;

namespace PowerSupplyController;

public static class ThemeManager
{
    private static readonly IReadOnlyDictionary<string, string> DarkThemeColors = new Dictionary<string, string>
    {
        ["BgBrush"] = "#10151D",
        ["PanelBrush"] = "#161D27",
        ["BorderBrushStrong"] = "#27303D",
        ["TitleBrush"] = "#F4F8FB",
        ["TextBrush"] = "#D6DEE6",
        ["MutedBrush"] = "#8A98A8",
        ["AccentBrush"] = "#3DC3D8",
        ["SuccessBrush"] = "#4FD28E",
        ["WarnBrush"] = "#FFB347",
        ["DangerBrush"] = "#FF6B6B",
        ["ThemeFieldBrush"] = "#121923",
        ["ThemePanelBrush"] = "#161D27",
        ["ThemePanelSoftBrush"] = "#0F151D",
        ["ThemeBorderBrush"] = "#2A3647",
        ["ThemeBorderStrongBrush"] = "#27303D",
        ["ThemeTextBrush"] = "#F4F8FB",
        ["ThemeMutedBrush"] = "#7D8A98",
        ["ThemeAccentBrush"] = "#3DC3D8",
        ["ThemeAccentDeepBrush"] = "#1E5E6B",
        ["ThemeScrollTrackBrush"] = "#0E141C",
        ["ThemeScrollThumbBrush"] = "#324255",
        ["ThemeScrollThumbHoverBrush"] = "#45607B",
        ["ThemeWindowTitleBarBrush"] = "#0E141C",
        ["ThemeWindowTitleBarBorderBrush"] = "#1D2633",
        ["ThemeTitleBarButtonBrush"] = "#16202B",
        ["ThemeTitleBarButtonBorderBrush"] = "#253244",
        ["ThemeTitleBarButtonHoverBrush"] = "#1F2D3D",
        ["ThemeCloseButtonBrush"] = "#24161A",
        ["ThemeCloseButtonBorderBrush"] = "#49343A",
        ["ThemeCloseButtonHoverBrush"] = "#8B2C3A",
        ["ThemeStatusBadgeBrush"] = "#131D28",
        ["ThemeStatusBadgeBorderBrush"] = "#253244",
        ["ThemeInfoCardBrush"] = "#121B24",
        ["ThemeInfoCardBorderBrush"] = "#263243",
        ["ThemeChartBrush"] = "#0F151D",
        ["ThemeChartBorderBrush"] = "#243040",
        ["ThemeHeaderIconOuterBrush"] = "#13212C",
        ["ThemeHeaderIconOuterBorderBrush"] = "#254055",
        ["ThemeHeaderIconInnerBrush"] = "#22414C",
        ["ThemePrimaryButtonBrush"] = "#1E5E6B",
        ["ThemePrimaryButtonBorderBrush"] = "#2B7A8A",
        ["ThemePrimaryButtonForegroundBrush"] = "#FFFFFF",
        ["ThemeNeutralButtonBrush"] = "#1A2330",
        ["ThemeNeutralButtonBorderBrush"] = "#2A3546",
        ["ThemePositiveButtonBrush"] = "#1D5D47",
        ["ThemePositiveButtonBorderBrush"] = "#2E886A",
        ["ThemeWarningButtonBrush"] = "#5F4A1A",
        ["ThemeWarningButtonBorderBrush"] = "#8E6A22",
        ["ThemeDestructiveButtonBrush"] = "#6E2D36",
        ["ThemeDestructiveButtonBorderBrush"] = "#9B414D",
        ["ThemeTabBrush"] = "#121923",
        ["ThemeTabBorderBrush"] = "#27303D"
    };

    private static readonly IReadOnlyDictionary<string, string> LightThemeColors = new Dictionary<string, string>
    {
        ["BgBrush"] = "#F5F7FA",
        ["PanelBrush"] = "#FFFFFF",
        ["BorderBrushStrong"] = "#E1E4E8",
        ["TitleBrush"] = "#1F2937",
        ["TextBrush"] = "#374151",
        ["MutedBrush"] = "#6B7280",
        ["AccentBrush"] = "#0891B2",
        ["SuccessBrush"] = "#059669",
        ["WarnBrush"] = "#D97706",
        ["DangerBrush"] = "#DC2626",
        ["ThemeFieldBrush"] = "#FFFFFF",
        ["ThemePanelBrush"] = "#FFFFFF",
        ["ThemePanelSoftBrush"] = "#EEF2F7",
        ["ThemeBorderBrush"] = "#D0D7DE",
        ["ThemeBorderStrongBrush"] = "#E1E4E8",
        ["ThemeTextBrush"] = "#1F2937",
        ["ThemeMutedBrush"] = "#6B7280",
        ["ThemeAccentBrush"] = "#0891B2",
        ["ThemeAccentDeepBrush"] = "#D5EEF4",
        ["ThemeScrollTrackBrush"] = "#E5E7EB",
        ["ThemeScrollThumbBrush"] = "#B9C2CF",
        ["ThemeScrollThumbHoverBrush"] = "#94A3B8",
        ["ThemeWindowTitleBarBrush"] = "#F3F6FA",
        ["ThemeWindowTitleBarBorderBrush"] = "#D8E0EA",
        ["ThemeTitleBarButtonBrush"] = "#FFFFFF",
        ["ThemeTitleBarButtonBorderBrush"] = "#CBD5E1",
        ["ThemeTitleBarButtonHoverBrush"] = "#E2E8F0",
        ["ThemeCloseButtonBrush"] = "#FFF1F2",
        ["ThemeCloseButtonBorderBrush"] = "#F1B5BF",
        ["ThemeCloseButtonHoverBrush"] = "#E11D48",
        ["ThemeStatusBadgeBrush"] = "#F8FAFC",
        ["ThemeStatusBadgeBorderBrush"] = "#CBD5E1",
        ["ThemeInfoCardBrush"] = "#F8FAFC",
        ["ThemeInfoCardBorderBrush"] = "#D8E0EA",
        ["ThemeChartBrush"] = "#F8FAFC",
        ["ThemeChartBorderBrush"] = "#D8E0EA",
        ["ThemeHeaderIconOuterBrush"] = "#E0F2FE",
        ["ThemeHeaderIconOuterBorderBrush"] = "#7DD3FC",
        ["ThemeHeaderIconInnerBrush"] = "#BAE6FD",
        ["ThemePrimaryButtonBrush"] = "#D6EDF4",
        ["ThemePrimaryButtonBorderBrush"] = "#8CC3D2",
        ["ThemePrimaryButtonForegroundBrush"] = "#145266",
        ["ThemeNeutralButtonBrush"] = "#FFFFFF",
        ["ThemeNeutralButtonBorderBrush"] = "#CBD5E1",
        ["ThemePositiveButtonBrush"] = "#DDEFE7",
        ["ThemePositiveButtonBorderBrush"] = "#9FC3B0",
        ["ThemeWarningButtonBrush"] = "#F4E7C8",
        ["ThemeWarningButtonBorderBrush"] = "#D7B464",
        ["ThemeDestructiveButtonBrush"] = "#F7DCE1",
        ["ThemeDestructiveButtonBorderBrush"] = "#D7A0AB",
        ["ThemeTabBrush"] = "#FFFFFF",
        ["ThemeTabBorderBrush"] = "#D0D7DE"
    };

    public static void InitializeTheme()
    {
        // 初始化深色主题
        ApplyDarkTheme();
    }

    public static void ApplyDarkTheme()
    {
        ApplyTheme(DarkThemeColors);
    }

    public static void ApplyLightTheme()
    {
        ApplyTheme(LightThemeColors);
    }

    private static void ApplyTheme(IReadOnlyDictionary<string, string> themeColors)
    {
        var resources = Application.Current.Resources;
        foreach (var entry in themeColors)
        {
            SetOrAddBrush(resources, entry.Key, entry.Value);
        }
    }

    private static void SetOrAddBrush(ResourceDictionary resources, string key, string colorValue)
    {
        var color = (Color)ColorConverter.ConvertFromString(colorValue);

        if (resources.Contains(key))
        {
            if (resources[key] is SolidColorBrush existingBrush && !existingBrush.IsFrozen)
            {
                existingBrush.Color = color;
                return;
            }
        }

        resources[key] = new SolidColorBrush(color);
    }
}
