using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
        Width = Math.Round(Math.Round(workArea.Width) * 0.7);
        Height = Math.Round(Math.Round(workArea.Height) * 0.7);
        MinWidth = Math.Min(1050, Width);
        MinHeight = Math.Min(620, Height);
        Left = Math.Round(Math.Round(workArea.Left) + (Math.Round(workArea.Width) - Width) / 2);
        Top = Math.Round(Math.Round(workArea.Top) + (Math.Round(workArea.Height) - Height) / 2);

        // Keep all three columns within the smaller startup window.
        double contentWidth = Math.Max(0, Width - 40);
        double panelScale = Math.Min(1, contentWidth / 906);
        LeftColumn.MinWidth = Math.Round(200 * panelScale);
        CenterColumn.MinWidth = Math.Round(410 * panelScale);
        RightColumn.MinWidth = Math.Round(280 * panelScale);
        LeftColumn.Width = new GridLength(Math.Clamp(settings.LeftWidth, LeftColumn.MinWidth, Math.Max(LeftColumn.MinWidth, Math.Round(contentWidth * 0.22))));
        RightColumn.Width = new GridLength(Math.Clamp(settings.RightWidth, RightColumn.MinWidth, Math.Max(RightColumn.MinWidth, Math.Round(contentWidth * 0.28))));

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
        settings.RightWidth = Math.Round(RightColumn.ActualWidth);
    }

    private void SaveSettings(double volume)
    {
        if (!persistSettings) return;

        settings.Volume = volume;
        settings.Accent = ViewModel.Accent;
        settings.Library = ViewModel.LibraryRoot;
        try
        {
            settings.Save();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            string message = "无法保存配置文件：" + error.Message;
            if (IsLoaded) Dispatcher.BeginInvoke(() => ViewModel.Status = message);
            else MessageBox.Show(message, "ReadArticles", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.Accent) || e.PropertyName == nameof(ViewModel.LibraryRoot))
            SaveSettings(player.Volume);
    }

    private void ColumnsResized(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        settings.LeftWidth = Math.Round(LeftColumn.ActualWidth);
        settings.RightWidth = Math.Round(RightColumn.ActualWidth);
        LeftColumn.Width = new GridLength(settings.LeftWidth);
        RightColumn.Width = new GridLength(settings.RightWidth);
        SaveSettings(player.Volume);
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
        foreach (var reader in readers) reader.FontSize = settings.FontSize;
        VocabularyList.FontSize = settings.FontSize;
        RefreshSettingsChecks();
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
    }

    private static void RefreshStepChecks(MenuItem menu, double step)
    {
        foreach (MenuItem item in menu.Items)
            item.IsChecked = double.Parse((string)item.Tag, CultureInfo.InvariantCulture) == step;
    }


}
