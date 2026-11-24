using FileCompare.App.Tabs;
using FileCompare.Core.Sessions;
using FileCompare.Core.Text;

namespace FileCompare.App;

/// <summary>Creates the page that suits a pair of paths: home, text, folder, hex or image compare.</summary>
public static class PageLauncher
{
    public static ComparePage Create(CommandLineArgs args)
    {
        string left = TextComparer.NormalizePath(args.Left);
        string right = TextComparer.NormalizePath(args.Right);
        if (left.Length == 0 && right.Length == 0) return new HomePage();

        return CompareKindDetector.Detect(left, right) switch
        {
            CompareKind.Folder => new FolderComparePage(left, right),
            CompareKind.Hex => new HexComparePage(left, right),
            CompareKind.Image => new ImageComparePage(left, right),
            _ => new TextComparePage(new CommandLineArgs(left, right))
        };
    }
}
