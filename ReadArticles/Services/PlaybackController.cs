namespace ReadArticles.Services;
using System.IO;

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
    public string? CurrentSid { get; private set; }
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
                !(string.Equals(extension, ".mp3", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase)) ||
                !File.Exists(item.Path) || new FileInfo(item.Path).Length == 0)
            {
                Message?.Invoke("缺少或无效 MP3/WAV：" + item.Path);
                return;
            }
        }
        catch (IOException error) { Message?.Invoke(error.Message); return; }
        catch (UnauthorizedAccessException error) { Message?.Invoke(error.Message); return; }
        Owner = owner;
        CurrentSid = item.Sid;
        State = PlaybackState.Playing;
        long token = ++request;
        try
        {
            player.Play(item.Path, token);
            Changed?.Invoke();
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            OnFailed(token, error.Message);
        }
    }

    private void OnOpened(long token)
    {
        if (token == request && State != PlaybackState.Stopped) Changed?.Invoke();
    }

    private void OnEnded(long token)
    {
        if (token != request || State == PlaybackState.Stopped) return;
        Stop();
    }

    private void OnFailed(long token, string error)
    {
        if (token != request || State == PlaybackState.Stopped) return;
        Stop();
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
        CurrentSid = null;
        Owner = "";
        State = PlaybackState.Stopped;
        Changed?.Invoke();
    }
}
