using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FileCompare.App.Theming;

/// <summary>
/// Fills one or more menus with every theme from <see cref="ThemeCatalog"/> (with a small color preview),
/// applies a theme when clicked and keeps the check mark on the current one.
/// </summary>
public sealed class ThemeMenus : IDisposable
{
    private readonly List<(AppTheme Theme, MenuItem Item)> _items = new();
    private readonly EventHandler _onThemeChanged;

    public ThemeMenus(params ItemsControl[] menus)
    {
        foreach (var menu in menus)
        {
            foreach (var theme in ThemeCatalog.All)
            {
                var item = CreateItem(theme);
                menu.Items.Add(item);
                _items.Add((theme, item));
            }
        }
        _onThemeChanged = (_, _) => Refresh();
        ThemeService.Changed += _onThemeChanged;
        Refresh();
    }

    /// <summary>Stops following theme changes (call when the menus' page is closed).</summary>
    public void Dispose() => ThemeService.Changed -= _onThemeChanged;

    public void Refresh()
    {
        foreach (var (theme, item) in _items)
            item.IsChecked = theme.Id == ThemeService.Current.Id;
    }

    private MenuItem CreateItem(AppTheme theme)
    {
        var item = new MenuItem { Header = theme.Name, IsCheckable = true, Icon = CreateSwatch(theme) };
        item.Click += (_, _) =>
        {
            ThemeService.SetTheme(theme);
            Refresh(); // clicking the current theme must not leave it unchecked
        };
        return item;
    }

    /// <summary>A tiny "Aa" box in the theme's editor colors.</summary>
    private static Border CreateSwatch(AppTheme theme) => new()
    {
        Width = 22,
        Height = 16,
        CornerRadius = new CornerRadius(3),
        BorderThickness = new Thickness(1),
        BorderBrush = new SolidColorBrush(theme.Chrome?.Border ?? Colors.Gray),
        Background = new SolidColorBrush(theme.Diff.EditorBackground),
        Child = new TextBlock
        {
            Text = "Aa",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(theme.Diff.EditorText),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        }
    };
}
