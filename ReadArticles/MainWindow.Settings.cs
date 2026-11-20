using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ReadArticles.Infrastructure;

namespace ReadArticles;

public partial class MainWindow
{
    private readonly ReaderSettings settings;
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
        LeftColumn.Width = new GridLength(settings.LeftWidth);

        player.Volume = settings.Volume;
        pronunciationPlayer.Volume = player.Volume;
        ViewModel.Accent = settings.Accent;
    }

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (!persistSettings) return;
        SaveSettings(player.Volume, showDialogOnFailure: true);
    }

    private bool SaveSettings(double volume, bool showDialogOnFailure = false)
    {
        if (!persistSettings) return true;

        settings.Volume = volume;
        settings.Accent = ViewModel.Accent;
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
        if (e.PropertyName == nameof(ViewModel.Accent) || e.PropertyName == nameof(ViewModel.LibraryRoot))
            SaveSettings(player.Volume);
    }

    // One layout authority for both splitters. The center absorbs changes
    // first; only after it reaches 200 DIP does the opposite side shrink.
    private double AvailablePaneWidth()
    {
        double width = ((Grid)Content).ActualWidth;
        if (width <= 0) width = Math.Max(646, Width) - 40;
        return Math.Floor(Math.Max(606, width) - 6);
    }

    private void SetPaneWidths(double left, double right, bool notifyBrowser)
    {
        double available = AvailablePaneWidth();
        left = Math.Clamp(Math.Round(left), 200, Math.Min(400, available - 400));
        right = Math.Clamp(Math.Round(right), 200, Math.Min(400, available - left - 200));
        bool rightChanged = Math.Abs(settings.RightWidth - right) >= 0.5;
        settings.LeftWidth = left;
        settings.RightWidth = right;
        if (Math.Abs(LeftColumn.Width.Value - left) >= 0.5)
            LeftColumn.Width = new GridLength(left);
        if (rightChanged && notifyBrowser && webPilotReady)
            PostWebPilot("setPaneWidth", new { rightWidth = right });
    }

    private void ResizeLibraryPane(double requestedLeft)
    {
        double available = AvailablePaneWidth();
        double left = Math.Clamp(Math.Round(requestedLeft), 200, Math.Min(400, available - 400));
        double right = Math.Min(settings.RightWidth, available - left - 200);
        SetPaneWidths(left, right, notifyBrowser: true);
    }

    private void ResizeVocabularyPane(double requestedRight)
    {
        double available = AvailablePaneWidth();
        double right = Math.Clamp(Math.Round(requestedRight), 200, Math.Min(400, available - 400));
        double left = Math.Min(settings.LeftWidth, available - right - 200);
        // Only C# decides the applied width; the browser waits for the result.
        SetPaneWidths(left, right, notifyBrowser: true);
    }

    private void WorkspaceResized(object sender, SizeChangedEventArgs e)
    {
        double available = AvailablePaneWidth();
        double left = settings.LeftWidth;
        double right = settings.RightWidth;
        double overflow = Math.Max(0, left + right + 200 - available);
        double rightReduction = Math.Min(overflow, right - 200);
        right -= rightReduction;
        left -= overflow - rightReduction;
        SetPaneWidths(left, right, notifyBrowser: true);
    }

    private void LibrarySplitterDragged(object sender, DragDeltaEventArgs e)
    {
        ResizeLibraryPane(settings.LeftWidth + e.HorizontalChange);
    }

    private void ColumnSplitterKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right)) return;
        ResizeLibraryPane(settings.LeftWidth + (e.Key == Key.Right ? 16 : -16));
        e.Handled = true;
    }

    private void SettingsClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(settings, ApplyDialogSettings) { Owner = this };
        dialog.ShowDialog();
    }

    private bool ApplyDialogSettings(ReaderSettings edited)
    {
        edited.Library = ViewModel.LibraryRoot;
        edited.LeftWidth = settings.LeftWidth;
        edited.RightWidth = settings.RightWidth;
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

        settings.Apply(edited);
        player.Volume = settings.Volume;
        pronunciationPlayer.Volume = settings.Volume;
        ViewModel.Accent = settings.Accent;
        SendWebPilotSettings();
        return true;
    }
}
