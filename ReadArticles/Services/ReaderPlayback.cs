using System.IO;
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
        articlePlayer.Message += message => Message?.Invoke(message);
        pronunciationPlayer.Message += message => Message?.Invoke(message);
    }

    private void ArticleChanged()
    {
        if (articlePlayer.State == PlaybackState.Playing && pronunciationPlayer.State != PlaybackState.Stopped)
            pronunciationPlayer.Stop();
        Changed?.Invoke();
    }

    private void PronunciationChanged()
    {
        if (pronunciationPlayer.State == PlaybackState.Playing) articlePlayer.Pause();
        Changed?.Invoke();
    }

    public event Action? Changed;
    public event Action<string>? Message;
    private PlaybackController ActivePlayer => pronunciationPlayer.State != PlaybackState.Stopped ? pronunciationPlayer : articlePlayer;
    public PlaybackState State => ActivePlayer.State;
    public string Owner => ActivePlayer.Owner;
    public string? CurrentSid => pronunciationPlayer.CurrentSid;
    public bool IsArticleActive => articlePlayer.State != PlaybackState.Stopped;
    public bool IsArticlePlaying => articlePlayer.State == PlaybackState.Playing;
    public bool IsArticlePaused => articlePlayer.State == PlaybackState.Paused;
    public bool CanSeekArticle => IsArticleActive && articlePlayer.IsOpen && articlePlayer.Duration > TimeSpan.Zero;
    public double ArticleDuration => CanSeekArticle ? articlePlayer.Duration.TotalSeconds : 0;
    public double ArticlePosition => CanSeekArticle ? articlePlayer.Position.TotalSeconds : 0;

    public void ToggleArticle(LoadedArticle? article)
    {
        Handle(() =>
        {
            if (articlePlayer.ToggleCurrent(ArticleOwner)) return;
            string path = resources.ArticleFile(RequireArticle(article).Directory);
            articlePlayer.Play(new AudioItem(path), ArticleOwner);
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

    public void PlaySegment(LoadedArticle? article, string sid, string accent)
    {
        Handle(() =>
        {
            var current = RequireArticle(article);
            var segment = current.Segments.Single(s => s.Sid == sid);
            string path = resources.Segment(current.Directory, segment, accent);
            pronunciationPlayer.Play(new AudioItem(path, sid), "segment:" + sid);
        });
    }

    public bool CanPlaySegment(LoadedArticle? article, string sid, string accent)
    {
        var segment = article?.Segments.FirstOrDefault(s => s.Sid == sid);
        return segment?.HasAudio == true && AudioResources.IsAvailable(resources.Segment(article!.Directory, segment, accent));
    }

    public void PlayWord(LoadedArticle? article, VocabularyEntry entry, string accent)
    {
        Handle(() =>
        {
            var current = RequireArticle(article);
            if (!AudioResources.OwnsWordAudio(current, entry)) throw new InvalidOperationException("词条没有音频资格。");
            string path = resources.Vocabulary(current.Directory, entry, accent);
            pronunciationPlayer.Play(new AudioItem(path), "word:" + accent + ":" + entry.Word);
        });
    }

    public bool CanPlayWord(LoadedArticle article, VocabularyEntry entry, string accent)
    {
        try
        {
            return AudioResources.OwnsWordAudio(article, entry) && AudioResources.IsAvailable(resources.Vocabulary(article.Directory, entry, accent));
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

    public void RefreshVocabulary(LoadedArticle article)
    {
        if (!pronunciationPlayer.Owner.StartsWith("word:", StringComparison.Ordinal)) return;
        var entry = article.Vocabulary.Data.Words.FirstOrDefault(w =>
            pronunciationPlayer.Owner == "word:uk:" + w.Word || pronunciationPlayer.Owner == "word:us:" + w.Word);
        if (entry is null || !AudioResources.OwnsWordAudio(article, entry)) pronunciationPlayer.Stop();
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

    private static LoadedArticle RequireArticle(LoadedArticle? article) =>
        article ?? throw new InvalidOperationException("请先打开文章。");

    private void Handle(Action action, bool stopOnFailure = true)
    {
        try
        {
            action();
        }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            // A missing sibling MP3 leaves pronunciation playing; failed pronunciation cancels its previous target.
            if (stopOnFailure) pronunciationPlayer.Stop();
            Message?.Invoke(error.Message);
        }
    }
}
