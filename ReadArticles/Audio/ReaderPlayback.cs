using System.IO;
using ReadArticles.Infrastructure;
using ReadArticles.Models;

namespace ReadArticles.Services;

public sealed class ReaderPlayback
{
    private const string ArticleOwner = "passage";
    private readonly PlaybackController articlePlayer;
    private readonly PlaybackController pronunciationPlayer;
    private readonly AudioResources resources = new AudioResources();

    public ReaderPlayback(PlaybackController articlePlayer, PlaybackController pronunciationPlayer)
    {
        this.articlePlayer = articlePlayer;
        this.pronunciationPlayer = pronunciationPlayer;
        articlePlayer.Changed += ArticleChanged;
        pronunciationPlayer.Changed += PronunciationChanged;
        articlePlayer.Message += message => Message?.Invoke(message, NotificationSeverity.Warning);
        pronunciationPlayer.Message += message => Message?.Invoke(message, NotificationSeverity.Warning);
    }

    private void ArticleChanged()
    {
        if (articlePlayer.State == PlaybackState.Playing && articlePlayer.IsOpen &&
            pronunciationPlayer.State != PlaybackState.Stopped)
            pronunciationPlayer.Stop();
        Changed?.Invoke();
    }

    private void PronunciationChanged()
    {
        if (pronunciationPlayer.State == PlaybackState.Playing)
        {
            if (articlePlayer.State != PlaybackState.Stopped && !articlePlayer.IsOpen)
                articlePlayer.Stop();
            else
                articlePlayer.Pause();
        }
        Changed?.Invoke();
    }

    public event Action? Changed;
    public event Action<string, NotificationSeverity>? Message;
    public bool IsArticleActive => articlePlayer.State != PlaybackState.Stopped;
    public bool IsArticlePlaying => articlePlayer.State == PlaybackState.Playing;
    public bool IsArticlePaused => articlePlayer.State == PlaybackState.Paused;
    public bool CanSeekArticle => IsArticleActive && articlePlayer.IsOpen && articlePlayer.Duration > TimeSpan.Zero;
    public double ArticleDuration => CanSeekArticle ? articlePlayer.Duration.TotalSeconds : 0;
    public double ArticlePosition => CanSeekArticle ? articlePlayer.Position.TotalSeconds : 0;

    public void ToggleArticle(string? directory)
    {
        Handle(() =>
        {
            if (articlePlayer.ToggleCurrent(ArticleOwner)) return;
            var files = resources.ArticleFiles(RequireDirectory(directory));
            articlePlayer.PlayCandidates(files, ArticleOwner);
        }, false);
    }

    public void StopArticle()
    {
        articlePlayer.Stop();
    }

    public void SeekArticle(double seconds)
    {
        if (CanSeekArticle) articlePlayer.Seek(seconds);
    }

    public void SeekArticleRelative(double offsetSeconds)
    {
        if (CanSeekArticle) SeekArticle(ArticlePosition + offsetSeconds);
    }

    public void PlayWord(string? directory, IReadOnlyList<VocabularyEntry> words, VocabularyEntry entry, string accent)
    {
        Handle(() =>
        {
            string root = RequireDirectory(directory);
            if (!AudioResources.OwnsWordAudio(words, entry)) throw new InvalidOperationException("词条没有音频资格。");
            string path = resources.Vocabulary(root, entry, accent);
            pronunciationPlayer.Play(new AudioItem(path), "word:" + accent + ":" + entry.Word);
        });
    }

    public bool CanPlayWord(string directory, IReadOnlyList<VocabularyEntry> words, VocabularyEntry entry, string accent) =>
        AudioResources.OwnsWordAudio(words, entry) && CanPlayStem(directory, AudioResources.AudioStem(entry.Word), accent);

    internal bool CanPlayStem(string directory, string stem, string accent)
    {
        try
        {
            return AudioResources.IsAvailable(resources.VocabularyByStem(directory, stem, accent));
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void StopWord(VocabularyEntry entry)
    {
        if (pronunciationPlayer.Owner == "word:uk:" + entry.Word || pronunciationPlayer.Owner == "word:us:" + entry.Word) pronunciationPlayer.Stop();
    }

    public void RefreshVocabulary(IReadOnlyList<VocabularyEntry> words)
    {
        if (!pronunciationPlayer.Owner.StartsWith("word:", StringComparison.Ordinal)) return;
        var entry = words.FirstOrDefault(w =>
            pronunciationPlayer.Owner == "word:uk:" + w.Word || pronunciationPlayer.Owner == "word:us:" + w.Word);
        if (entry is null || !AudioResources.OwnsWordAudio(words, entry)) pronunciationPlayer.Stop();
    }

    public void StopPronunciation()
    {
        pronunciationPlayer.Stop();
    }

    public void Stop()
    {
        pronunciationPlayer.Stop();
        articlePlayer.Stop();
    }

    private static string RequireDirectory(string? directory) =>
        directory ?? throw new InvalidOperationException("请先打开文章。");

    private void Handle(Action action, bool stopOnFailure = true)
    {
        try
        {
            action();
        }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            // A missing sibling MP3 leaves pronunciation playing; failed pronunciation cancels its previous target.
            AppLogger.Default.Warning("PlaybackRequestFailed", error);
            if (stopOnFailure) pronunciationPlayer.Stop();
            Message?.Invoke(error.Message, error is FileNotFoundException ? NotificationSeverity.Warning : NotificationSeverity.Error);
        }
    }
}
