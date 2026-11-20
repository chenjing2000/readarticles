namespace ReadArticles.Services;
using System.IO;
using ReadArticles.Infrastructure;

public sealed class PlaybackController
{
    private readonly IAudioPlayer player;
    private long request;
    public PlaybackController(IAudioPlayer player)
    {
        this.player = player;
        player.Opened += OnOpened;
        player.Ended += OnEnded;
        player.Failed += OnFailed;
    }
    public PlaybackState State { get; private set; }
    public string Owner { get; private set; } = "";
    private IReadOnlyList<string>? candidates;
    private int nextCandidate;
    private bool awaitingOpen;
    public bool IsOpen => player.IsOpen;
    public TimeSpan Duration => player.Duration;
    public TimeSpan Position => player.Position;
    public event Action? Changed;
    public event Action<string>? Message;
    public bool ToggleCurrent(string owner)
    {
        if (Owner != owner || State == PlaybackState.Stopped) return false;
        if (State == PlaybackState.Playing) Pause();
        else Resume();
        return true;
    }

    public void Seek(double seconds)
    {
        if (State == PlaybackState.Stopped || !IsOpen || Duration <= TimeSpan.Zero) return;
        player.Position = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, Duration.TotalSeconds));
    }

    public void Play(AudioItem item, string owner)
    {
        Stop();
        try
        {
            string extension = Path.GetExtension(item.Path);
            if (!Path.IsPathFullyQualified(item.Path) ||
                !(string.Equals(extension, ".m4a", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".mp3", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase)) ||
                !File.Exists(item.Path) || new FileInfo(item.Path).Length == 0)
            {
                AppLogger.Default.Warning("AudioFileUnavailable");
                Message?.Invoke("缺少或无效 M4A/MP3/WAV 音频文件。");
                return;
            }
        }
        catch (IOException error) { AppLogger.Default.Warning("AudioFileAccessFailed", error); Message?.Invoke(error.Message); return; }
        catch (UnauthorizedAccessException error) { AppLogger.Default.Warning("AudioFileAccessDenied", error); Message?.Invoke(error.Message); return; }
        Owner = owner;
        State = PlaybackState.Playing;
        long token = ++request;
        try
        {
            player.Play(item.Path, token);
            Changed?.Invoke();
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            AppLogger.Default.Warning("AudioPlaybackStartFailed", error);
            OnFailed(token, error.Message);
        }
    }

    public void PlayCandidates(IReadOnlyList<string> files, string owner)
    {
        Stop();
        candidates = files;
        nextCandidate = 0;
        Owner = owner;
        TryNextCandidate();
    }

    private void TryNextCandidate()
    {
        while (candidates is not null && nextCandidate < candidates.Count)
        {
            string path = candidates[nextCandidate++];
            try
            {
                string extension = Path.GetExtension(path);
                if (!Path.IsPathFullyQualified(path) ||
                    !(extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase) ||
                      extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
                      extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)) ||
                    !File.Exists(path) || new FileInfo(path).Length == 0)
                    continue;
                player.Stop();
                long token = ++request;
                awaitingOpen = true;
                State = PlaybackState.Playing;
                player.Play(path, token);
                if (token == request && State != PlaybackState.Stopped)
                    Changed?.Invoke();
                return;
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
            {
                AppLogger.Default.Warning("AudioCandidateOpenFailed", error);
            }
        }
        Stop();
        Message?.Invoke("没有可以播放的文章音频文件（M4A/MP3/WAV）。");
    }

    private void OnOpened(long token)
    {
        if (token != request || State == PlaybackState.Stopped) return;
        awaitingOpen = false;
        candidates = null;
        Changed?.Invoke();
    }

    private void OnEnded(long token)
    {
        if (token != request || State == PlaybackState.Stopped) return;
        Stop();
    }

    private void OnFailed(long token, string error)
    {
        if (token != request || State == PlaybackState.Stopped) return;
        if (awaitingOpen && candidates is not null)
        {
            awaitingOpen = false;
            TryNextCandidate();
            return;
        }
        Stop();
        AppLogger.Default.Warning("AudioPlaybackFailed");
        Message?.Invoke($"音频文件无法播放：{error}");
    }

    public void Pause()
    {
        if (State != PlaybackState.Playing) return;
        player.Pause();
        State = PlaybackState.Paused;
        Changed?.Invoke();
    }

    public void Resume()
    {
        if (State != PlaybackState.Paused) return;
        player.Resume();
        State = PlaybackState.Playing;
        Changed?.Invoke();
    }

    public void Stop()
    {
        request++;
        player.Stop();
        candidates = null;
        nextCandidate = 0;
        awaitingOpen = false;
        Owner = "";
        State = PlaybackState.Stopped;
        Changed?.Invoke();
    }
}
