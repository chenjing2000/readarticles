using System.Windows.Media;
using System.Windows;
using ReadArticles.Services;
namespace ReadArticles.Infrastructure;

public sealed class WpfAudioPlayer : IAudioPlayer
{
    private readonly MediaPlayer media = new MediaPlayer();
    private long activeRequest;
    private EventHandler? endedHandler;
    private EventHandler? openedHandler;
    private EventHandler<ExceptionEventArgs>? failedHandler;
    public event Action<long>? Ended;
    public event Action<long>? Opened;
    public event Action<long, string>? Failed;
    public bool IsOpen { get; private set; }
    public double Volume { get => media.Volume; set => media.Volume = Math.Clamp(value, 0, 1); }
    public TimeSpan Position { get => media.Position; set => media.Position = value; }
    public TimeSpan Duration => media.NaturalDuration.HasTimeSpan ? media.NaturalDuration.TimeSpan : TimeSpan.Zero;

    public void Play(string absolutePath, long request)
    {
        Stop();
        activeRequest = request;
        // Each callback captures its request so an old media event cannot affect a new playback.
        openedHandler = (_, _) =>
        {
            if (activeRequest != request) return;
            IsOpen = true;
            Opened?.Invoke(request);
        };
        endedHandler = (_, _) => { if (activeRequest == request) Ended?.Invoke(request); };
        failedHandler = (_, e) =>
        {
            if (activeRequest != request) return;
            IsOpen = false;
            Failed?.Invoke(request, e.ErrorException.Message);
        };
        media.MediaEnded += endedHandler;
        media.MediaOpened += openedHandler;
        media.MediaFailed += failedHandler;
        media.Open(new Uri(absolutePath, UriKind.Absolute));
        media.Play();
    }

    public void Pause() => media.Pause();
    public void Resume() => media.Play();
    public void Stop()
    {
        activeRequest = 0;
        IsOpen = false;
        if (endedHandler is not null) media.MediaEnded -= endedHandler;
        if (openedHandler is not null) media.MediaOpened -= openedHandler;
        if (failedHandler is not null) media.MediaFailed -= failedHandler;
        endedHandler = null;
        openedHandler = null;
        failedHandler = null;
        media.Close();
    }
    public void Dispose() => Stop();
}
