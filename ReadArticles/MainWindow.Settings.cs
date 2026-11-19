using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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

        ApplyReadingFont((int)settings.FontSize);
        player.Volume = settings.Volume;
        pronunciationPlayer.Volume = player.Volume;
        ViewModel.Accent = settings.Accent;
        RefreshSettingsChecks();
    }

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (!persistSettings) return;
        settings.LeftWidth = Math.Round(LeftColumn.ActualWidth);
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

    private void WorkspaceResized(object sender, SizeChangedEventArgs e)
    {
        // Keep the requested left width when it fits; shrink it before the
        // 200-DIP article or 200-DIP vocabulary would be squeezed.
        double maxLeft = Math.Max(LeftColumn.MinWidth,
            e.NewSize.Width - 3 - CenterColumn.MinWidth);
        double target = Math.Clamp(settings.LeftWidth, LeftColumn.MinWidth,
            Math.Min(LeftColumn.MaxWidth, maxLeft));
        if (Math.Abs(LeftColumn.Width.Value - target) >= 0.5)
            LeftColumn.Width = new GridLength(target);
    }

    private void ColumnsResized(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        RememberLeftColumnWidth();
    }

    private void ColumnSplitterKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right)
            RememberLeftColumnWidth();
    }

    private void RememberLeftColumnWidth()
    {
        settings.LeftWidth = Math.Round(LeftColumn.ActualWidth);
        LeftColumn.Width = new GridLength(settings.LeftWidth);
        // Pane widths are session-only and do not belong in settings.json.
        SendWebPilotSettings();
    }

    private void AccentClicked(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item)
        {
            ViewModel.Accent = (string)item.Tag;
            RefreshSettingsChecks();
        }
    }

    private void SettingsClicked(object sender, RoutedEventArgs e)
    {
        SettingsButton.ContextMenu.PlacementTarget = SettingsButton;
        SettingsButton.ContextMenu.IsOpen = true;
    }

    private void FontClicked(object sender, RoutedEventArgs e)
    {
        ApplyReadingFont(int.Parse((string)((MenuItem)sender).Tag));
        SaveSettings(player.Volume);
    }

    private void ApplyReadingFont(int size)
    {
        settings.FontSize = size;
        RefreshSettingsChecks();
        SendWebPilotSettings();
    }

    private void RefreshSettingsChecks()
    {
        Font14Option.IsChecked = settings.FontSize == 14;
        Font15Option.IsChecked = settings.FontSize == 15;
        Font16Option.IsChecked = settings.FontSize == 16;
        AccentUkOption.IsChecked = ViewModel.Accent == "uk";
        AccentUsOption.IsChecked = ViewModel.Accent == "us";
        RefreshStepChecks(ForwardStepMenu, settings.ForwardStep);
        RefreshStepChecks(BackwardStepMenu, settings.BackwardStep);
    }

    private void ProgressStepClicked(object sender, RoutedEventArgs e)
    {
        var item = (MenuItem)sender;
        double step = double.Parse((string)item.Tag, CultureInfo.InvariantCulture);
        if (ReferenceEquals(item.Parent, ForwardStepMenu)) settings.ForwardStep = step;
        else settings.BackwardStep = step;
        RefreshSettingsChecks();
        SaveSettings(player.Volume);
        SendWebPilotSettings();
    }

    private static void RefreshStepChecks(MenuItem menu, double step)
    {
        foreach (MenuItem item in menu.Items)
            item.IsChecked = double.Parse((string)item.Tag, CultureInfo.InvariantCulture) == step;
    }


}
