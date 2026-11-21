using ReadArticles.Models;

namespace ReadArticles.ViewModels;

public sealed partial class MainViewModel
{
    public void ToggleArticleAudio() => playback.ToggleArticle(Article);
    public void PlayWord(VocabularyEntry entry, string wordAccent) => playback.PlayWord(Article, entry, wordAccent);
    public void StopArticleAudio() => playback.StopArticle();
    public void Stop() => playback.Stop();
}
