using ReadArticles.Models;

namespace ReadArticles.ViewModels;

public sealed partial class MainViewModel
{
    public bool CanPlaySegment(string sid) => playback.CanPlaySegment(Article, sid);
    public void PlaySegment(string sid) => playback.PlaySegment(Article, sid, Accent);
    public void ToggleArticleAudio() => playback.ToggleArticle(Article);
    public void PlayWord(VocabularyEntry entry, string wordAccent) => playback.PlayWord(Article, entry, wordAccent);
    public void StopArticleAudio() => playback.StopArticle();
    public void Stop() => playback.Stop();
}
