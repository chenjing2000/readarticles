using ReadArticles.Services;
using System.IO;
using ReadArticles.ViewModels;
using ReadArticles.Infrastructure;
using ReadArticles.Models;

internal static class AudioChecks
{
    internal static void Run()
    {
        Program.Run("resource paths reject traversal and absolute references", () =>
        {
            string resourceRoot = Directory.GetCurrentDirectory();
            Program.Check(ResourcePaths.Resolve(resourceRoot, "audio_sentences/s001_uk.mp3").StartsWith(resourceRoot));
            Reject("../escape.mp3");
            Reject("C:/outside.mp3");
            Reject("audio_sentences/file.mp3:stream");
            void Reject(string relative)
            {
                try { ResourcePaths.Resolve(resourceRoot, relative); }
                catch (ArgumentException) { return; }
                throw new Exception("Unsafe path accepted: " + relative);
            }
        });
        var root = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "test-media", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var first = Path.Combine(root, "a.mp3");
        var second = Path.Combine(root, "b.mp3");
        File.WriteAllBytes(first, [1]);
        File.WriteAllBytes(second, [1]);
        try
        {
            Program.Run("independent players retain article position and coordinate word/segment playback", () =>
            {
                string dualRoot = Path.Combine(root, "dual-player");
                Directory.CreateDirectory(dualRoot);
                File.Copy(first, Path.Combine(dualRoot, "a.mp3"));
                File.Copy(second, Path.Combine(dualRoot, "b.mp3"));
                string passage = Path.Combine(dualRoot, "Dual.json");
                File.Copy(TestData.ArticlePath, passage);
                File.WriteAllText(Path.ChangeExtension(passage, ".vocabulary.json"), """{"filetype":"vocabulary","words":[{"word":"test","meanings":[]}]}""");
                var articlePlayer = new FakePlayer();
                var pronunciationPlayer = new FakePlayer();
                var articleController = new PlaybackController(articlePlayer);
                var pronunciationController = new PlaybackController(pronunciationPlayer);
                var model = new MainViewModel(articleController, pronunciationController);
                Program.Check(model.OpenArticle(passage));
                var entry = model.Article!.Vocabulary.Data.Words[0];
                Directory.CreateDirectory(Path.Combine(dualRoot, "audio"));
                File.Copy(second, Path.Combine(dualRoot, "audio", VocabularyRepository.AudioStem(entry.Word) + "_uk.mp3"));
                string segmentPath = new AudioResources().Segment(dualRoot, model.Article!.Segments.First(), "uk");
                Directory.CreateDirectory(Path.GetDirectoryName(segmentPath)!);
                File.WriteAllBytes(segmentPath, new byte[] { 1 });
                model.ToggleArticleAudio();
                articlePlayer.Position = TimeSpan.FromSeconds(4);
                model.PlayWord(entry, "uk");
                Program.Check(model.Playback.IsArticlePaused && model.Playback.ArticlePosition == 4);
                Program.Check(pronunciationController.State == PlaybackState.Playing && pronunciationPlayer.Played.Count == 1);
                model.PlaySegment(model.Article.Segments.First().Sid);
                Program.Check(articlePlayer.Position.TotalSeconds == 4 && articlePlayer.Pauses == 1);
                Program.Check(pronunciationPlayer.Played.Count == 2 && model.PlayingSid == model.Article.Segments.First().Sid);
                long stale = pronunciationPlayer.Played.Last().Request;
                model.ToggleArticleAudio();
                Program.Check(model.Playback.IsArticlePlaying && model.Playback.ArticlePosition == 4);
                Program.Check(pronunciationController.State == PlaybackState.Stopped && !pronunciationPlayer.IsOpen && model.PlayingSid == null);
                Program.Check(articlePlayer.Played.Count == 1 && articlePlayer.Resumes == 1);
                pronunciationPlayer.Complete(stale);
                pronunciationPlayer.Fail(stale);
                Program.Check(model.Playback.IsArticlePlaying);
                model.PlayWord(entry, "uk");
                model.Playback.SeekArticle(6);
                Program.Check(articlePlayer.Position.TotalSeconds == 6 && pronunciationPlayer.Position == TimeSpan.Zero);
                pronunciationPlayer.Complete(pronunciationPlayer.Played.Last().Request);
                Program.Check(model.Playback.IsArticlePaused && model.Playback.ArticlePosition == 6);
                model.PlayWord(entry, "uk");
                model.StopArticleAudio();
                Program.Check(pronunciationController.State == PlaybackState.Playing && articleController.State == PlaybackState.Stopped);
                model.ToggleArticleAudio();
                Program.Check(pronunciationController.State == PlaybackState.Stopped && model.Playback.IsArticlePlaying);
                model.PlayWord(new VocabularyEntry { Word = "missing" }, "uk");
                Program.Check(model.Playback.IsArticlePlaying && model.Status.Contains("音频资格"));
                model.PlayWord(entry, "uk");
                model.Stop();
                Program.Check(articleController.State == PlaybackState.Stopped && pronunciationController.State == PlaybackState.Stopped);
            });
            Program.Run("reader playback exposes progress and seeking only for article ownership", () =>
            {
                var player = new FakePlayer();
                var controller = new PlaybackController(player);
                var pronunciationPlayer = new FakePlayer();
                var pronunciation = new PlaybackController(pronunciationPlayer);
                var playback = new ReaderPlayback(controller, pronunciation);
                controller.Play(new AudioItem(first), "passage");
                Program.Check(playback.IsArticlePlaying && playback.CanSeekArticle && playback.ArticleDuration == 10);
                playback.SeekArticle(20);
                Program.Check(playback.ArticlePosition == 10);
                playback.SeekArticle(-1);
                Program.Check(playback.ArticlePosition == 0);
                controller.Pause();
                playback.SeekArticle(4);
                Program.Check(playback.IsArticlePaused && playback.ArticlePosition == 4);
                controller.Stop();
                foreach (string owner in new[] { "word:uk:life", "segment:s001" })
                {
                    pronunciation.Play(new AudioItem(second), owner);
                    pronunciationPlayer.Position = TimeSpan.FromSeconds(2);
                    playback.SeekArticle(8);
                    playback.StopArticle();
                    Program.Check(!playback.IsArticleActive && !playback.CanSeekArticle && playback.ArticlePosition == 0 && playback.ArticleDuration == 0);
                    Program.Check(pronunciation.State == PlaybackState.Playing && pronunciationPlayer.Position.TotalSeconds == 2);
                }
                controller.Stop();
                playback.SeekArticle(5);
                Program.Check(!playback.CanSeekArticle && player.Position == TimeSpan.Zero);
            });
            Program.Run("stale media-open callbacks do not refresh a newer target", () =>
            {
                var player = new FakePlayer();
                var controller = new PlaybackController(player);
                int changes = 0;
                controller.Changed += () => changes++;
                controller.Play(new AudioItem(first), "passage");
                long stale = player.Played.Last().Request;
                controller.Play(new AudioItem(second), "word:uk:life");
                int before = changes;
                player.Open(stale);
                Program.Check(changes == before && controller.Owner == "word:uk:life");
                player.Open(player.Played.Last().Request);
                Program.Check(changes == before + 1);
                controller.Stop();
                before = changes;
                player.Open(player.Played.Last().Request);
                Program.Check(changes == before);
            });
            Program.Run("deleting a word cancels only that word's pronunciation", () =>
            {
                var player = new FakePlayer();
                var controller = new PlaybackController(player);
                var pronunciation = new PlaybackController(new FakePlayer());
                var playback = new ReaderPlayback(controller, pronunciation);
                var entry = VocabularyRepository.NewEntry("life");
                foreach (string owner in new[] { "passage", "segment:s001", "word:uk:afterlife" })
                {
                    var target = owner == "passage" ? controller : pronunciation;
                    target.Play(new AudioItem(first), owner);
                    playback.StopWord(entry);
                    Program.Check(target.Owner == owner && target.State == PlaybackState.Playing);
                }
                foreach (string accent in new[] { "uk", "us" })
                {
                    pronunciation.Play(new AudioItem(first), "word:" + accent + ":life");
                    playback.StopWord(entry);
                    Program.Check(pronunciation.State == PlaybackState.Stopped);
                }
            });
            Program.Run("article controls leave pronunciation alone and keep pause/resume ownership", () =>
            {
                string passage = Path.Combine(root, "Reading.json");
                File.Copy(TestData.ArticlePath, passage);
                var player = new FakePlayer();
                var controller = new PlaybackController(player);
                var pronunciationPlayer = new FakePlayer();
                var pronunciation = new PlaybackController(pronunciationPlayer);
                var model = new MainViewModel(controller, pronunciation);
                Program.Check(model.OpenArticle(passage));
                pronunciation.Play(new AudioItem(second), "word:uk:life");
                model.StopArticleAudio();
                Program.Check(pronunciation.Owner == "word:uk:life" && pronunciation.State == PlaybackState.Playing);
                model.ToggleArticleAudio();
                Program.Check(controller.Owner == "passage" && player.Played.Last().Path == first);
                model.ToggleArticleAudio();
                Program.Check(controller.State == PlaybackState.Paused && player.Pauses == 1);
                model.Accent = "us";
                Program.Check(controller.State == PlaybackState.Paused);
                model.ToggleArticleAudio();
                Program.Check(controller.State == PlaybackState.Playing && player.Resumes == 1);
                model.StopArticleAudio();
                Program.Check(controller.State == PlaybackState.Stopped);
                pronunciation.Play(new AudioItem(second, "s001"), "segment:s001");
                model.StopArticleAudio();
                Program.Check(pronunciation.State == PlaybackState.Playing);
                model.Accent = "uk";
                Program.Check(pronunciation.State == PlaybackState.Stopped);
            });
            Program.Run("missing sibling MP3 preserves pronunciation but missing segment cancels it", () =>
            {
                string directory = Path.Combine(root, "no-sibling");
                Directory.CreateDirectory(directory);
                string passage = Path.Combine(directory, "Reading.json");
                File.Copy(TestData.ArticlePath, passage);
                var player = new FakePlayer();
                var controller = new PlaybackController(player);
                var pronunciationPlayer = new FakePlayer();
                var pronunciation = new PlaybackController(pronunciationPlayer);
                var model = new MainViewModel(controller, pronunciation);
                Program.Check(model.OpenArticle(passage));
                pronunciation.Play(new AudioItem(first), "word:uk:life");
                model.ToggleArticleAudio();
                Program.Check(pronunciation.Owner == "word:uk:life" && pronunciation.State == PlaybackState.Playing && model.Status.Contains("MP3"));
                model.PlaySegment("s001");
                Program.Check(pronunciation.State == PlaybackState.Stopped && model.Status.Contains("MP3"));
            });
            Program.Run("missing MP3 leaves the player stopped and reports a warning", () =>
            {
                var player = new FakePlayer();
                var controller = new PlaybackController(player);
                string? warning = null;
                controller.Message += message => warning = message;
                controller.Play(new AudioItem(Path.Combine(root, "missing.mp3")), "passage");
                Program.Check(player.Played.Count == 0 && controller.State == PlaybackState.Stopped && warning is not null);
            });
            Program.Run("single audio pause/resume and completion clear ownership", () =>
            {
                var player = new FakePlayer();
                var controller = new PlaybackController(player);
                controller.Play(new AudioItem(first, "s001"), "segment:s001");
                Program.Check(controller.CurrentSid == "s001");
                Program.Check(controller.ToggleCurrent("segment:s001"));
                Program.Check(controller.State == PlaybackState.Paused && player.Pauses == 1);
                Program.Check(controller.ToggleCurrent("segment:s001"));
                Program.Check(controller.State == PlaybackState.Playing && player.Resumes == 1);
                player.Complete(player.Played.Last().Request);
                Program.Check(controller.State == PlaybackState.Stopped && controller.Owner == "" && controller.CurrentSid == null);
            });
            Program.Run("P19 switching targets ignores old media callbacks", () =>
            {
                var player = new FakePlayer();
                var controller = new PlaybackController(player);
                controller.Play(new AudioItem(first), "passage");
                long stale = player.Played[^1].Request;
                controller.Play(new AudioItem(second), "word:uk:life");
                player.Complete(stale);
                player.Fail(stale);
                Program.Check(controller.Owner == "word:uk:life" && player.Played.Count == 2 && controller.CurrentSid is null);
                controller.Stop();
                player.Complete(player.Played[^1].Request);
                Program.Check(player.Played.Count == 2 && controller.State == PlaybackState.Stopped);
            });
            Program.Run("media failure clears playing state and reports warning", () =>
            {
                var player = new FakePlayer();
                var controller = new PlaybackController(player);
                string? warning = null;
                controller.Message += message => warning = message;
                controller.Play(new AudioItem(first, "s001"), "segment:s001");
                player.Fail(player.Played[^1].Request);
                Program.Check(controller.State == PlaybackState.Stopped && controller.CurrentSid is null && warning is not null);
            });
            Program.Run("reader stale vocabulary entry cancels previous playback target", () =>
            {
                var player = new FakePlayer();
                var controller = new PlaybackController(player);
                var pronunciationPlayer = new FakePlayer();
                var pronunciation = new PlaybackController(pronunciationPlayer);
                var model = new MainViewModel(controller, pronunciation);
                Program.Check(model.OpenArticle(TestData.ArticlePath));
                pronunciation.Play(new AudioItem(first, "s001"), "segment:s001");
                Program.Check(pronunciation.State == PlaybackState.Playing);
                long stale = pronunciationPlayer.Played[^1].Request;
                var invalid = new VocabularyEntry
                {
                    Word = "unsafe"
                };
                model.PlayWord(invalid, "uk");
                Program.Check(pronunciation.State == PlaybackState.Stopped && pronunciation.Owner == "" && pronunciation.CurrentSid is null);
                Program.Check(pronunciationPlayer.Played.Count == 1 && model.Status.Contains("音频资格"));
                pronunciationPlayer.Complete(stale);
                pronunciationPlayer.Fail(stale);
                Program.Check(pronunciation.State == PlaybackState.Stopped && pronunciationPlayer.Played.Count == 1);
            });
            Program.Run("missing audio_sentences never falls back to old audio directory", () =>
            {
                string directory = Path.Combine(root, "no-fallback");
                string old = Path.Combine(directory, "audio");
                Directory.CreateDirectory(old);
                File.WriteAllBytes(Path.Combine(old, "s001_uk.mp3"), [1]);
                var resources = new AudioResources();
                var segment = new Segment("s001", "test", true);
                string asset = resources.Segment(directory, segment, "uk");
                Program.Check(!AudioResources.IsAvailable(asset));
                Directory.CreateDirectory(Path.Combine(directory, "audio_sentences"));
                File.WriteAllBytes(asset, [1]);
                Program.Check(AudioResources.IsAvailable(resources.Segment(directory, segment, "uk")));
            });
        }
        finally { Directory.Delete(root, true); }
    }

    internal sealed class FakePlayer : IAudioPlayer
    {
        public event Action<long>? Opened;
        public event Action<long>? Ended;
        public event Action<long, string>? Failed;
        public List<(string Path, long Request)> Played { get; } = [];
        public int Pauses { get; private set; }
        public int Resumes { get; private set; }
        public double Volume { get; set; }
        public bool IsOpen { get; private set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration { get; } = TimeSpan.FromSeconds(10);
        public void Play(string absolutePath, long request)
        {
            Played.Add((absolutePath, request));
            IsOpen = true;
            Opened?.Invoke(request);
        }
        public void Pause() => Pauses++;
        public void Resume() => Resumes++;
        public void Stop() { IsOpen = false; Position = TimeSpan.Zero; }
        public void Dispose() { }
        public void Complete(long request) => Ended?.Invoke(request);
        public void Open(long request) => Opened?.Invoke(request);
        public void Fail(long request) => Failed?.Invoke(request, "decode failed");
    }
}

