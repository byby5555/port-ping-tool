using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PortPingTool;

/// <summary>
/// Apple-style design tokens. Centralized so the whole app feels cohesive.
/// Inspired by macOS Sonoma / iOS 17 system colors.
/// </summary>
public static class AppleTheme
{
    // System gray ramp (SF Pro / iOS style)
    public static readonly Color BgWindow      = Color.FromRgb(0xF5, 0xF5, 0xF7);  // window background
    public static readonly Color BgPanel       = Color.FromRgb(0xFF, 0xFF, 0xFF);  // card background
    public static readonly Color BgPanelAlt    = Color.FromRgb(0xFA, 0xFA, 0xFC);  // alternate card / row
    public static readonly Color Border        = Color.FromRgb(0xE5, 0xE5, 0xEA);  // 1px hairline
    public static readonly Color Divider       = Color.FromRgb(0xEC, 0xEC, 0xEF);

    public static readonly Color TextPrimary   = Color.FromRgb(0x1D, 0x1D, 0x1F);
    public static readonly Color TextSecondary = Color.FromRgb(0x6E, 0x6E, 0x73);
    public static readonly Color TextTertiary  = Color.FromRgb(0x8E, 0x8E, 0x93);

    // Accent (Apple system blue)
    public static readonly Color Accent        = Color.FromRgb(0x00, 0x7A, 0xFF);
    public static readonly Color AccentHover   = Color.FromRgb(0x00, 0x6F, 0xE6);

    // Status colors (iOS system colors)
    public static readonly Color Success       = Color.FromRgb(0x34, 0xC7, 0x59);  // green
    public static readonly Color Warning       = Color.FromRgb(0xFF, 0x95, 0x00);  // orange
    public static readonly Color Danger        = Color.FromRgb(0xFF, 0x3B, 0x30);  // red
    public static readonly Color Info          = Color.FromRgb(0x5A, 0xC8, 0xFA);  // light blue

    // Brushes (pre-built for XAML convenience)
    public static SolidColorBrush BgWindowBrush      => new(BgWindow);
    public static SolidColorBrush BgPanelBrush       => new(BgPanel);
    public static SolidColorBrush BgPanelAltBrush    => new(BgPanelAlt);
    public static SolidColorBrush BorderBrush        => new(Border);
    public static SolidColorBrush DividerBrush       => new(Divider);
    public static SolidColorBrush TextPrimaryBrush   => new(TextPrimary);
    public static SolidColorBrush TextSecondaryBrush => new(TextSecondary);
    public static SolidColorBrush TextTertiaryBrush  => new(TextTertiary);
    public static SolidColorBrush AccentBrush        => new(Accent);
    public static SolidColorBrush AccentHoverBrush   => new(AccentHover);
    public static SolidColorBrush SuccessBrush       => new(Success);
    public static SolidColorBrush WarningBrush       => new(Warning);
    public static SolidColorBrush DangerBrush        => new(Danger);
    public static SolidColorBrush InfoBrush          => new(Info);

    /// <summary>SF Pro / system stack — Windows 10/11 falls back to Segoe UI Variable automatically.</summary>
    public const string FontFamily = "Segoe UI Variable, Segoe UI, -apple-system, BlinkMacSystemFont, Helvetica Neue";

    public const double RadiusSmall  = 6;
    public const double RadiusMedium = 10;
    public const double RadiusLarge  = 14;

    /// <summary>Returns a card style used for grouped panels.</summary>
    public static Style CardBorderStyle() => new(typeof(Border))
    {
        Setters =
        {
            new Setter(Border.BackgroundProperty, BgPanelBrush),
            new Setter(Border.CornerRadiusProperty, new CornerRadius(RadiusLarge)),
            new Setter(Border.BorderBrushProperty, BorderBrush),
            new Setter(Border.BorderThicknessProperty, new Thickness(1)),
            new Setter(Border.PaddingProperty, new Thickness(16)),
        }
    };
}
