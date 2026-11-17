using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using ReadArticles.Infrastructure;

namespace ReadArticles;

public partial class MainWindow
{
    private readonly WpfAudioPlayer player = new WpfAudioPlayer();
    private readonly WpfAudioPlayer pronunciationPlayer = new WpfAudioPlayer();
    private readonly DispatcherTimer progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
    private bool updatingProgress;
    private bool draggingProgress;
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

    private void ProgressTimerTick(object? sender, EventArgs e) => UpdateProgress();

    private void RefreshPlayback()
    {
        foreach (var reader in readers) reader.SetPlayingSegment(ViewModel.PlayingSid);
        ReadAllButton.IsEnabled = ViewModel.Article != null;
        StopButton.IsEnabled = ViewModel.Playback.IsArticleActive;
        bool pause = ViewModel.Playback.IsArticlePlaying;
        if (!ReadAllButton.IsEnabled) ReadAllImage.Source = UiIcons.DisabledPlay;
        else if (pause) ReadAllImage.Source = UiIcons.Pause;
        else ReadAllImage.Source = UiIcons.Play;
        StopImage.Source = StopButton.IsEnabled ? UiIcons.Stop : UiIcons.DisabledStop;
        if (pause)
        {
            ReadAllButton.ToolTip = "暂停文章音频";
        }
        else if (ViewModel.Playback.IsArticlePaused)
        {
            ReadAllButton.ToolTip = "继续文章音频";
        }
        else
        {
            ReadAllButton.ToolTip = "播放文章音频";
        }
        System.Windows.Automation.AutomationProperties.SetName(ReadAllButton, (string)ReadAllButton.ToolTip);
        // One existing timer; use finer updates only for playing Script text.
        progressTimer.Interval = scriptMode && ViewModel.Playback.IsArticlePlaying
            ? TimeSpan.FromMilliseconds(33) : TimeSpan.FromMilliseconds(100);
        if (!ViewModel.Playback.IsArticleActive)
        {
            progressTimer.Stop();
            draggingProgress = false;
        }
        else
        {
            progressTimer.Start();
        }
        UpdateProgress();
    }

    private void ArticleAudioClicked(object sender, RoutedEventArgs e) => ViewModel.ToggleArticleAudio();

    private void StopClicked(object sender, RoutedEventArgs e) => ViewModel.StopArticleAudio();

    private void PlaybackKeyDown(object sender, KeyEventArgs e)
    {
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
        UpdateProgress();
    }

    private void UpdateProgress()
    {
        var playback = ViewModel.Playback;
        bool active = playback.IsArticleActive;
        PlaybackProgress.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        PlaybackProgress.IsEnabled = playback.CanSeekArticle;
        if (draggingProgress && active)
        {
            UpdatePlaybackTime();
            UpdateScriptColors();
            return;
        }
        updatingProgress = true;
        try
        {
            PlaybackProgress.Maximum = PlaybackProgress.IsEnabled ? playback.ArticleDuration : 1;
            PlaybackProgress.Value = Math.Clamp(playback.ArticlePosition, 0, PlaybackProgress.Maximum);
        }
        finally
        {
            updatingProgress = false;
        }
        UpdatePlaybackTime();
        UpdateScriptColors();
    }

    private void ProgressChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!updatingProgress)
        {
            ViewModel.Playback.SeekArticle(e.NewValue);
            UpdatePlaybackTime();
            UpdateScriptColors();
        }
    }

    private void UpdatePlaybackTime() => PlaybackTime.Text =
        FormatPlaybackTime(PlaybackProgress.Value) + "/" + FormatPlaybackTime(ViewModel.Playback.ArticleDuration);

    private static string FormatPlaybackTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        if (time.TotalHours > 1) return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
        return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
    }

    private void ProgressDragStarted(object sender, DragStartedEventArgs e) => draggingProgress = ViewModel.Playback.CanSeekArticle;

    private void ProgressDragCompleted(object sender, DragCompletedEventArgs e)
    {
        draggingProgress = false;
        UpdateProgress();
    }
}
