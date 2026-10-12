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
        ViewModel = new MainViewModel(new PlaybackController(player), new PlaybackController(pronunciationPlayer));

        InitializeComponent();
        DataContext = ViewModel;
        InitializeWindowSettings();
        ViewModel.HighlightsVisible = false;

        ViewModel.ArticleChanged += RenderArticle;
        ViewModel.VocabularyChanged += RefreshVocabularyHighlights;
        InitializePlayback();
        InitializeStatus();
        ViewModel.PropertyChanged += SettingsChanged;
        Closing += WindowClosing;
        Closed += WindowClosed;

        RenderArticle();
        if (settingsWarning != null) Dispatcher.BeginInvoke(() => ViewModel.Status = settingsWarning);
    }

    private void WindowClosed(object? sender, EventArgs e)
    {
        double volume = player.Volume;
        CloseStatus();
        ClosePlayback();
        SaveSettings(volume);
    }
}
