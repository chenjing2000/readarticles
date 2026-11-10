using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using ReadArticles.Models;
using ReadArticles.Infrastructure;
using ReadArticles.Services;

namespace ReadArticles.ViewModels;

public sealed partial class MainViewModel : INotifyPropertyChanged
{
    private readonly ReaderWorkspace workspace = new ReaderWorkspace();
    private readonly ReaderPlayback playback;
    private string status = "";
    public NotificationService Notifications { get; } = new();
    public void Notify(string key, string message, NotificationSeverity severity) => Notifications.Publish(key, message, severity);
    private string accent = "uk";
    private bool highlightsVisible;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? ArticleChanged;
    public event Action? VocabularyChanged;
    public event Action? PlaybackChanged;
    public ObservableCollection<NavigationNode> Books { get; } = new ObservableCollection<NavigationNode>();
    public ObservableCollection<VocabularyEntryViewModel> Words { get; } = new ObservableCollection<VocabularyEntryViewModel>();
    public LoadedArticle? Article => workspace.Article;
    public string? LibraryRoot => workspace.Library?.Root;
    public string ArticleTitle => Article?.Title ?? "";
    public ReaderPlayback Playback => playback;
    public string? PlayingSid => playback.CurrentSid;
    public string Owner => playback.Owner;
    public PlaybackState PlaybackState => playback.State;
    public bool CanWriteVocabulary => Article?.Vocabulary.CanWrite == true;
    public string CurrentUser { get; private set; } = "User: —";
    public string Status
    {
        get => status;
        set
        {
            status = value;
            Changed();
        }
    }
    public string Accent
    {
        get => accent;
        set
        {
            if ((value != "uk" && value != "us") || value == accent) return;
            playback.StopPronunciation();
            accent = value;
            Changed();
        }
    }
    public bool HighlightsVisible
    {
        get => highlightsVisible;
        set
        {
            highlightsVisible = value;
            Changed();
            VocabularyChanged?.Invoke();
        }
    }
    public MainViewModel(PlaybackController articlePlayer, PlaybackController pronunciationPlayer)
    {
        playback = new ReaderPlayback(articlePlayer, pronunciationPlayer);
        playback.Message += (message, severity) => Notify("PlaybackFailure", message, severity);
        playback.Changed += () =>
        {
            PlaybackChanged?.Invoke();
            CommandManager.InvalidateRequerySuggested();
        };
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
            Notify("UserOperationFailed", error.Message, NotificationSeverity.Error);
            Status = error.Message;
            return false;
        }
    }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
