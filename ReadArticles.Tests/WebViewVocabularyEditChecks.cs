using System.IO;
using ReadArticles.Services;
using ReadArticles.ViewModels;

internal static class WebViewVocabularyEditChecks
{
    internal static void Run()
    {
        Program.Run("WebView2 vocabulary edit results commit only on successful saves", () =>
        {
            string root = Path.Combine(Path.GetTempPath(),
                "ReadArticles-webview-edit-" + Guid.NewGuid().ToString("N"));
            ReadArticles.MainWindow? window = null;
            try
            {
                Directory.CreateDirectory(root);
                string articlePath = Path.Combine(root, "Copy.json");
                string vocabularyPath = Path.ChangeExtension(articlePath, ".vocabulary.json");
                string sourceVocabulary = Path.ChangeExtension(TestData.ArticlePath, ".vocabulary.json");
                File.Copy(TestData.ArticlePath, articlePath);
                File.Copy(sourceVocabulary, vocabularyPath);
                var familyFixture = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(vocabularyPath))!;
                familyFixture["words"]![0]!["family"] = new System.Text.Json.Nodes.JsonArray("was stuck in");
                File.WriteAllText(vocabularyPath, familyFixture.ToJsonString());
                string importPath = Path.Combine(root, "Import.vocabulary.json");
                File.Copy(vocabularyPath, importPath);

                window = new ReadArticles.MainWindow(false)
                {
                    Left = -10000, Top = -10000, ShowActivated = false
                };
                window.Show();
                Program.Check(window.ViewModel.OpenArticle(articlePath));
                var model = window.ViewModel;
                int originalCount = model.Words.Count;
                string original = File.ReadAllText(vocabularyPath);
                string existingWord = model.Words[0].Word;

                Program.Check(model.TryAddWord(existingWord) == VocabularyEditResult.Duplicate);
                Program.Check(File.ReadAllText(vocabularyPath) == original);
                Program.Check(model.Words.Count == originalCount);

                const string phrase = "webview stage four verification";
                Program.Check(model.TryAddWord(phrase) == VocabularyEditResult.Success);
                Program.Check(model.Words.Count == originalCount + 1);
                var committed = new VocabularyRepository().Load(vocabularyPath);
                Program.Check(committed.Data.Words[^1].Word == phrase &&
                    committed.Data.Words[0].Family!.SequenceEqual(["was stuck in"]));
                string addedFile = File.ReadAllText(vocabularyPath);

                Program.Check(model.TryAddWord(phrase) == VocabularyEditResult.Duplicate);
                Program.Check(File.ReadAllText(vocabularyPath) == addedFile);

                var entry = model.Words[^1].Entry;
                Program.Check(model.TryMoveWord(entry, -1) == VocabularyEditResult.Success);
                Program.Check(model.Words[^2].Word == phrase);
                Program.Check(new VocabularyRepository().Load(vocabularyPath).Data.Words[^2].Word == phrase &&
                    new VocabularyRepository().Load(vocabularyPath).Data.Words[0].Family!.SequenceEqual(["was stuck in"]));

                entry = model.Words[^2].Entry;
                Program.Check(model.TryDeleteWord(entry) == VocabularyEditResult.Success);
                Program.Check(!model.Words.Any(w => w.Word == phrase));
                Program.Check(!new VocabularyRepository().Load(vocabularyPath).Data.Words.Any(w => w.Word == phrase));

                Program.Check(model.TryImportVocabulary(importPath) == VocabularyEditResult.Success);
                Program.Check(model.Words.Count == originalCount &&
                    new VocabularyRepository().Load(vocabularyPath).Data.Words[0].Family!.SequenceEqual(["was stuck in"]));

                string current = File.ReadAllText(vocabularyPath);
                File.AppendAllText(vocabularyPath, "\n");
                string externallyEdited = File.ReadAllText(vocabularyPath);
                Program.Check(model.TryAddWord(phrase) == VocabularyEditResult.Failed);
                Program.Check(File.ReadAllText(vocabularyPath) == externallyEdited);
                Program.Check(model.Words.Count == originalCount);
                Program.Check(model.TryMoveWord(model.Words[0].Entry, -1) == VocabularyEditResult.Unavailable);
            }
            finally
            {
                window?.Close();
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
    }
}
