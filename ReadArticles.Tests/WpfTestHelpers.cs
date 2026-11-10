using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

internal static class WpfTestHelpers
{
    internal static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    internal static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    internal static void Wait(Func<bool> predicate)
    {
        var watch = Stopwatch.StartNew();
        while (!predicate() && watch.Elapsed.TotalSeconds < 8) Pump(30);
        if (!predicate()) throw new Exception("WPF event/progress timed out");
    }

    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    // Explicitly materialize only the branches needed by legacy UI assertions.
    // Production navigation remains strictly lazy.
    internal static void ExpandAllDirectories(System.Windows.Controls.ItemsControl parent)
    {
        parent.UpdateLayout();
        foreach (var item in parent.Items)
        {
            if (item is not ReadArticles.Models.NavigationNode node || node.IsPassage || node.IsPlaceholder)
                continue;
            var container = parent.ItemContainerGenerator.ContainerFromItem(item)
                as System.Windows.Controls.TreeViewItem;
            if (container is null)
                throw new Exception("Directory tree container was not generated.");
            container.IsExpanded = true;
            Wait(() => node.LoadState is ReadArticles.Models.NodeLoadState.Loaded
                or ReadArticles.Models.NodeLoadState.Failed);
            if (node.LoadState != ReadArticles.Models.NodeLoadState.Loaded)
                throw new Exception("Directory could not be loaded in UI fixture.");
            Pump();
            container.UpdateLayout();
            ExpandAllDirectories(container);
        }
    }

    internal static bool IsIcon(ImageSource actual, ImageSource expected) => ReferenceEquals(actual, expected);
}
