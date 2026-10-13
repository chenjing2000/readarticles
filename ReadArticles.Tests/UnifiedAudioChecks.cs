using System.IO;
using System.Text.Json.Nodes;
using ReadArticles.Models;
using ReadArticles.Services;
using ReadArticles.ViewModels;

internal static class UnifiedAudioChecks
{
    internal static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "ReadArticles-audio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Program.Run("unified ASCII stems collapse punctuation and preserve empty-stem words", () =>
            {
                foreach (var pair in new[] { (" One's Own ", "one_s_own"), ("A / B", "a_b"), ("_A!", "a"), ("café", "caf"), ("!!!", "") })
                    Program.Check(VocabularyRepository.AudioStem(pair.Item1) == pair.Item2);
                VocabularyRepository.Validate(new VocabularyData { Words = [new() { Word = "!!!" }, new() { Word = "well-being" }, new() { Word = "well being" }] });
            });
            Program.Run("legacy word audio is ignored and removed on save while other metadata survives", () =>
            {
                string path = Path.Combine(root, "words.json");
                File.WriteAllText(path, """{"filetype":"vocabulary","words":[{"word":"test","audio":42,"source":"kept","meanings":[]}]}""");
                var repo = new VocabularyRepository();
                var snapshot = repo.Load(path);
                repo.Save(path, snapshot.Data, snapshot.Fingerprint);
                var word = JsonNode.Parse(File.ReadAllText(path))!["words"]![0]!;
                Program.Check(word["audio"] == null && (string?)word["source"] == "kept");
            });
            Program.Run("sentence audio derives SID without paths and blank detection uses placeholders", () =>
            {
                string path = Path.Combine(root, "Article.json");
                File.WriteAllText(path, """{"filetype":"passage","next_sid":2,"paragraphs":[{"paragraph":[{"sid":"s001","text":"Test.","audio":42}]}]}""");
                var article = new ArticleRepository().Load(path);
                Program.Check(!article.IsBlank);
                Directory.CreateDirectory(Path.Combine(root, "audio_sentences"));
                string wav = Path.Combine(root, "audio_sentences", "s001_uk.wav");
                File.WriteAllBytes(wav, [1]);
                Program.Check(new AudioResources().Segment(root, article.Segments.First(), "uk") == wav);
                string mp3 = Path.ChangeExtension(wav, ".mp3"); File.WriteAllBytes(mp3, [1]);
                Program.Check(new AudioResources().Segment(root, article.Segments.First(), "uk") == mp3);
                File.WriteAllText(path, File.ReadAllText(path).Replace("Test.", "Test [[1]]."));
                var blank = new ArticleRepository().Load(path);
                Program.Check(blank.IsBlank && !blank.Segments.First().HasAudio);
            });
            Program.Run("word lookup uses audio MP3 before WAV and ignores old directory", () =>
            {
                var resources = new AudioResources();
                var entry = VocabularyRepository.NewEntry("One's Own");
                Directory.CreateDirectory(Path.Combine(root, "audio_vocabulary"));
                File.WriteAllBytes(Path.Combine(root, "audio_vocabulary", "one_s_own_uk.mp3"), [1]);
                Program.Check(!AudioResources.IsAvailable(resources.Vocabulary(root, entry, "uk")));
                Directory.CreateDirectory(Path.Combine(root, "audio"));
                string wav = Path.Combine(root, "audio", "one_s_own_uk.wav");
                File.WriteAllBytes(wav, [1]);
                Program.Check(resources.Vocabulary(root, entry, "uk") == wav);
                string mp3 = Path.ChangeExtension(wav, ".mp3");
                File.WriteAllBytes(mp3, [1]);
                Program.Check(resources.Vocabulary(root, entry, "uk") == mp3);
                File.WriteAllBytes(mp3, []);
                Program.Check(resources.Vocabulary(root, entry, "uk") == mp3 && !AudioResources.IsAvailable(mp3));
            });
            Program.Run("whole article chooses MP3 first and falls back to sorted WAV", () =>
            {
                string folder = Path.Combine(root, "whole"); Directory.CreateDirectory(folder);
                string wav = Path.Combine(folder, "a.wav"); File.WriteAllBytes(wav, [1]);
                var resources = new AudioResources();
                Program.Check(resources.ArticleFile(folder) == wav);
                string mp3 = Path.Combine(folder, "z.mp3"); File.WriteAllBytes(mp3, [1]);
                Program.Check(resources.ArticleFile(folder) == mp3);
            });
            Program.Run("collision words remain editable and only first owns audio after reorder", () =>
            {
                string path = Path.Combine(root, "Collision.json");
                File.WriteAllText(path, """{"filetype":"passage","next_sid":2,"paragraphs":[{"paragraph":[{"sid":"s001","text":"Test."}]}]}""");
                File.WriteAllText(Path.ChangeExtension(path, ".vocabulary.json"), """{"filetype":"vocabulary","words":[{"word":"well-being","meanings":[]},{"word":"well being","meanings":[]},{"word":"!!!","meanings":[]}]}""");
                using var first = new AudioChecks.FakePlayer(); using var second = new AudioChecks.FakePlayer();
                var model = new MainViewModel(new PlaybackController(first), new PlaybackController(second));
                Program.Check(model.OpenArticle(path) && model.Words.Count == 3);
                var entries = model.Article!.Vocabulary.Data.Words;
                // The first reserves the stem before any file exists.
                Program.Check(AudioResources.OwnsWordAudio(model.Article, entries[0]) && !AudioResources.OwnsWordAudio(model.Article, entries[1]));
                File.WriteAllBytes(Path.Combine(root, "audio", "well_being_uk.mp3"), [1]);
                File.WriteAllBytes(Path.Combine(root, "audio", "well_being_us.wav"), [1]);
                File.WriteAllBytes(Path.Combine(root, "audio", "_uk.mp3"), [1]);
                Program.Check(model.OpenArticle(path));
                entries = model.Article!.Vocabulary.Data.Words;
                Program.Check(model.Words[0].CanPlayUk && model.Words[0].CanPlayUs);
                Program.Check(!model.Words[1].CanPlayUk && !model.Words[1].CanPlayUs);
                Program.Check(!model.Words[2].CanPlayUk && !model.Words[2].CanPlayUs);
                model.PlayWord(entries[1], "uk");
                Program.Check(model.PlaybackState == PlaybackState.Stopped && model.Status.Contains("音频资格"));
                var formerFirst = entries[0]; var formerSecond = entries[1];
                model.PlayWord(formerFirst, "uk");
                Program.Check(model.PlaybackState == PlaybackState.Playing);
                model.MoveWord(formerSecond, -1);
                Program.Check(AudioResources.OwnsWordAudio(model.Article!, formerSecond) && !AudioResources.OwnsWordAudio(model.Article!, formerFirst));
                Program.Check(model.PlaybackState == PlaybackState.Stopped && model.Words[0].CanPlayUk && !model.Words[1].CanPlayUk);
                model.DeleteWord(formerSecond);
                Program.Check(AudioResources.OwnsWordAudio(model.Article!, formerFirst));
            });
        }
        finally { Directory.Delete(root, true); }
    }
}
