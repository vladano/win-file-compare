using System.IO;
using System.Windows;
using FileCompare.App.Dialogs;
using FileCompare.App.Folders;
using FileCompare.Core.Folders;

namespace FileCompare.App;

// Opening files, copy, delete, update and the rules dialog.
public partial class FolderComparePage
{
    // ----- Open -----

    private void OnOpen(object sender, RoutedEventArgs e) => OpenSelected();

    /// <summary>A file opens in a new tab (text, hex or image compare, whichever fits); a folder opens or closes.</summary>
    private void OpenSelected()
    {
        // Opening a file works while the contents are still compared; it only reads.
        if (_session is null || _busy || FolderGrid.SelectedItem is not FolderRow row) return;

        if (row.Node.IsFolder)
        {
            ToggleRow(row);
            return;
        }
        if (row.Node.Left is null || row.Node.Right is null)
        {
            StatusText.Text = "This file exists on one side only, so there is nothing to compare it with.";
            return;
        }

        OpenInNewTab(Path.Combine(LeftRoot, row.RelativePath), Path.Combine(RightRoot, row.RelativePath));
    }

    // ----- Copy -----

    private async void OnCopyToRight(object sender, RoutedEventArgs e) => await CopyAsync(toRight: true);
    private async void OnCopyToLeft(object sender, RoutedEventArgs e) => await CopyAsync(toRight: false);

    private async Task CopyAsync(bool toRight)
    {
        if (!CanChangeFiles()) return;
        var nodes = SelectedNodes();
        if (nodes.Count == 0) { StatusText.Text = "Select the files or folders to copy first."; return; }

        string side = toRight ? "right" : "left";
        int overwrites = FolderOperations.CountOverwrites(nodes, toRight);
        if (overwrites > 0 && !Confirm($"{overwrites:N0} file(s) on the {side} side will be replaced by the other side's version. Continue?"))
            return;

        string left = LeftRoot, right = RightRoot;
        await RunAsync($"Copied to the {side}",
            (ct, progress) => FolderOperations.Copy(nodes, left, right, toRight, ct, progress),
            nodes.Select(n => n.RelativePath).ToList());
    }

    // ----- Delete -----

    private void OnDeleteButtonClick(object sender, RoutedEventArgs e) => OpenMenu(DeleteMenu, DeleteButton);
    private async void OnDeleteLeft(object sender, RoutedEventArgs e) => await DeleteAsync(fromLeft: true);
    private async void OnDeleteRight(object sender, RoutedEventArgs e) => await DeleteAsync(fromLeft: false);

    private async Task DeleteAsync(bool fromLeft)
    {
        if (!CanChangeFiles()) return;
        string side = fromLeft ? "left" : "right";
        var nodes = SelectedNodes().Where(n => (fromLeft ? n.Left : n.Right) is not null).ToList();
        if (nodes.Count == 0) { StatusText.Text = $"Nothing selected exists on the {side} side."; return; }

        string what = nodes.Count == 1 ? $"\"{nodes[0].Name}\"" : $"{nodes.Count:N0} items";
        if (!Confirm($"Delete {what} from the {side} side?\n\nThey are moved to the Recycle Bin.")) return;

        string left = LeftRoot, right = RightRoot;
        await RunAsync($"Deleted from the {side} side",
            (_, _) => FolderOperations.Delete(nodes, left, right, fromLeft),
            nodes.Select(n => n.RelativePath).ToList());
    }

    // ----- Update -----

    // ----- Update, Mirror and Sync -----

    private static readonly SyncBaselineStore Baselines = new();

    private void OnUpdateButtonClick(object sender, RoutedEventArgs e) => OpenMenu(UpdateMenu, UpdateButton);
    private async void OnUpdateRight(object sender, RoutedEventArgs e) => await SyncAsync(SyncKind.UpdateRight);
    private async void OnUpdateLeft(object sender, RoutedEventArgs e) => await SyncAsync(SyncKind.UpdateLeft);
    private async void OnMirrorRight(object sender, RoutedEventArgs e) => await SyncAsync(SyncKind.MirrorRight);
    private async void OnMirrorLeft(object sender, RoutedEventArgs e) => await SyncAsync(SyncKind.MirrorLeft);
    private async void OnSyncBothWays(object sender, RoutedEventArgs e) => await SyncAsync(SyncKind.TwoWay);

    /// <summary>Plans the operation, shows the preview, and runs what the user confirmed.</summary>
    private async Task SyncAsync(SyncKind kind)
    {
        if (!CanChangeFiles() || _session is null) return;
        var session = _session;
        string left = LeftRoot, right = RightRoot;

        // Planning a sync may read small files (to tell a touched file from a changed one), so it runs in the background.
        SetBusy(true);
        SyncPlan plan;
        try
        {
            plan = await Task.Run(() => kind switch
            {
                SyncKind.UpdateRight => SyncPlanner.PlanUpdate(session.Root, toRight: true),
                SyncKind.UpdateLeft => SyncPlanner.PlanUpdate(session.Root, toRight: false),
                SyncKind.MirrorRight => SyncPlanner.PlanMirror(session.Root, left, right, toRight: true),
                SyncKind.MirrorLeft => SyncPlanner.PlanMirror(session.Root, left, right, toRight: false),
                _ => SyncPlanner.PlanTwoWay(session.Root, left, right, Baselines.Load(left, right))
            });
        }
        finally
        {
            SetBusy(false);
        }

        if (plan.Refusal is { } refusal)
        {
            StatusText.Text = "Nothing was done";
            Ask(refusal, "Folder Compare", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (plan.Items.Count == 0)
        {
            StatusText.Text = kind switch
            {
                SyncKind.UpdateRight => "Nothing to update: the right side is up to date.",
                SyncKind.UpdateLeft => "Nothing to update: the left side is up to date.",
                SyncKind.MirrorRight or SyncKind.MirrorLeft => "Nothing to do: both sides are already the same.",
                _ => "Nothing to sync: no changes on either side."
            };
            // In sync: remember this state, so the next sync can tell later deletes from new files. Not when files
            // were left uncompared (Esc), because they would drop out of the baseline.
            if (kind == SyncKind.TwoWay && session.Summary.Pending == 0)
                await Task.Run(() => Baselines.Save(SyncBaseline.FromTree(session.Root, left, right)));
            return;
        }

        var dialog = new SyncPreviewDialog(plan, left, right) { Owner = OwnerWindow };
        if (dialog.ShowDialog() != true) return;

        string done = kind switch
        {
            SyncKind.UpdateRight => "Updated the right side",
            SyncKind.UpdateLeft => "Updated the left side",
            SyncKind.MirrorRight => "Mirrored to the right",
            SyncKind.MirrorLeft => "Mirrored to the left",
            _ => "Synced"
        };
        await RunPlanAsync(session, plan, done);
    }

    /// <summary>Runs a confirmed plan in the background (Esc stops it between files), then shows the new state.</summary>
    private async Task RunPlanAsync(FolderCompareSession session, SyncPlan plan, string doneText)
    {
        _cancel = new CancellationTokenSource();
        var cancel = _cancel;
        SetBusy(true);
        var progress = new Progress<int>(count => StatusText.Text = $"Working... {count:N0} files (Esc to stop)");
        try
        {
            var result = await session.RunPlanAsync(plan, Baselines, cancel.Token, progress);
            RebuildRows();
            UpdateSummary();
            StatusText.Text = result.HasErrors
                ? $"{doneText}: {result.Done:N0} done, {result.Errors.Count:N0} failed" +
                  (plan.Kind == SyncKind.TwoWay ? ". Sync again to finish." : "")
                : $"{doneText}: {result.Done:N0} done";
            if (result.HasErrors) ShowErrors(result.Errors);
        }
        catch (OperationCanceledException)
        {
            RebuildRows();
            UpdateSummary();
            StatusText.Text = "Stopped. What was done so far stays done; the folders were scanned again.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ----- Rules -----

    private async void OnRules(object sender, RoutedEventArgs e)
    {
        var dialog = new FolderRulesDialog(UserSettings.Current.Folders) { Owner = OwnerWindow };
        if (dialog.ShowDialog() != true) return;

        UserSettings.Current.Folders = dialog.Result;
        UserSettings.Save();
        await ReloadAsync();
    }

    // ----- Shared -----

    /// <summary>
    /// Runs a file operation in the background, then scans just the affected entries again and redraws.
    /// Esc cancels; after a cancel the whole tree is scanned again because it may be half changed.
    /// </summary>
    private async Task RunAsync(string doneText, Func<CancellationToken, IProgress<int>, OperationResult> work, IReadOnlyCollection<string> refreshPaths)
    {
        if (!Ready || _session is null) return;

        var session = _session;
        _cancel = new CancellationTokenSource();
        var cancel = _cancel;
        SetBusy(true);
        var progress = new Progress<int>(count => StatusText.Text = $"Working... {count:N0} files (Esc to cancel)");
        try
        {
            var result = await Task.Run(() => work(cancel.Token, progress));
            await session.RefreshAsync(refreshPaths);
            RebuildRows();
            UpdateSummary();

            StatusText.Text = result.HasErrors
                ? $"{doneText}: {result.Done:N0} done, {result.Errors.Count:N0} failed"
                : $"{doneText}: {result.Done:N0} done";
            if (result.HasErrors) ShowErrors(result.Errors);
        }
        catch (OperationCanceledException)
        {
            await session.RefreshAsync(new[] { "" });
            RebuildRows();
            UpdateSummary();
            StatusText.Text = "Cancelled";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private bool Confirm(string message) =>
        Ask(message, "Folder Compare", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void ShowErrors(IReadOnlyList<string> errors)
    {
        const int Shown = 10;
        string text = string.Join("\n", errors.Take(Shown));
        if (errors.Count > Shown) text += $"\n... and {errors.Count - Shown:N0} more";
        Ask("Some items could not be processed:\n\n" + text, "Folder Compare", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
