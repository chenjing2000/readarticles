using ReadArticles.Models;

namespace ReadArticles.ViewModels;

public sealed partial class MainViewModel
{
    public void ToggleArticleAudio() => playback.ToggleArticle(Article?.Directory);
    public void PlayWord(VocabularyEntry entry, string wordAccent) =>
        playback.PlayWord(Article?.Directory, Article?.Vocabulary.Data.Words ?? [], entry, wordAccent);
    public void SeekArticleRelative(bool backward) =>
        playback.SeekArticleRelative(backward ? -settings.BackwardStep : settings.ForwardStep);
    public void StopArticleAudio() => playback.StopArticle();
    public void Stop() => playback.Stop();
}
