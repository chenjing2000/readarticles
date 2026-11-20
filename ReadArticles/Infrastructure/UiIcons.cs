using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace ReadArticles.Infrastructure;

public static class UiIcons
{
    public static ImageSource AppIcon { get; } = Png("maple_leaf.png");
    public static ImageSource Plus { get; } = Svg("tree/plus.svg");
    public static ImageSource Minus { get; } = Svg("tree/minus.svg");
    public static ImageSource Settings { get; } = Svg("icons/setting.svg");
    public static ImageSource Refresh { get; } = Svg("icons/refresh.svg");

    private static Uri Resource(string path) => new Uri($"pack://application:,,,/ReadArticles;component/ui/{path}");
    private static ImageSource Png(string path)
    {
        var image = new BitmapImage(Resource(path));
        image.Freeze();
        return image;
    }
    // Render the circle, rounded rectangle and path primitives used by these assets.
    private static ImageSource Svg(string path)
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
        foreach (var element in svg.Elements()) drawing.Children.Add(ReadElement(element));
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }
    private static Drawing ReadElement(XElement element)
    {
        if (element.Name.LocalName == "g")
        {
            var group = new DrawingGroup();
            if (element.Attribute("transform") is { } transform)
            {
                string value = transform.Value.Trim();
                // Support the centered translate()+scale() transform in the
                // approved settings icon as well as the existing tree icons.
                var matches = System.Text.RegularExpressions.Regex.Matches(value,
                    @"(translate|scale)\(([^)]*)\)");
                var groupTransform = new TransformGroup();
                // In SVG, the rightmost transformation is applied first.
                foreach (System.Text.RegularExpressions.Match match in
                    matches.Cast<System.Text.RegularExpressions.Match>().Reverse())
                {
                    var args = match.Groups[2].Value.Split(new[] { ' ', ',' },
                        StringSplitOptions.RemoveEmptyEntries)
                        .Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                    groupTransform.Children.Add(match.Groups[1].Value == "scale"
                        ? new ScaleTransform(args[0], args.Length > 1 ? args[1] : args[0])
                        : new TranslateTransform(args[0], args.Length > 1 ? args[1] : 0));
                }
                if (matches.Count == 0)
                    throw new NotSupportedException($"Unsupported SVG transform: {value}");
                group.Transform = groupTransform;
            }
            foreach (var child in element.Elements()) group.Children.Add(ReadElement(child));
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

        var stroke = ReadBrush(element.Attribute("stroke")?.Value);
        Pen? pen = null;
        if (stroke != null)
        {
            pen = new Pen(stroke, OptionalNumber(element, "stroke-width", 1));
            if (element.Attribute("stroke-linecap")?.Value == "round")
                pen.StartLineCap = pen.EndLineCap = PenLineCap.Round;
            if (element.Attribute("stroke-linejoin")?.Value == "round")
                pen.LineJoin = PenLineJoin.Round;
        }
        return new GeometryDrawing(ReadBrush(element.Attribute("fill")?.Value ?? "black"), pen, geometry);
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

    private static Brush? ReadBrush(string? color)
    {
        if (color == null || color == "none") return null;
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }
}

