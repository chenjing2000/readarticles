using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ReadArticles.Infrastructure;
using ReadArticles.Layout;

namespace ReadArticles;

public partial class MainWindow
{
    private readonly ReaderSettings settings;
    private readonly PaneLayout paneLayout = new();
    private readonly bool persistSettings;

    private void InitializeWindowSettings()
    {
        Resources[SystemParameters.VerticalScrollBarWidthKey] = Math.Round(Math.Round(SystemParameters.VerticalScrollBarWidth) / 2);
        var workArea = SystemParameters.WorkArea;
        // Reserve enough room for the default pane widths when the screen permits.
        const double minimumWindowWidth = 200 + 3 + 200 + 3 + 200 + 40;
        const double defaultWindowWidth = 250 + 3 + 200 + 3 + 300 + 40;
        Width = Math.Min(Math.Round(workArea.Width),
            Math.Max(Math.Round(Math.Round(workArea.Width) * 0.7), defaultWindowWidth));
        Height = Math.Round(Math.Round(workArea.Height) * 0.7);
        MinWidth = Math.Min(minimumWindowWidth, Width);
        MinHeight = Math.Min(620, Height);
        Left = Math.Round(Math.Round(workArea.Left) + (Math.Round(workArea.Width) - Width) / 2);
        Top = Math.Round(Math.Round(workArea.Top) + (Math.Round(workArea.Height) - Height) / 2);

        // Only the native library pane is sized in WPF; WebView2 owns the vocabulary divider.
        LeftColumn.Width = new GridLength(paneLayout.LeftWidth);

        // The reader owns no volume setting: both native players run at 100%.
        player.Volume = 1.0;
        pronunciationPlayer.Volume = 1.0;
    }

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (!persistSettings) return;
        SaveSettings(showDialogOnFailure: true);
    }

    private bool SaveSettings(bool showDialogOnFailure = false)
    {
        if (!persistSettings) return true;

        settings.Library = ViewModel.LibraryRoot;
        try
        {
            settings.Save();
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            AppLogger.Default.Error("SettingsSaveFailed", error);
            string message = "无法保存配置文件：" + error.Message;
            if (showDialogOnFailure || !IsLoaded)
                MessageBox.Show(this, message, "ReadArticles", MessageBoxButton.OK, MessageBoxImage.Warning);
            else
                Dispatcher.BeginInvoke(() => ViewModel.Notify("SettingsSaveFailed", "无法保存配置文件，请检查配置文件内容及目录权限。", NotificationSeverity.Error));
            return false;
        }
    }

    private void SettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.LibraryRoot))
            SaveSettings();
    }

    // One layout authority for both splitters. The center absorbs changes
    // first; only after it reaches 200 DIP does the opposite side shrink.
    private double AvailablePaneWidth()
    {
        double width = ((Grid)Content).ActualWidth;
        if (width <= 0) width = Math.Max(646, Width) - 40;
        return Math.Floor(Math.Max(606, width) - 6);
    }

    private void ApplyPaneWidths(bool rightChanged)
    {
        if (Math.Abs(LeftColumn.Width.Value - paneLayout.LeftWidth) >= 0.5)
            LeftColumn.Width = new GridLength(paneLayout.LeftWidth);
        if (rightChanged) webReaderBridge?.SendPaneWidth();
    }

    private void ResizeLibraryPane(double requestedLeft) =>
        ApplyPaneWidths(paneLayout.ResizeLibrary(requestedLeft, AvailablePaneWidth()));

    private void WorkspaceResized(object sender, SizeChangedEventArgs e) =>
        ApplyPaneWidths(paneLayout.Fit(AvailablePaneWidth()));

    private void LibrarySplitterDragged(object sender, DragDeltaEventArgs e) =>
        ResizeLibraryPane(paneLayout.LeftWidth + e.HorizontalChange);

    private void SettingsClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(settings, ApplyDialogSettings) { Owner = this };
        dialog.ShowDialog();
    }

    private bool ApplyDialogSettings(ReaderSettings edited)
    {
        edited.Library = ViewModel.LibraryRoot;
        try
        {
            if (persistSettings) edited.Save();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            AppLogger.Default.Error("SettingsSaveFailed", error);
            MessageBox.Show(this, "无法保存设置：" + error.Message,
                "ReadArticles", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        ViewModel.ApplySettings(edited);
        webReaderBridge?.SendSettings();
        return true;
    }
}
