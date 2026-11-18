using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows;
using System.Text.RegularExpressions;
using ReadArticles.Models;
using ReadArticles.Services;
using System.Windows.Threading;
namespace ReadArticles.Rendering;

public sealed record SegmentTextRange(Segment Segment, TextPointer Start, TextPointer End);
public sealed class ParagraphReader : RichTextBox
{
    private readonly SelectionAddAdorner selectionAdd;
    private readonly ScriptVocabularyAdorner? scriptVocabularyAdorner;
    private AdornerLayer? scriptVocabularyLayer;
    private readonly VocabularyMatcher matcher = new VocabularyMatcher();
    private readonly Segment[] source;
    private readonly List<VisualRun> visualRuns = new List<VisualRun>();
    private sealed class VisualRun(string sid, int sentenceIndex, int offset, Run run, string? vocabularyKey,
        ScriptTimedUnit? timedUnit = null)
    {
        public string Sid { get; } = sid;
        public int SentenceIndex { get; } = sentenceIndex;
        public int Offset { get; } = offset;
        public Run Run { get; } = run;
        public string? VocabularyKey { get; } = vocabularyKey;
        public bool Vocabulary => VocabularyKey is not null;
        public ScriptTimedUnit? TimedUnit { get; } = timedUnit;
        public double LastRead { get; set; } = double.NaN;
        public IReadOnlyList<ScriptVisualLine>? LayoutLines { get; set; }
    }
    private sealed record ScriptVisualLine(int Start, int Count, Rect Bounds);
    private sealed record ScriptVocabularyRange(int SentenceIndex, int Start, int Length, string Key);
    private readonly List<ScriptVocabularyRange> scriptVocabularyRanges = new();
    private readonly IReadOnlyList<ScriptSentence>? scriptSentences;
    private double? scriptSeconds;
    private static readonly SolidColorBrush ScriptReadBrush = FrozenScriptReadBrush();

    private static SolidColorBrush FrozenScriptReadBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0xB8, 0x1A, 0x35));
        brush.Freeze();
        return brush;
    }
    private string[] words;
    private bool highlightsVisible;
    private string? playingSid;
    private string? focusedWord;
    private bool formatting;
    public IReadOnlyList<SegmentTextRange> Ranges { get; private set; } = Array.Empty<SegmentTextRange>();
    public string? HoveredSid { get; private set; }
    public bool IsSelectionAddVisible => selectionAdd.IsShown;
    public event Action<PassageSelection>? SelectionRequested;
    public event Action<string>? SegmentPlaybackRequested;

    // Script sentences are display-only: their segments have no real SID or sentence audio.
    public ParagraphReader(IEnumerable<Segment> segments, IEnumerable<string> words,
        bool highlightsVisible = true, IReadOnlyList<ScriptSentence>? scriptSentences = null)
    {
        this.scriptSentences = scriptSentences;
        this.words = words.ToArray();
        this.highlightsVisible = highlightsVisible;
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        Padding = new Thickness(0);
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        SelectionBrush = Brush("SelectionBrush");
        SelectionOpacity = 0.8;
        ContextMenu = null;
        FontFamily = new FontFamily("Georgia");
        FontSize = 15;

        source = segments.ToArray();

        selectionAdd = new SelectionAddAdorner(this);
        if (scriptSentences is not null)
        {
            scriptVocabularyAdorner = new ScriptVocabularyAdorner(this);
            Loaded += AttachScriptVocabulary;
            Unloaded += DetachScriptVocabulary;
        }
        SelectionChanged += ReaderSelectionChanged;
        PreviewMouseRightButtonDown += (_, e) =>
        {
            var pointer = GetPositionFromPoint(e.GetPosition(this), false);
            if (pointer is not null) RequestSegmentPlayback(pointer);
            e.Handled = true;
        };
        MouseMove += (_, e) =>
        {
            var pointer = GetPositionFromPoint(e.GetPosition(this), false);
            SetHoveredSegment(pointer is null ? null : FindSegment(pointer)?.Segment.Sid);
        };
        MouseLeave += (_, _) => SetHoveredSegment(null);
        PreviewMouseLeftButtonUp += ReaderMouseLeftButtonUp;
        if (scriptSentences is not null)
        {
            Loaded += (_, _) => RefreshScriptLayout();
            SizeChanged += (_, _) => RefreshScriptLayout();
        }
        BuildDocument();
    }

    public SegmentTextRange? FindSegment(TextPointer pointer) =>
        Ranges.FirstOrDefault(r => pointer.CompareTo(r.Start) >= 0 && pointer.CompareTo(r.End) < 0);

    public PassageSelection? GetSelection()
    {
        if (Selection.IsEmpty) return null;
        // End is exclusive; equality with this segment's end is valid.
        var range = Ranges.FirstOrDefault(r => Selection.Start.CompareTo(r.Start) >= 0 &&
            Selection.Start.CompareTo(r.End) < 0 && Selection.End.CompareTo(r.End) <= 0);
        if (range is null) return null;
        var raw = new TextRange(Selection.Start, Selection.End).Text;
        string normalized = Regex.Replace(raw, @"\s+", " ").Trim();
        if (normalized.Length == 0) return null;
        int offset = new TextRange(range.Start, Selection.Start).Text.Length;
        return new PassageSelection(range.Segment.Sid, normalized, offset, raw.Length);
    }

    public void SetHoveredSegment(string? sid)
    {
        string? next = Ranges.FirstOrDefault(r => r.Segment.Sid == sid && r.Segment.HasAudio)?.Segment.Sid;
        if (next == HoveredSid) return;
        HoveredSid = next;
        ApplyStyles();
    }

    public void SetPlayingSegment(string? sid)
    {
        playingSid = sid;
        ApplyStyles();
    }

    public void SetVocabulary(IEnumerable<string> entries, bool visible)
    {
        var next = entries.ToArray();
        var currentWords = new HashSet<string>(words, StringComparer.OrdinalIgnoreCase);
        bool changed = !currentWords.SetEquals(next);
        words = next;
        highlightsVisible = visible;
        if (changed)
            BuildDocument();
        else
            ApplyStyles();
    }

    private void BuildDocument()
    {
        int selectionStart = new TextRange(Document.ContentStart, Selection.Start).Text.Length;
        int selectionEnd = new TextRange(Document.ContentStart, Selection.End).Text.Length;
        formatting = true;
        try
        {
            visualRuns.Clear();
            scriptVocabularyRanges.Clear();
            var paragraph = new Paragraph { Margin = new Thickness(0), LineHeight = Math.Round(FontSize * 1.7), TextIndent = FontSize * 2 };
            Document = new FlowDocument(paragraph) { PagePadding = new Thickness(0) };
            var ranges = new List<SegmentTextRange>();
            for (int index = 0; index < source.Length; index++)
            {
                var segment = source[index];
                var span = new Span();
                int renderedOffset = 0;
                if (scriptSentences is not null)
                {
                    AddScriptText(segment.Text, scriptSentences[index].TimedUnits);
                }
                else
                {
                    int cursor = 0;
                    foreach (Match match in Regex.Matches(segment.Text, @"\[\[(\d+)\]\]"))
                    {
                        AddText(segment.Text[cursor..match.Index]);
                        AddRun($"____{match.Groups[1].Value}____", null, true);
                        cursor = match.Index + match.Length;
                    }
                    AddText(segment.Text[cursor..]);
                }
                // Script keeps the exact sentence text, including existing whitespace.
                if (index < source.Length - 1 &&
                    (scriptSentences is null ||
                     (!char.IsWhiteSpace(segment.Text[^1]) && !char.IsWhiteSpace(source[index + 1].Text[0]))))
                    AddRun(" ");
                paragraph.Inlines.Add(span);
                ranges.Add(new SegmentTextRange(segment, span.ContentStart, span.ContentEnd));

                void AddText(string text)
                {
                    int offset = 0;
                    foreach (var hit in matcher.Match(text, words))
                    {
                        AddRun(text[offset..hit.Start]);
                        AddRun(text.Substring(hit.Start, hit.Length), hit.Key);
                        offset = hit.Start + hit.Length;
                    }
                    AddRun(text[offset..]);
                }

                void AddScriptText(string text, IReadOnlyList<ScriptTimedUnit> units)
                {
                    // Vocabulary matches remain independent background ranges:
                    // they never split a timed word into separate gradient Runs.
                    foreach (var hit in matcher.Match(text, words))
                        scriptVocabularyRanges.Add(new ScriptVocabularyRange(index, hit.Start, hit.Length, hit.Key));

                    int cursor = 0;
                    foreach (var unit in units)
                    {
                        // The producer owns text alignment. Invalid/overlapping
                        // spans remain readable without introducing new timing.
                        if (unit.Offset < cursor || unit.Offset + unit.Length > text.Length) continue;
                        AddRun(text[cursor..unit.Offset]);
                        AddRun(text.Substring(unit.Offset, unit.Length), timedUnit: unit);
                        cursor = unit.Offset + unit.Length;
                    }
                    AddRun(text[cursor..]);
                }

                void AddRun(string text, string? vocabularyKey = null, bool blank = false,
                    ScriptTimedUnit? timedUnit = null)
                {
                    if (text.Length == 0) return;
                    var run = new Run(text);
                    if (blank) { run.Foreground = Brush("BlankBrush"); run.FontWeight = FontWeights.SemiBold; }
                    span.Inlines.Add(run);
                    visualRuns.Add(new VisualRun(segment.Sid, index, renderedOffset, run,
                        vocabularyKey, timedUnit));
                    renderedOffset += text.Length;
                }
            }
            Ranges = ranges;
            Selection.Select(PositionInParagraph(selectionStart), PositionInParagraph(selectionEnd));
            ApplyStyles();
            SetScriptPosition(scriptSeconds);
            if (scriptSentences is not null && IsLoaded) RefreshScriptLayout();
        }
        finally { formatting = false; }
    }

    private TextPointer PositionInParagraph(int offset)
    {
        foreach (var range in Ranges)
        {
            int length = new TextRange(range.Start, range.End).Text.Length;
            if (offset < length) return PositionAt(range, offset);
            offset -= length;
        }
        return Ranges.Count > 0 ? Ranges[^1].End : Document.ContentStart;
    }

    private void RefreshScriptLayout()
    {
        foreach (var visual in visualRuns)
        {
            visual.LastRead = double.NaN;
            visual.LayoutLines = null;
        }
        // Refresh absolute endpoints after WPF reflows the text.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (scriptSentences is null) return;
            SetScriptPosition(scriptSeconds);
            scriptVocabularyAdorner?.Redraw();
        }));
    }

    // One timed unit is one complete Run, irrespective of internal words
    // or punctuation. Its gradient makes a single sweep across that Run.
    public void SetScriptPosition(double? seconds)
    {
        scriptSeconds = seconds;
        if (scriptSentences is null) return;
        foreach (var visual in visualRuns)
        {
            var unit = visual.TimedUnit;
            if (unit?.CanColor != true) continue;
            double fraction = seconds.HasValue ? unit.Progress(seconds.Value) : 0;
            if (Math.Abs(fraction - visual.LastRead) < 0.0001) continue;
            visual.LastRead = fraction;
            if (fraction <= 0)
            {
                visual.Run.ClearValue(TextElement.TextEffectsProperty);
                visual.Run.ClearValue(TextElement.ForegroundProperty);
            }
            else if (fraction >= 1)
            {
                visual.Run.ClearValue(TextElement.TextEffectsProperty);
                visual.Run.Foreground = ScriptReadBrush;
            }
            else
            {
                Color baseColor = Foreground is SolidColorBrush solid ? solid.Color : Colors.Black;
                var lines = visual.LayoutLines ??= GetTimedUnitLines(visual);
                if (lines.Count > 1)
                {
                    // WPF renders a wrapped Run as multiple glyph runs. A
                    // DrawingBrush on their shared Foreground repeats its mapping
                    // and colors later lines too early. Apply a brush per visual
                    // line using TextEffects; the Run and clock remain indivisible.
                    double remaining = fraction * lines.Sum(line => line.Bounds.Width);
                    var effects = new TextEffectCollection();
                    foreach (var line in lines)
                    {
                        double width = Math.Clamp(remaining, 0, line.Bounds.Width);
                        Brush color;
                        if (width <= 0)
                            color = new SolidColorBrush(baseColor);
                        else if (width >= line.Bounds.Width)
                            color = ScriptReadBrush;
                        else
                        {
                            double local = width / line.Bounds.Width;
                            var lineGradient = new LinearGradientBrush
                            {
                                MappingMode = BrushMappingMode.Absolute,
                                StartPoint = new Point(line.Bounds.Left, 0),
                                EndPoint = new Point(line.Bounds.Right, 0)
                            };
                            lineGradient.GradientStops.Add(new GradientStop(ScriptReadBrush.Color, 0));
                            lineGradient.GradientStops.Add(new GradientStop(ScriptReadBrush.Color, local));
                            lineGradient.GradientStops.Add(new GradientStop(baseColor, local));
                            lineGradient.GradientStops.Add(new GradientStop(baseColor, 1));
                            lineGradient.Freeze();
                            color = lineGradient;
                        }
                        effects.Add(new TextEffect
                        {
                            PositionStart = line.Start,
                            PositionCount = line.Count,
                            Foreground = color
                        });
                        remaining -= line.Bounds.Width;
                    }
                    visual.Run.ClearValue(TextElement.ForegroundProperty);
                    visual.Run.TextEffects = effects;
                    continue;
                }
                visual.Run.ClearValue(TextElement.TextEffectsProperty);
                var gradient = new LinearGradientBrush();
                Rect first = visual.Run.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                Rect last = visual.Run.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
                if (IsSingleLine(first, last) && last.Left > first.Left)
                {
                    gradient.MappingMode = BrushMappingMode.Absolute;
                    gradient.StartPoint = new Point(first.Left, 0);
                    gradient.EndPoint = new Point(last.Left, 0);
                }
                else
                {
                    // Before layout, retain a single-Run relative gradient.
                    gradient.StartPoint = new Point(0, 0.5);
                    gradient.EndPoint = new Point(1, 0.5);
                }
                gradient.GradientStops.Add(new GradientStop(ScriptReadBrush.Color, 0));
                gradient.GradientStops.Add(new GradientStop(ScriptReadBrush.Color, fraction));
                gradient.GradientStops.Add(new GradientStop(baseColor, fraction));
                gradient.GradientStops.Add(new GradientStop(baseColor, 1));
                gradient.Freeze();
                visual.Run.Foreground = gradient;
            }
        }
    }

    // Locate visual lines and their text offsets without splitting the
    // underlying Run. Effects use these offsets solely for painting.
    private IReadOnlyList<ScriptVisualLine> GetTimedUnitLines(VisualRun visual)
    {
        var lines = new List<ScriptVisualLine>();
        var range = Ranges[visual.SentenceIndex];
        int startOffset = visual.Offset;
        for (int i = 0; i < visual.Run.Text.Length; i++)
        {
            var start = PositionAt(range, startOffset + i)
                .GetCharacterRect(LogicalDirection.Forward);
            var end = PositionAt(range, startOffset + i + 1)
                .GetCharacterRect(LogicalDirection.Backward);
            if (!IsSingleLine(start, end) || end.Left <= start.Left) continue;
            var cell = new Rect(start.Left, start.Top, end.Left - start.Left, start.Height);
            if (lines.Count > 0 && Math.Abs(lines[^1].Bounds.Top - cell.Top) < 1)
            {
                var previous = lines[^1];
                lines[^1] = previous with
                {
                    Count = i + 1 - previous.Start,
                    Bounds = Rect.Union(previous.Bounds, cell)
                };
            }
            else
                lines.Add(new ScriptVisualLine(i, 1, cell));
        }
        return lines;
    }

    private static bool IsSingleLine(Rect first, Rect last) =>
        !first.IsEmpty && !last.IsEmpty &&
        Math.Abs(first.Top - last.Top) < 1 && first.Height > 0 && last.Height > 0;

    public Brush GetTextForeground(int sentenceIndex, int textOffset)
    {
        if (scriptSentences is null) throw new InvalidOperationException("Not a Script reader.");
        var run = visualRuns.First(v => v.Offset <= textOffset &&
            textOffset < v.Offset + v.Run.Text.Length &&
            v.SentenceIndex == sentenceIndex);
        return run.Run.Foreground;
    }

    private void ApplyStyles()
    {
        if (scriptSentences is not null)
        {
            // Rebuild the retained overlay only after the reader is laid out.
            // This avoids requesting character positions from OnRender, when
            // WPF's text layout can be in the middle of a rendering pass.
            if (scriptVocabularyLayer is not null)
                scriptVocabularyAdorner?.Redraw();
            return;
        }
        // Assign local Run properties: TextRange formatting can merge adjacent spans
        // and relocate retained boundary pointers, so it must not own visual states.
        foreach (var visual in visualRuns)
        {
            if (visual.Sid == playingSid)
                visual.Run.Background = Brush("PlayingSegmentBrush");
            else if (visual.Sid == HoveredSid)
                visual.Run.Background = Brush("SegmentHoverBrush");
            else if (highlightsVisible && visual.Vocabulary && string.Equals(visual.VocabularyKey, focusedWord, StringComparison.OrdinalIgnoreCase))
                visual.Run.Background = Brush("SelectedVocabularyBrush");
            else if (highlightsVisible && visual.Vocabulary)
                visual.Run.Background = Brush("VocabularyHighlightBrush");
            else
                visual.Run.Background = Brushes.Transparent;
        }
    }

    public void ClearVocabularyFocus()
    {
        focusedWord = null;
        ApplyStyles();
    }
    public bool FocusVocabulary(string word)
    {
        if (scriptSentences is not null)
        {
            var match = scriptVocabularyRanges.FirstOrDefault(m =>
                string.Equals(m.Key, word, StringComparison.OrdinalIgnoreCase));
            if (match is null) return false;
            focusedWord = word;
            ApplyStyles();
            Rect rect = PositionAt(Ranges[match.SentenceIndex], match.Start)
                .GetCharacterRect(LogicalDirection.Forward);
            if (!rect.IsEmpty) BringIntoView(rect);
            return true;
        }
        var visual = visualRuns.FirstOrDefault(v => v.Vocabulary &&
            string.Equals(v.VocabularyKey, word, StringComparison.OrdinalIgnoreCase));
        if (visual is null) return false;
        focusedWord = word;
        ApplyStyles();
        Rect position = visual.Run.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        if (!position.IsEmpty) BringIntoView(position);
        return true;
    }

    public Brush GetScriptVocabularyBackground(int sentenceIndex, int textOffset)
    {
        if (scriptSentences is null) throw new InvalidOperationException("Not a Script reader.");
        var match = scriptVocabularyRanges.FirstOrDefault(m => m.SentenceIndex == sentenceIndex &&
            textOffset >= m.Start && textOffset < m.Start + m.Length);
        if (!highlightsVisible || match is null) return Brushes.Transparent;
        return Brush(string.Equals(focusedWord, match.Key, StringComparison.OrdinalIgnoreCase)
            ? "SelectedVocabularyBrush" : "VocabularyHighlightBrush");
    }

    private void AttachScriptVocabulary(object sender, RoutedEventArgs e)
    {
        if (scriptVocabularyAdorner is null || scriptVocabularyLayer is not null) return;
        var layer = AdornerLayer.GetAdornerLayer(this);
        if (layer is null) return;
        scriptVocabularyLayer = layer;
        layer.Add(scriptVocabularyAdorner);
        scriptVocabularyAdorner.Redraw();
    }

    private void DetachScriptVocabulary(object sender, RoutedEventArgs e)
    {
        if (scriptVocabularyLayer is null || scriptVocabularyAdorner is null) return;
        scriptVocabularyLayer.Remove(scriptVocabularyAdorner);
        scriptVocabularyLayer = null;
    }

    private sealed class ScriptVocabularyAdorner : Adorner
    {
        private readonly ParagraphReader reader;
        private readonly DrawingVisual drawing = new();

        public ScriptVocabularyAdorner(ParagraphReader reader) : base(reader)
        {
            this.reader = reader;
            IsHitTestVisible = false;
            AddVisualChild(drawing);
        }

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index) =>
            index == 0 ? drawing : throw new ArgumentOutOfRangeException(nameof(index));

        // Calculate text geometry outside the WPF OnRender callback. The
        // retained visual remains part of this adorner and can be redrawn
        // immediately when vocabulary highlighting is toggled.
        public void Redraw()
        {
            using var context = drawing.RenderOpen();
            reader.DrawScriptVocabulary(context);
        }
    }

    private void DrawScriptVocabulary(DrawingContext drawingContext)
    {
        if (scriptSentences is null || !highlightsVisible) return;
        // The adorner renders after text. Translucency preserves glyph legibility
        // while giving both full and partial timed-unit vocabulary a visible tint.
        drawingContext.PushOpacity(0.45);
        foreach (var match in scriptVocabularyRanges)
        {
            if (match.SentenceIndex >= Ranges.Count) continue;
            var range = Ranges[match.SentenceIndex];
            Brush background = GetScriptVocabularyBackground(match.SentenceIndex, match.Start);
            int endOffset = match.Start + match.Length;
            Rect first = PositionAt(range, match.Start).GetCharacterRect(LogicalDirection.Forward);
            Rect last = PositionAt(range, endOffset).GetCharacterRect(LogicalDirection.Backward);
            if (IsSingleLine(first, last) && last.Left > first.Left)
            {
                drawingContext.DrawRectangle(background, null,
                    new Rect(first.Left, first.Top, last.Left - first.Left, first.Height));
                continue;
            }
            // Wrap-aware vocabulary backgrounds do not touch the timed Runs.
            for (int offset = match.Start; offset < endOffset; offset++)
            {
                Rect start = PositionAt(range, offset).GetCharacterRect(LogicalDirection.Forward);
                Rect finish = PositionAt(range, offset + 1).GetCharacterRect(LogicalDirection.Backward);
                if (!IsSingleLine(start, finish) || finish.Left <= start.Left) continue;
                drawingContext.DrawRectangle(background, null,
                    new Rect(start.Left, start.Top, finish.Left - start.Left, start.Height));
            }
        }
        drawingContext.Pop();
    }

    // Count text characters across multiple Runs, skipping WPF element symbols.
    // This locates offsets within an already-known Segment; it never searches for SID.
    public TextPointer PositionAt(SegmentTextRange range, int offset)
    {
        var pointer = range.Start;
        while (pointer.CompareTo(range.End) < 0)
        {
            if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                int count = pointer.GetTextRunLength(LogicalDirection.Forward);
                if (offset <= count) return pointer.GetPositionAtOffset(offset)!;
                offset -= count;
            }
            pointer = pointer.GetNextContextPosition(LogicalDirection.Forward)!;
        }
        if (offset != 0) throw new ArgumentOutOfRangeException(nameof(offset));
        return range.End;
    }

    public Brush GetTextBackground(string sid, int renderedOffset)
    {
        return visualRuns.First(v => v.Sid == sid && renderedOffset >= v.Offset &&
            renderedOffset < v.Offset + v.Run.Text.Length).Run.Background;
    }

    public bool RequestSegmentPlayback(TextPointer pointer)
    {
        var range = FindSegment(pointer);
        if (range?.Segment.HasAudio != true) return false;
        Selection.Select(pointer, pointer);
        HideSelectionAdd();
        SegmentPlaybackRequested?.Invoke(range.Segment.Sid);
        return true;
    }

    public bool RequestSelectionAdd()
    {
        PassageSelection? selectedWord = GetSelection();
        selectionAdd.Hide();
        if (selectedWord == null) return false;

        TextPointer start = Selection.Start;
        Selection.Select(start, start);
        SelectionRequested?.Invoke(selectedWord);
        return true;
    }

    private void ReaderSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (Selection.IsEmpty || string.IsNullOrWhiteSpace(Selection.Text))
            selectionAdd.Hide();
        else if (!formatting)
            selectionAdd.Show();
    }

    private void ReaderMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(ShowSelectionAdd));
    }

    private void ShowSelectionAdd()
    {
        selectionAdd.Show();
    }

    private void HideSelectionAdd()
    {
        selectionAdd.Hide();
    }

    private Brush Brush(string key) => (Brush)FindResource(key);

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == FontSizeProperty && Document?.Blocks.FirstBlock is Paragraph paragraph)
        {
            paragraph.LineHeight = Math.Round((double)e.NewValue * 1.7);
            paragraph.TextIndent = (double)e.NewValue * 2;
            if (selectionAdd?.IsShown == true)
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ShowSelectionAdd));
            if (scriptSentences is not null && IsLoaded) RefreshScriptLayout();
        }
    }
}

