namespace ReadArticles.Services;

public interface IAudioPlayer : IDisposable
{
    event Action<long>? Opened;
    event Action<long>? Ended;
    event Action<long, string>? Failed;
    double Volume { get; set; }
    bool IsOpen { get; }
    TimeSpan Position { get; set; }
    TimeSpan Duration { get; }
    void Play(string absolutePath, long request);
    void Pause();
    void Resume();
    void Stop();
}
public enum PlaybackState { Stopped, Playing, Paused }
public sealed record AudioItem(string Path);
