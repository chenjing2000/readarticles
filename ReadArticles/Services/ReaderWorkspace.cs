using ReadArticles.Models;
namespace ReadArticles.Services;
public sealed class ReaderWorkspace
{
    public LibrarySnapshot? Library { get; private set; }
    public LoadedArticle? Article { get; private set; }
    public void OpenLibrary(string root)
    {
        var prepared = new LibraryRepository().Load(root);
        CommitLibrary(prepared);
    }
    public void CommitLibrary(LibrarySnapshot prepared)
    {
        Library = prepared;
        Article = null;
    }
    public void OpenArticle(string path)
    {
        var prepared = new ArticleRepository().Load(path);
        CommitArticle(prepared);
    }
    public void CommitArticle(LoadedArticle prepared) => Article = prepared;
    public void ReplaceVocabulary(VocabularyData vocabulary)
    {
        var article = Article ?? throw new InvalidOperationException("请先打开文章。");
        if (!article.Vocabulary.CanWrite) throw new InvalidOperationException("原词汇表损坏，不能覆盖。请先修复并重新打开。");
        string fingerprint = new VocabularyRepository().Save(article.VocabularyPath, vocabulary, article.Vocabulary.Fingerprint);
        Article = article with { Vocabulary = new VocabularySnapshot(vocabulary, fingerprint, true) };
    }
}
