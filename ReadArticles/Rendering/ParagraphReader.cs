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
    private readonly VocabularyMatcher matcher = new VocabularyMatcher();
    private readonly Segment[] source;
    private readonly List<VisualRun> visualRuns = new List<VisualRun>();
    private sealed class VisualRun(string sid, int sentenceIndex, int offset, Run run, string? vocabularyKey,
        ScriptTimedUnit? timedUnit = null, int unitOffset = 0)
    {
        public string Sid { get; } = sid;
        public int SentenceIndex { get; } = sentenceIndex;
        public int Offset { get; } = offset;
        public Run Run { get; } = run;
        public string? VocabularyKey { get; } = vocabularyKey;
        public bool Vocabulary => VocabularyKey is not null;
        public ScriptTimedUnit? TimedUnit { get; } = timedUnit;
        public int UnitOffset { get; } = unitOffset;
        public double LastRead { get; set; } = double.NaN;
    }
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
                    // Split at the union of vocabulary and timing boundaries,
                    // but retain each complete vocabulary key for focus/background.
                    var matches = matcher.Match(text, words);
                    var located = units.Where(u => u.Offset >= 0 && u.Length > 0).ToArray();
                    var bounds = new SortedSet<int> { 0, text.Length };
                    foreach (var match in matches)
                    {
                        bounds.Add(match.Start);
                        bounds.Add(match.Start + match.Length);
                    }
                    foreach (var unit in located)
                    {
                        bounds.Add(unit.Offset);
                        bounds.Add(unit.Offset + unit.Length);
                    }
                    var positions = bounds.ToArray();
                    for (int part = 0; part < positions.Length - 1; part++)
                    {
                        int start = positions[part], end = positions[part + 1];
                        if (start >= end) continue;
                        var match = matches.FirstOrDefault(m => start >= m.Start && start < m.Start + m.Length);
                        // When malformed source intervals overlap, prefer the latest start.
                        var unit = located.Where(u => start >= u.Offset && start < u.Offset + u.Length)
                            .OrderByDescending(u => u.Start ?? -1).FirstOrDefault();
                        AddRun(text[start..end], match?.Key, false, unit,
                            unit is null ? 0 : start - unit.Offset);
                    }
                }

                void AddRun(string text, string? vocabularyKey = null, bool blank = false,
                    ScriptTimedUnit? timedUnit = null, int unitOffset = 0)
                {
                    if (text.Length == 0) return;
                    var run = new Run(text);
                    if (blank) { run.Foreground = Brush("BlankBrush"); run.FontWeight = FontWeights.SemiBold; }
                    span.Inlines.Add(run);
                    visualRuns.Add(new VisualRun(segment.Sid, index, renderedOffset, run,
                        vocabularyKey, timedUnit, unitOffset));
                    renderedOffset += text.Length;
                }
            }
            Ranges = ranges;
            Selection.Select(PositionInParagraph(selectionStart), PositionInParagraph(selectionEnd));
            ApplyStyles();
            SetScriptPosition(scriptSeconds);
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

    // Recompute from the actual article clock, never from a history of played words.
    // Unchanged runs retain their brushes; only the active timed unit usually changes.
    public void SetScriptPosition(double? seconds)
    {
        scriptSeconds = seconds;
        if (scriptSentences is null) return;
        foreach (var visual in visualRuns)
        {
            var unit = visual.TimedUnit;
            if (unit?.CanColor != true) continue;
            double totalRead = (seconds.HasValue ? unit.Progress(seconds.Value) : 0) * unit.Length;
            double fraction = Math.Clamp((totalRead - visual.UnitOffset) / visual.Run.Text.Length, 0, 1);
            if (Math.Abs(fraction - visual.LastRead) < 0.0001) continue;
            visual.LastRead = fraction;
            if (fraction <= 0) visual.Run.ClearValue(TextElement.ForegroundProperty);
            else if (fraction >= 1) visual.Run.Foreground = ScriptReadBrush;
            else
            {
                Color baseColor = Foreground is SolidColorBrush solid ? solid.Color : Colors.Black;
                var gradient = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0.5),
                    EndPoint = new Point(1, 0.5)
                };
                gradient.GradientStops.Add(new GradientStop(ScriptReadBrush.Color, 0));
                gradient.GradientStops.Add(new GradientStop(ScriptReadBrush.Color, fraction));
                gradient.GradientStops.Add(new GradientStop(baseColor, fraction));
                gradient.GradientStops.Add(new GradientStop(baseColor, 1));
                gradient.Freeze();
                visual.Run.Foreground = gradient;
            }
        }
    }

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
        var visual = visualRuns.FirstOrDefault(v => v.Vocabulary &&
            string.Equals(v.VocabularyKey, word, StringComparison.OrdinalIgnoreCase));
        if (visual is null) return false;
        focusedWord = word;
        ApplyStyles();
        Rect rect = visual.Run.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        if (!rect.IsEmpty) BringIntoView(rect);
        return true;
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
        }
    }
}

