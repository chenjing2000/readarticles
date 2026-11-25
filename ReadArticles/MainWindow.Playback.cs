using System.Windows.Input;
using ReadArticles.Infrastructure;

namespace ReadArticles;

public partial class MainWindow
{
    private readonly WpfAudioPlayer player = new();
    private readonly WpfAudioPlayer pronunciationPlayer = new();
    private static bool IsPlaybackShortcut(KeyEventArgs e) =>
        Keyboard.Modifiers == ModifierKeys.None && e.Key is (Key.Space or Key.Left or Key.Right);

    private void PlaybackShortcutKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsPlaybackShortcut(e)) return;
        e.Handled = true;
        if (webReaderHost?.IsUnavailable == true || ViewModel.Article is null) return;
        if (e.Key == Key.Space)
        {
            if (e.IsRepeat) return;
            ViewModel.ToggleArticleAudio();
        }
        else
            ViewModel.SeekArticleRelative(e.Key == Key.Left);
        webReaderBridge?.SendPlayback();
    }

    private void PlaybackShortcutKeyUp(object sender, KeyEventArgs e)
    {
        if (IsPlaybackShortcut(e)) e.Handled = true;
    }

    private void ClosePlayback()
    {
        ViewModel.Stop();
        player.Dispose();
        pronunciationPlayer.Dispose();
    }

}
