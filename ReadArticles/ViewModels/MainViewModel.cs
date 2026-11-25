using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.IO;
using System.Text.Json;
using ReadArticles.Models;
using ReadArticles.Infrastructure;
using ReadArticles.Services;
using ReadArticles.Library;

namespace ReadArticles.ViewModels;

public sealed partial class MainViewModel : INotifyPropertyChanged
{
    private readonly ReaderWorkspace workspace = new ReaderWorkspace();
    private readonly ReaderPlayback playback;
    private readonly LibraryNavigation navigation;
    public NotificationService Notifications { get; } = new();
    public void Notify(string key, string message, NotificationSeverity severity) =>
        Notifications.Publish(key, message, severity);
    private readonly ReaderSettings settings;
    private bool highlightsVisible;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? ArticleChanged;
    public event Action? VocabularyChanged;
    public event Action? PlaybackChanged;
    public ObservableCollection<NavigationNode> Books => navigation.Books;
    public ObservableCollection<VocabularyEntryViewModel> Words { get; } = new ObservableCollection<VocabularyEntryViewModel>();
    public LoadedArticle? Article => workspace.Article;
    public string? LibraryRoot => navigation.Root;
    public string ArticleTitle => Article?.Title ?? "";
    public ReaderPlayback Playback => playback;
    public bool CanWriteVocabulary => Article?.Vocabulary.CanWrite == true;
    public string Accent
    {
        get => settings.Accent;
        set
        {
            if ((value != "uk" && value != "us") || value == settings.Accent) return;
            playback.StopPronunciation();
            settings.Accent = value;
            Changed();
        }
    }
    public bool HighlightsVisible
    {
        get => highlightsVisible;
        set
        {
            if (highlightsVisible == value) return;
            highlightsVisible = value;
            Changed();
        }
    }
    // The caller persists the detached settings draft before applying it.
    public void ApplySettings(ReaderSettings edited)
    {
        string previousAccent = settings.Accent;
        if (previousAccent != edited.Accent) playback.StopPronunciation();
        settings.Apply(edited);
        if (previousAccent != settings.Accent) Changed(nameof(Accent));
    }

    public MainViewModel(PlaybackController articlePlayer, PlaybackController pronunciationPlayer,
        ReaderSettings? settings = null)
    {
        this.settings = settings ?? new ReaderSettings();
        navigation = new LibraryNavigation(Notify);
        playback = new ReaderPlayback(articlePlayer, pronunciationPlayer);
        playback.Message += (message, severity) => Notify("PlaybackFailure", message, severity);
        playback.Changed += () => PlaybackChanged?.Invoke();
    }

    private bool Handle(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            AppLogger.Default.Warning("UserOperationFailed", error);
            Notify("UserOperationFailed", "操作失败，请检查文章或词汇文件及访问权限。", NotificationSeverity.Error);
            return false;
        }
    }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
