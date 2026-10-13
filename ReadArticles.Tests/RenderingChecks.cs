using System.Windows.Documents;
using System.Windows.Media;
using ReadArticles.Models;
using ReadArticles.Rendering;

internal static class RenderingChecks
{
    internal static void Run()
    {
        Program.Run("P01/P02/P04 repeated text retains distinct SID", () =>
        {
            var reader = TestData.Reader();
            Program.Check(reader.FindSegment(reader.Ranges[1].Start)!.Segment.Sid == "s002");
            Program.Check(new TextRange(reader.Ranges[0].Start, reader.Ranges[0].End).Text == "public life. ");
            Program.Check(reader.FindSegment(reader.Ranges[0].End.GetPositionAtOffset(-1)!)!.Segment.Sid == "s001");
        });
        Program.Run("P06 single-segment selection normalized and emitted", () =>
        {
            var reader = TestData.Reader();
            reader.Selection.Select(reader.Ranges[0].Start, reader.Ranges[0].End);
            Program.Check(reader.GetSelection() is { Sid: "s001", Text: "public life." });
            PassageSelection? received = null;
            reader.SelectionRequested += selection => received = selection;
            Program.Check(reader.RequestSelectionAdd() && received?.Sid == "s001");
        });
        Program.Run("P07 cross-segment selection rejected", () =>
        {
            var reader = TestData.Reader();
            reader.Selection.Select(reader.Ranges[0].Start, reader.Ranges[1].End);
            Program.Check(reader.GetSelection() is null && !reader.RequestSelectionAdd());
        });
        Program.Run("P08 empty and whitespace-only selection rejected", () =>
        {
            var reader = TestData.Reader();
            Program.Check(reader.GetSelection() is null);
            var end = reader.Ranges[0].End;
            reader.Selection.Select(end.GetPositionAtOffset(-1)!, end);
            Program.Check(reader.GetSelection() is null);
        });
        Program.Run("P14/P15 hover restores vocabulary and independent playing state", () =>
        {
            var reader = TestData.Reader();
            var pink = reader.FindResource("VocabularyHighlightBrush");
            var hover = reader.FindResource("SegmentHoverBrush");
            var playing = reader.FindResource("PlayingSegmentBrush");
            Program.Check(Equals(reader.GetTextBackground("s001", 0), pink));
            reader.SetHoveredSegment("s001");
            Program.Check(Equals(reader.GetTextBackground("s001", 0), hover));
            reader.SetPlayingSegment("s001");
            Program.Check(Equals(reader.GetTextBackground("s001", 0), playing));
            reader.SetHoveredSegment(null);
            Program.Check(Equals(reader.GetTextBackground("s001", 0), playing));
            reader.SetPlayingSegment(null);
            Program.Check(Equals(reader.GetTextBackground("s001", 0), pink));
        });
        Program.Run("highlight visibility does not change text or selection", () =>
        {
            var reader = TestData.Reader();
            reader.Selection.Select(reader.Ranges[0].Start, reader.Ranges[0].End);
            reader.SetVocabulary(["public life"], false);
            Program.Check(reader.GetSelection()?.Text == "public life.");
            Program.Check(Equals(reader.GetTextBackground("s001", 0), Brushes.Transparent));
        });
        Program.Run("P16/P17 Blank is display-only with no audio events", () =>
        {
            var reader = new ParagraphReader([new Segment("s005", "Character [[1]] matters.", false)], ["character"]);
            Program.Check(new TextRange(reader.Document.ContentStart, reader.Document.ContentEnd).Text.Contains("____1____"));
            Program.Check(!((Paragraph)reader.Document.Blocks.FirstBlock!).Inlines.OfType<Span>().SelectMany(s => s.Inlines.Cast<Inline>()).OfType<InlineUIContainer>().Any());
            Program.Check(!reader.RequestSegmentPlayback(reader.Ranges[0].Start));
            reader.SetHoveredSegment("s005");
            Program.Check(reader.HoveredSid is null);
        });
        Program.Run("P20 right click sends SID and clears selection", () =>
        {
            var reader = TestData.Reader();
            string? received = null;
            reader.SegmentPlaybackRequested += sid => received = sid;
            reader.Selection.Select(reader.Ranges[0].Start, reader.Ranges[0].End);
            Program.Check(reader.RequestSegmentPlayback(reader.Ranges[1].Start));
            Program.Check(received == "s002" && reader.Selection.IsEmpty);
        });
        Program.Run("reordered, recased and duplicate vocabulary preserves document and selection", () =>
        {
            var reader = TestData.Reader();
            var document = reader.Document;
            reader.Selection.Select(reader.Ranges[0].Start, reader.Ranges[0].End);
            string selected = reader.Selection.Text;
            reader.SetVocabulary(new[] { "PUBLIC LIFE", "public", "public" }, true);
            Program.Check(ReferenceEquals(document, reader.Document));
            Program.Check(reader.Selection.Text == selected && reader.GetSelection()?.Sid == "s001");
            Program.Check(reader.FindSegment(reader.Ranges[1].Start)?.Segment.Sid == "s002");
        });
    }
}
