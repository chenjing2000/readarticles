using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace ReadArticles.Infrastructure;

public static class UiIcons
{
    public static ImageSource Play { get; } = Svg("audio/play.svg");
    public static ImageSource Pause { get; } = Svg("audio/pause.svg");
    public static ImageSource Stop { get; } = Svg("audio/stop.svg");
    public static ImageSource Import { get; } = Svg("vocabulary/import.svg");
    public static ImageSource HighlightOn { get; } = Svg("vocabulary/highlight_on.svg");
    public static ImageSource HighlightOff { get; } = Svg("vocabulary/highlight_off.svg");
    public static ImageSource ScriptOff { get; } = Svg("reader/script_music.svg", tint: "#4B4B4B");
    public static ImageSource ScriptOn { get; } = Svg("reader/script_music.svg", tint: "#B81A35");
    public static ImageSource DisabledPlay { get; } = Svg("audio/play.svg", true);
    public static ImageSource DisabledStop { get; } = Svg("audio/stop.svg", true);
    public static ImageSource DisabledHighlightOn { get; } = Svg("vocabulary/highlight_on.svg", true);
    public static ImageSource DisabledHighlightOff { get; } = Svg("vocabulary/highlight_off.svg", true);
    public static ImageSource MoveUp { get; } = Svg("vocabulary/move_up.svg");
    public static ImageSource MoveDown { get; } = Svg("vocabulary/move_down.svg");
    public static ImageSource Delete { get; } = Svg("vocabulary/delete.svg");
    public static ImageSource AppIcon { get; } = Png("maple_leaf.png");
    public static ImageSource Plus { get; } = Svg("tree/plus.svg");
    public static ImageSource Minus { get; } = Svg("tree/minus.svg");

    private static Uri Resource(string path) => new Uri($"pack://application:,,,/ReadArticles;component/ui/{path}");
    private static ImageSource Png(string path)
    {
        var image = new BitmapImage(Resource(path));
        image.Freeze();
        return image;
    }
    // Render the circle, rounded rectangle and path primitives used by these assets.
    private static ImageSource Svg(string path, bool grayscale = false, string? tint = null)
    {
        using var stream = Application.GetResourceStream(Resource(path))!.Stream;
        var svg = XDocument.Load(stream).Root!;
        double[] bounds;
        var viewBox = svg.Attribute("viewBox");
        if (viewBox != null)
        {
            bounds = viewBox.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        }
        else
        {
            bounds = new[] { 0, 0, Number(svg, "width"), Number(svg, "height") };
        }
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(bounds[0], bounds[1], bounds[2], bounds[3]))));
        foreach (var element in svg.Elements()) drawing.Children.Add(ReadElement(element, grayscale, tint));
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }
    private static Drawing ReadElement(XElement element, bool grayscale, string? tint)
    {
        if (element.Name.LocalName == "g")
        {
            var group = new DrawingGroup();
            if (element.Attribute("transform") is { } transform)
            {
                string value = transform.Value.Trim();
                if (!value.StartsWith("translate(", StringComparison.Ordinal) || !value.EndsWith(')'))
                    throw new NotSupportedException($"Unsupported SVG transform: {value}");
                var coordinates = value.Substring(10, value.Length - 11).Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                group.Transform = new TranslateTransform(coordinates[0], coordinates.Length > 1 ? coordinates[1] : 0);
            }
            foreach (var child in element.Elements()) group.Children.Add(ReadElement(child, grayscale, tint));
            return group;
        }
        Geometry geometry;
        switch (element.Name.LocalName)
        {
            case "circle":
                double radius = Number(element, "r");
                geometry = new EllipseGeometry(new Point(Number(element, "cx"), Number(element, "cy")), radius, radius);
                break;
            case "rect":
                double radiusX = OptionalNumber(element, "rx", 0);
                double radiusY = OptionalNumber(element, "ry", radiusX);
                geometry = new RectangleGeometry(
                    new Rect(Number(element, "x"), Number(element, "y"), Number(element, "width"), Number(element, "height")),
                    radiusX, radiusY);
                break;
            case "path":
                geometry = Geometry.Parse(element.Attribute("d")!.Value);
                break;
            default:
                throw new NotSupportedException($"Unsupported SVG element: {element.Name.LocalName}");
        }

        var stroke = ReadBrush(element.Attribute("stroke")?.Value, grayscale, tint);
        Pen? pen = null;
        if (stroke != null)
        {
            pen = new Pen(stroke, OptionalNumber(element, "stroke-width", 1));
            if (element.Attribute("stroke-linecap")?.Value == "round")
                pen.StartLineCap = pen.EndLineCap = PenLineCap.Round;
            if (element.Attribute("stroke-linejoin")?.Value == "round")
                pen.LineJoin = PenLineJoin.Round;
        }
        return new GeometryDrawing(ReadBrush(element.Attribute("fill")?.Value ?? "black", grayscale, tint), pen, geometry);
    }

    private static double Number(XElement element, string attribute)
    {
        return double.Parse(element.Attribute(attribute)!.Value, CultureInfo.InvariantCulture);
    }

    private static double OptionalNumber(XElement element, string attribute, double defaultValue)
    {
        if (element.Attribute(attribute) == null) return defaultValue;
        return Number(element, attribute);
    }

    private static Brush? ReadBrush(string? color, bool grayscale, string? tint)
    {
        if (color == null || color == "none") return null;
        Color value = (Color)ColorConverter.ConvertFromString(tint ?? color);
        if (grayscale)
        {
            byte gray = (byte)((299 * value.R + 587 * value.G + 114 * value.B) / 1000);
            value = Color.FromArgb(value.A, gray, gray, gray);
        }
        return new SolidColorBrush(value);
    }
}

