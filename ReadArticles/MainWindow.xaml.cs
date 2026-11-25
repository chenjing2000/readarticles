using System.Windows;
using ReadArticles.Infrastructure;
using ReadArticles.Services;
using ReadArticles.ViewModels;

namespace ReadArticles;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow(bool persistSettings = true)
    {
        this.persistSettings = persistSettings;
        string? settingsWarning = null;
        settings = persistSettings ? ReaderSettings.Load(out settingsWarning) : new ReaderSettings();
        ViewModel = new MainViewModel(new PlaybackController(player), new PlaybackController(pronunciationPlayer), settings);

        InitializeComponent();
        DataContext = ViewModel;
        InitializeWindowSettings();
        ViewModel.HighlightsVisible = false;
        ViewModel.PropertyChanged += SettingsChanged;
        InitializeWebReader();
        Closing += WindowClosing;
        Closed += WindowClosed;

        if (settingsWarning != null)
        {
            AppLogger.Default.Warning("SettingsLoadWarning");
            Dispatcher.BeginInvoke(() => ViewModel.Notify("SettingsLoadWarning", settingsWarning, NotificationSeverity.Warning));
        }
    }

    private void WindowClosed(object? sender, EventArgs e)
    {
        CloseWebReader();
        ViewModel.PropertyChanged -= SettingsChanged;
        ViewModel.CloseNavigation();
        ClosePlayback();
    }
}
