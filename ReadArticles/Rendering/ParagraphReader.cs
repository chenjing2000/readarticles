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
    private sealed record VisualRun(string Sid, int Offset, Run Run, bool Vocabulary);
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

    public ParagraphReader(IEnumerable<Segment> segments, IEnumerable<string> words, bool highlightsVisible = true)
    {
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
                int cursor = 0, renderedOffset = 0;
                foreach (Match match in Regex.Matches(segment.Text, @"\[\[(\d+)\]\]"))
                {
                    AddText(segment.Text[cursor..match.Index]);
                    AddRun($"____{match.Groups[1].Value}____", false, true);
                    cursor = match.Index + match.Length;
                }
                AddText(segment.Text[cursor..]);
                // Match Python presentation spacing, with an explicit owning SID.
                if (index < source.Length - 1) AddRun(" ", false);
                paragraph.Inlines.Add(span);
                ranges.Add(new SegmentTextRange(segment, span.ContentStart, span.ContentEnd));

                void AddText(string text)
                {
                    int offset = 0;
                    foreach (var hit in matcher.Match(text, words))
                    {
                        AddRun(text[offset..hit.Start], false);
                        AddRun(text.Substring(hit.Start, hit.Length), true);
                        offset = hit.Start + hit.Length;
                    }
                    AddRun(text[offset..], false);
                }
                void AddRun(string text, bool vocabulary, bool blank = false)
                {
                    if (text.Length == 0) return;
                    var run = new Run(text);
                    if (blank) { run.Foreground = Brush("BlankBrush"); run.FontWeight = FontWeights.SemiBold; }
                    span.Inlines.Add(run);
                    visualRuns.Add(new VisualRun(segment.Sid, renderedOffset, run, vocabulary));
                    renderedOffset += text.Length;
                }
            }
            Ranges = ranges;
            Selection.Select(PositionInParagraph(selectionStart), PositionInParagraph(selectionEnd));
            ApplyStyles();
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
            else if (highlightsVisible && visual.Vocabulary && visual.Run.Text.Equals(focusedWord, StringComparison.OrdinalIgnoreCase))
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
        var visual = visualRuns.FirstOrDefault(v => v.Vocabulary && v.Run.Text.Equals(word, StringComparison.OrdinalIgnoreCase));
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

