using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ReadArticles.Infrastructure;

namespace ReadArticles.Controls;

/// <summary>A floating, non-modal notification overlay over the reading pane.</summary>
public sealed class ToastHost : Grid
{
    private readonly StackPanel stack = new()
    {
        Orientation = Orientation.Vertical,
        VerticalAlignment = VerticalAlignment.Bottom,
        HorizontalAlignment = HorizontalAlignment.Center
    };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly Dictionary<long, ToastVisual> visuals = new();
    private readonly Dictionary<long, Point> previousPositions = new();
    private NotificationService? service;
    private bool disposed;
    private bool showingOverflow;

    public ToastHost()
    {
        VerticalAlignment = VerticalAlignment.Stretch;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Margin = new Thickness(0, 0, 0, 16);
        Children.Add(stack);
        timer.Tick += TimerTick;
        SizeChanged += (_, _) => UpdateWidths();
    }

    public void Attach(NotificationService notifications)
    {
        if (service is not null) service.Changed -= ServiceChanged;
        service = notifications;
        disposed = false;
        service.Changed += ServiceChanged;
        Render();
        timer.Start();
    }

    public void Detach()
    {
        disposed = true;
        timer.Stop();
        if (service is not null) service.Changed -= ServiceChanged;
        service = null;
        foreach (var item in visuals.Values) item.Card.BeginAnimation(OpacityProperty, null);
        visuals.Clear();
        previousPositions.Clear();
        stack.Children.Clear();
    }

    private void ServiceChanged()
    {
        if (disposed) return;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(ServiceChanged));
            return;
        }
        Render();
    }

    private void TimerTick(object? sender, EventArgs e)
    {
        if (disposed || service is null) return;
        foreach (var visual in visuals.Values.ToArray())
        {
            if (visual.Item.Severity == NotificationSeverity.Error || visual.Exiting || visual.Hovered) continue;
            // Elapsed is monotonic and not derived from the frequency of UI timer ticks.
            if (visual.Elapsed.Elapsed >= TimeSpan.FromMilliseconds(4650))
                Close(visual.Item.Id, 350);
        }
    }

    private void Close(long id, int milliseconds = 180)
    {
        if (disposed || service is null || !visuals.TryGetValue(id, out var visual) || visual.Exiting) return;
        visual.Exiting = true;
        visual.Elapsed.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(milliseconds))
        {
            FillBehavior = FillBehavior.HoldEnd
        };
        fade.Completed += (_, _) =>
        {
            if (!disposed) service?.Dismiss(id);
        };
        visual.Card.BeginAnimation(OpacityProperty, fade);
    }

    private void Render()
    {
        if (disposed || service is null) return;
        previousPositions.Clear();
        foreach (var pair in visuals)
        {
            if (pair.Value.Card.IsLoaded)
                previousPositions[pair.Key] = pair.Value.Card.TranslatePoint(new Point(), this);
        }

        var visible = service.Visible;
        var ids = visible.Select(n => n.Id).ToHashSet();
        foreach (long id in visuals.Keys.Where(k => !ids.Contains(k)).ToArray())
            visuals.Remove(id);

        stack.Children.Clear();
        foreach (var item in visible)
        {
            if (!visuals.TryGetValue(item.Id, out var visual))
            {
                visual = CreateVisual(item);
                visuals[item.Id] = visual;
            }
            visual.Card.Margin = new Thickness(0, 0, 0, stack.Children.Count == 0 ? 0 : 8);
            stack.Children.Add(visual.Card);
        }
        int hiddenErrors = service.OverflowErrors.Count;
        if (hiddenErrors > 0)
        {
            var button = new Button
            {
                Content = $"还有 {hiddenErrors} 条错误",
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            button.Click += (_, _) => ShowOverflow();
            stack.Children.Insert(0, button);
        }
        UpdateWidths();
        Dispatcher.BeginInvoke(new Action(AnimateReflow), DispatcherPriority.Loaded);
    }

    private ToastVisual CreateVisual(NotificationItem item)
    {
        var elapsed = Stopwatch.StartNew();
        var icon = new TextBlock
        {
            Text = item.Severity switch
            {
                NotificationSeverity.Error => "⛔",
                NotificationSeverity.Warning => "⚠",
                _ => "ⓘ"
            },
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var content = new TextBlock { Text = item.Message, TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(38, 45, 57)), VerticalAlignment = VerticalAlignment.Center };
        var close = new Button
        {
            Content = "×", Width = 28, Height = 28, FontSize = 16,
            Padding = new Thickness(0), Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "关闭消息"
        };
        System.Windows.Automation.AutomationProperties.SetName(close, "关闭消息");
        close.Click += (_, _) => Close(item.Id);
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.Children.Add(icon);
        Grid.SetColumn(content, 1);
        layout.Children.Add(content);
        Grid.SetColumn(close, 2);
        layout.Children.Add(close);
        var borderColor = item.Severity switch
        {
            NotificationSeverity.Error => Color.FromRgb(216, 122, 122),
            NotificationSeverity.Warning => Color.FromRgb(209, 168, 81),
            _ => Color.FromRgb(207, 215, 224)
        };
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(252, 252, 253)),
            BorderBrush = new SolidColorBrush(borderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Child = layout,
            Opacity = 0,
            RenderTransform = new TranslateTransform()
        };
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black, BlurRadius = 12, ShadowDepth = 2, Opacity = 0.15
        };
        var visual = new ToastVisual(item, card, elapsed);
        card.MouseEnter += (_, _) => { visual.Hovered = true; visual.Elapsed.Stop(); };
        card.MouseLeave += (_, _) =>
        {
            visual.Hovered = false;
            if (!visual.Exiting) visual.Elapsed.Start();
        };
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
        return visual;
    }

    private void UpdateWidths()
    {
        var width = Math.Min(360, Math.Max(0, ActualWidth - 16));
        foreach (var visual in visuals.Values)
            visual.Card.Width = Math.Min(300, width);
    }

    private void AnimateReflow()
    {
        if (disposed) return;
        foreach (var pair in visuals)
        {
            if (!previousPositions.TryGetValue(pair.Key, out Point old) || !pair.Value.Card.IsLoaded) continue;
            var card = pair.Value.Card;
            double delta = old.Y - card.TranslatePoint(new Point(), this).Y;
            if (Math.Abs(delta) < 1) continue;
            var animation = new DoubleAnimation(delta, 0, TimeSpan.FromMilliseconds(200));
            ((TranslateTransform)card.RenderTransform).BeginAnimation(TranslateTransform.YProperty, animation);
        }
    }

    private void ShowOverflow()
    {
        if (disposed || service is null || showingOverflow) return;
        showingOverflow = true;
        var window = Window.GetWindow(this);
        var dialog = new Window
        {
            Title = "未处理的错误",
            Owner = window,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 440, Height = 360,
            MinWidth = 320, MinHeight = 220,
            Background = Brushes.White
        };
        var list = new StackPanel { Margin = new Thickness(12) };
        foreach (var error in service.OverflowErrors.ToArray())
        {
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var close = new Button { Content = "关闭", MinWidth = 54, Height = 28 };
            System.Windows.Automation.AutomationProperties.SetName(close, "关闭错误消息");
            close.Click += (_, _) =>
            {
                service.Dismiss(error.Id);
                list.Children.Remove(line);
            };
            DockPanel.SetDock(close, Dock.Right);
            line.Children.Add(close);
            line.Children.Add(new TextBlock { Text = error.Message, TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            list.Children.Add(line);
        }
        dialog.Content = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        dialog.Closed += (_, _) => showingOverflow = false;
        dialog.Show();
    }

    private sealed class ToastVisual(NotificationItem item, Border card, Stopwatch elapsed)
    {
        public NotificationItem Item { get; } = item;
        public Border Card { get; } = card;
        public Stopwatch Elapsed { get; } = elapsed;
        public bool Hovered { get; set; }
        public bool Exiting { get; set; }
    }
}
