using System.Windows.Threading;
using System.Windows.Input;
using ReadArticles.Infrastructure;

namespace ReadArticles;

public partial class MainWindow
{
    private readonly WpfAudioPlayer player = new();
    private readonly WpfAudioPlayer pronunciationPlayer = new();
    // The C# player is the only media clock. WebView2 owns all progress visuals.
    private readonly DispatcherTimer progressTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    private static bool IsPlaybackShortcut(KeyEventArgs e) =>
        Keyboard.Modifiers == ModifierKeys.None && e.Key is (Key.Space or Key.Left or Key.Right);

    private void PlaybackShortcutKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsPlaybackShortcut(e)) return;
        e.Handled = true;
        if (webPilotClosed || webPilotFailureShown || ViewModel.Article is null) return;
        if (e.Key == Key.Space)
        {
            if (e.IsRepeat) return;
            ViewModel.ToggleArticleAudio();
        }
        else
            SeekArticleRelative(e.Key == Key.Left);
        SendWebPilotPlayback();
    }

    private void PlaybackShortcutKeyUp(object sender, KeyEventArgs e)
    {
        if (IsPlaybackShortcut(e)) e.Handled = true;
    }

    private void SeekArticleRelative(bool backward)
    {
        if (!ViewModel.Playback.CanSeekArticle) return;
        double step = backward ? -settings.BackwardStep : settings.ForwardStep;
        ViewModel.Playback.SeekArticle(Math.Clamp(ViewModel.Playback.ArticlePosition + step,
            0, ViewModel.Playback.ArticleDuration));
    }

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
        progressTimer.Interval = ViewModel.IsScriptMode && ViewModel.Playback.IsArticlePlaying
            ? TimeSpan.FromMilliseconds(33) : TimeSpan.FromMilliseconds(100);
        if (!webPilotFailureShown && ViewModel.Playback.IsArticleActive) progressTimer.Start();
        else progressTimer.Stop();
    }

}
