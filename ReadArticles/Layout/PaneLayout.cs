namespace ReadArticles.Layout;

/// <summary>Session-only geometry for both native and browser dividers.
/// Available width excludes both splitters and is at least 600 DIP.
/// </summary>
public sealed class PaneLayout
{
    public double LeftWidth { get; private set; } = 250;
    public double RightWidth { get; private set; } = 300;
    private (double Left, double Right)? vocabularyDragStart;

    // Return whether the browser needs a new authoritative right width.
    private bool Set(double left, double right, double available)
    {
        left = Math.Clamp(Math.Round(left), 200, Math.Min(400, available - 400));
        right = Math.Clamp(Math.Round(right), 200, Math.Min(400, available - left - 200));
        bool rightChanged = Math.Abs(RightWidth - right) >= 0.5;
        LeftWidth = left;
        RightWidth = right;
        return rightChanged;
    }

    public bool ResizeLibrary(double requestedLeft, double available)
    {
        double left = Math.Clamp(Math.Round(requestedLeft), 200, Math.Min(400, available - 400));
        return Set(left, Math.Min(RightWidth, available - left - 200), available);
    }

    public bool ResizeVocabulary(double requestedRight, double available)
    {
        double right = Math.Clamp(Math.Round(requestedRight), 200, Math.Min(400, available - 400));
        return Set(Math.Min(LeftWidth, available - right - 200), right, available);
    }

    public bool Fit(double available)
    {
        double left = LeftWidth;
        double right = RightWidth;
        double overflow = Math.Max(0, left + right + 200 - available);
        double rightReduction = Math.Min(overflow, right - 200);
        right -= rightReduction;
        left -= overflow - rightReduction;
        return Set(left, right, available);
    }

    public bool DragVocabulary(double requestedRight, double available)
    {
        vocabularyDragStart ??= (LeftWidth, RightWidth);
        return ResizeVocabulary(requestedRight, available);
    }

    public void EndVocabularyDrag() => vocabularyDragStart = null;

    public bool CancelVocabularyDrag(double available)
    {
        if (vocabularyDragStart is not { } original) return false;
        vocabularyDragStart = null;
        return Set(original.Left, original.Right, available);
    }
}
