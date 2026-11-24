using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileCompare.App.Folders;
using FileCompare.App.Images;
using FileCompare.App.Tabs;
using FileCompare.App.Theming;
using FileCompare.Core.Images;
using FileCompare.Core.Sessions;
using FileCompare.Core.Text;
using Microsoft.Win32;

namespace FileCompare.App;

/// <summary>Image compare tab: two pictures side by side, or a picture of where they differ.</summary>
public partial class ImageComparePage : PathComparePage
{
    private LoadedImage? _left;
    private LoadedImage? _right;
    private bool _loading;
    private bool _syncingScroll;

    public ImageComparePage(string left, string right)
    {
        InitializeComponent();
        Header = "Image compare";
        Title = "Image Compare - File Compare";
        SetPaths(left, right);
        InitializePathBar(ThemeMenu, ThemeButtonMenu);
        ApplyBackground();
        ApplyZoom();

        AttachDrop(LeftScroll, DropSide.Left);
        AttachDrop(RightScroll, DropSide.Right);
    }

    protected override TextBox LeftPathBox => LeftPath;
    protected override TextBox RightPathBox => RightPath;

    // Folders have a view of their own.
    protected override bool ShowsKind(CompareKind kind) => kind == CompareKind.Image;

    protected override string? PickPath(TextBox target)
    {
        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Pictures|*.png;*.jpg;*.jpeg;*.jfif;*.bmp;*.gif;*.tif;*.tiff;*.ico;*.wdp;*.jxr|All files|*.*"
        };
        return ShowDialog(dialog) ? dialog.FileName : null;
    }

    public override CompareTarget? Target =>
        _left is null || _right is null ? null : new CompareTarget(CompareKind.Image, _left.Path, _right.Path);

    protected override void OnFirstLoaded() => Run(ReloadAsync);

    protected override void OnThemeChanged() => ApplyBackground();

    /// <summary>The picture areas use the editor background of the theme, so transparent parts look the same as in the text view.</summary>
    private void ApplyBackground()
    {
        var brush = Editor.DiffTheme.EditorBackground;
        LeftScroll.Background = RightScroll.Background = DifferenceScroll.Background = brush;
    }

    // ----- Loading and comparing -----

    protected override async Task<bool> ReloadAsync()
    {
        if (_loading) return true;

        string left = LeftPathText;
        string right = RightPathText;
        if (left.Length == 0 && right.Length == 0)
        {
            ShowStatus("Pick two pictures to compare, or drop them here");
            return true;
        }

        _loading = true;
        ShowStatus("Reading the pictures...");
        try
        {
            var sw = Stopwatch.StartNew();
            var (l, r, diff, mask) = await Task.Run(() =>
            {
                var leftImage = left.Length > 0 ? ImageLoader.Load(left) : null;
                var rightImage = right.Length > 0 ? ImageLoader.Load(right) : null;
                if (leftImage is null || rightImage is null) return (leftImage, rightImage, (ImageDiffResult?)null, (BitmapSource?)null);

                var result = ImageDiff.Compare(leftImage.Pixels, rightImage.Pixels);
                return (leftImage, rightImage, result, ImageLoader.ToBitmap(result.Mask));
            });
            sw.Stop();
            Show(l, r, diff, mask, sw.Elapsed);
            if (l is not null && r is not null) RecordInHistory();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or FormatException or ArgumentException or InvalidOperationException)
        {
            // FileFormatException (a damaged or unknown picture) is a FormatException.
            _left = _right = null;
            LeftImage.Source = RightImage.Source = DifferenceImage.Source = null;
            LeftInfo.Text = RightInfo.Text = "";
            ShowStatus($"Error: {ex.Message}");
            return true;
        }
        finally
        {
            _loading = false;
        }
    }

    private void Show(LoadedImage? left, LoadedImage? right, ImageDiffResult? diff, BitmapSource? mask, TimeSpan elapsed)
    {
        _left = left;
        _right = right;
        LeftImage.Source = left?.Bitmap;
        RightImage.Source = right?.Bitmap;
        DifferenceImage.Source = mask;
        ApplyZoom();

        LeftInfo.Text = Describe(left);
        RightInfo.Text = Describe(right);
        SetTitles(left?.Path ?? "", right?.Path ?? "", "Image Compare");
        LoadTimeText.Text = $"Load time: {elapsed.TotalSeconds:0.00} seconds";

        if (diff is null)
        {
            ShowStatus("Pick the other picture to compare");
            LoadTimeText.Text = $"Load time: {elapsed.TotalSeconds:0.00} seconds";
            return;
        }

        if (diff.AreSame)
        {
            DiffCountText.Text = "The pictures are identical, pixel for pixel";
        }
        else
        {
            string count = $"{diff.DifferentPixels:N0} of {diff.TotalPixels:N0} pixels differ ({diff.DifferentPercent:0.##} %)";
            DiffCountText.Text = diff.SameSize ? count : $"The sizes differ. {count}";
        }
        SameText.Text = diff.AreSame ? "Same" : "Different";
    }

    private static string Describe(LoadedImage? image)
    {
        if (image is null) return "";
        string frames = image.FrameCount > 1 ? $"   first of {image.FrameCount} frames" : "";
        return $"{image.Pixels.Width:N0} x {image.Pixels.Height:N0}   {image.Format}   {FolderFormat.Size(image.SizeBytes)}   " +
               $"{image.LastWriteTime:dd.MM.yyyy HH:mm:ss}{frames}";
    }

    private void ShowStatus(string message)
    {
        DiffCountText.Text = message;
        SameText.Text = "";
        LoadTimeText.Text = "";
    }

    // ----- View -----

    private void OnViewChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool difference = DifferenceButton.IsChecked == true;
        SideBySidePanel.Visibility = difference ? Visibility.Collapsed : Visibility.Visible;
        DifferenceScroll.Visibility = difference ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnZoomChanged(object sender, RoutedEventArgs e)
    {
        // The toggle starts checked, so this also fires while the XAML is still being read, before the picture areas exist.
        if (IsInitialized) ApplyZoom();
    }

    /// <summary>
    /// Fit: the picture shrinks to the window (never grows). Off: 100 %, with scroll bars. At 100 % one picture pixel
    /// is one device-independent pixel, so with Windows display scaling it covers more than one screen pixel.
    /// </summary>
    private void ApplyZoom()
    {
        bool fit = FitToggle.IsChecked == true;
        foreach (var (scroll, image) in new[] { (LeftScroll, LeftImage), (RightScroll, RightImage), (DifferenceScroll, DifferenceImage) })
        {
            var bars = fit ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = bars;
            scroll.VerticalScrollBarVisibility = bars;

            image.Stretch = fit ? Stretch.Uniform : Stretch.None;
            image.StretchDirection = StretchDirection.DownOnly;
            image.HorizontalAlignment = HorizontalAlignment.Center;
            image.VerticalAlignment = VerticalAlignment.Center;
            RenderOptions.SetBitmapScalingMode(image, fit ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor);
        }
    }

    /// <summary>At actual size the two sides scroll together, so the same spot stays next to each other.</summary>
    private void OnSideScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_syncingScroll || sender is not ScrollViewer from) return;
        var to = ReferenceEquals(from, LeftScroll) ? RightScroll : LeftScroll;
        _syncingScroll = true;
        try
        {
            to.ScrollToHorizontalOffset(from.HorizontalOffset);
            to.ScrollToVerticalOffset(from.VerticalOffset);
        }
        finally { _syncingScroll = false; }
    }

    // ----- Commands -----

    private void OnOpenAsHex(object sender, RoutedEventArgs e) =>
        ShellWindow.Show(new HexComparePage(LeftPathText, RightPathText), Host);

    private void OnPageKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5) { Run(ReloadAsync); e.Handled = true; }
    }
}
