using ReadArticles.Infrastructure;
using ReadArticles.Services;
using System.IO;

internal static class RealMediaChecks
{
    internal static void Run()
    {
        Program.Run("real WAV word and sentence lookup decode through playback controller", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "ReadArticles-wav-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "audio"));
                Directory.CreateDirectory(Path.Combine(root, "audio_sentences"));
                string word = Path.Combine(root, "audio", "test_uk.wav");
                using (var writer = new BinaryWriter(File.Create(word)))
                {
                    const int rate = 22050, frames = rate;
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + frames * 2);
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                    writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
                    writer.Write((short)2); writer.Write((short)16); writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(frames * 2);
                    for (int i = 0; i < frames; i++) writer.Write((short)0);
                }
                string sentence = Path.Combine(root, "audio_sentences", "s001_uk.wav"); File.Copy(word, sentence);
                var resources = new AudioResources();
                foreach (string path in new[] { resources.Vocabulary(root, VocabularyRepository.NewEntry("test"), "uk"), resources.Segment(root, new ReadArticles.Models.Segment("s001", "Test."), "uk") })
                {
                    using var player = new WpfAudioPlayer { Volume = 0 };
                    var controller = new PlaybackController(player);
                    string? error = null; controller.Message += message => error = message;
                    controller.Play(new AudioItem(path), "word:uk:test");
                    WpfTestHelpers.Wait(() => player.IsOpen || error != null);
                    Program.Check(error == null && player.Duration > TimeSpan.Zero);
                    WpfTestHelpers.Wait(() => controller.State == PlaybackState.Stopped || error != null);
                    Program.Check(error == null && controller.State == PlaybackState.Stopped);
                }
            }
            finally { Directory.Delete(root, true); }
        });
        foreach (string accent in new[] { "uk", "us" })
        {
            Program.Run($"real MP3 segment {accent}: decode, position, pause/resume and end", () =>
            {
                var article = new ArticleRepository().Load(TestData.ArticlePath);
                string path = new AudioResources().Segment(article.Directory, article.Segments.First(), accent);
                Verify(path, true);
            });
            Program.Run($"real MP3 vocabulary {accent}: decode, position and end", () =>
            {
                var article = new ArticleRepository().Load(TestData.ArticlePath);
                string path = new AudioResources().Vocabulary(article.Directory, article.Vocabulary.Data.Words[0], accent);
                Verify(path, false);
            });
        }
        Program.Run("real MP3 controller clears SID and ownership after completion", () =>
        {
            var article = new ArticleRepository().Load(TestData.ArticlePath);
            var segment = article.Segments.First();
            string path = new AudioResources().Segment(article.Directory, segment, "uk");
            using var player = new WpfAudioPlayer { Volume = 0 };
            var controller = new PlaybackController(player);
            string? error = null;
            controller.Message += message => error = message;
            controller.Play(new AudioItem(path, segment.Sid), "segment:" + segment.Sid);
            WpfTestHelpers.Wait(() => player.IsOpen || error != null);
            Program.Check(error == null && controller.CurrentSid == segment.Sid);
            player.Position = player.Duration - TimeSpan.FromMilliseconds(100);
            WpfTestHelpers.Wait(() => controller.State == PlaybackState.Stopped || error != null);
            Program.Check(error == null && controller.CurrentSid == null && controller.Owner == "");
        });
    }
    private static void Verify(string path, bool pause)
    {
        using var player = new WpfAudioPlayer { Volume = 0 };
        bool opened = false;
        bool ended = false;
        string? error = null;
        player.Opened += token => opened = token == 42;
        player.Ended += token => ended = token == 42;
        player.Failed += (_, message) => error = message;
        player.Play(path, 42);
        WpfTestHelpers.Wait(() => opened || error is not null);
        Program.Check(error is null && player.Duration > TimeSpan.Zero);
        WpfTestHelpers.Wait(() => player.Position > TimeSpan.FromMilliseconds(120) || error is not null);
        Program.Check(error is null);
        if (pause)
        {
            player.Pause();
            WpfTestHelpers.Pump(120);
            var before = player.Position;
            WpfTestHelpers.Pump(250);
            Program.Check(Math.Abs((player.Position - before).TotalMilliseconds) < 60);
            player.Resume();
            WpfTestHelpers.Wait(() => player.Position > before + TimeSpan.FromMilliseconds(100));
        }
        player.Position = player.Duration - TimeSpan.FromMilliseconds(100);
        WpfTestHelpers.Wait(() => ended || error is not null);
        Program.Check(ended && error is null);
    }
}
