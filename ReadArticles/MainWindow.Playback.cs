using System.Windows.Input;
using System.Windows.Threading;
using ReadArticles.Infrastructure;

namespace ReadArticles;

public partial class MainWindow
{
    private readonly WpfAudioPlayer player = new();
    private readonly WpfAudioPlayer pronunciationPlayer = new();
    // The C# player is the only media clock. WebView2 owns all progress visuals.
    private readonly DispatcherTimer progressTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    private void InitializePlayback()
    {
        ViewModel.PlaybackChanged += RefreshPlayback;
        progressTimer.Tick += ProgressTimerTick;
    }

    private void ClosePlayback()
    {
        progressTimer.Stop();
        ViewModel.PlaybackChanged -= RefreshPlayback;
        progressTimer.Tick -= ProgressTimerTick;
        ViewModel.Stop();
        player.Dispose();
        pronunciationPlayer.Dispose();
    }

    private void ProgressTimerTick(object? sender, EventArgs e)
    {
        if (!webPilotFailureShown && ViewModel.Playback.IsArticleActive)
            SendWebPilotPlayback();
    }

    private void RefreshPlayback()
    {
        progressTimer.Interval = webPilotScriptMode && ViewModel.Playback.IsArticlePlaying
            ? TimeSpan.FromMilliseconds(33) : TimeSpan.FromMilliseconds(100);
        if (!webPilotFailureShown && ViewModel.Playback.IsArticleActive) progressTimer.Start();
        else progressTimer.Stop();
    }

    // Keys inside WebView2 are handled by reader.js; WPF handles the native tree/menu.
    private void PlaybackKeyDown(object sender, KeyEventArgs e)
    {
        if (webPilotFailureShown || ReaderWebView.IsKeyboardFocusWithin) return;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key != Key.Left && key != Key.Right && key != Key.Space) return;
        e.Handled = true;
        if (key == Key.Space)
        {
            if (!e.IsRepeat && ViewModel.Article != null) ViewModel.ToggleArticleAudio();
            return;
        }
        var playback = ViewModel.Playback;
        if (!playback.CanSeekArticle) return;
        double step = key == Key.Left ? -settings.BackwardStep : settings.ForwardStep;
        playback.SeekArticle(playback.ArticlePosition + step);
    }
}
