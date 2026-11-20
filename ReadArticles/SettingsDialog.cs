using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ReadArticles.Infrastructure;

namespace ReadArticles;

// The dialog edits a detached copy. Only Save invokes the host's
// persistence callback; Escape, close, and Cancel never mutate settings.
internal sealed class SettingsDialog : Window
{
    private ReaderSettings draft;
    private readonly Func<ReaderSettings, bool> commit;
    private readonly StackPanel sections = new();
    private readonly List<TextBox> colorInputs = new();

    private static readonly string[] EnglishFonts = ["Nunito Sans", "Microsoft YaHei", "Arial"];
    private static readonly double[] Lines = [1.2, 1.3, 1.4, 1.5];
    private static readonly double[] BodySizes = [14, 15, 16, 17];

    internal SettingsDialog(ReaderSettings current, Func<ReaderSettings, bool> commit)
    {
        this.commit = commit;
        draft = current.Copy();

        Title = "阅读设置";
        Width = 750;
        Height = 690;
        MinWidth = 690;
        MinHeight = 510;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.CanResize;
        FontFamily = new FontFamily("Microsoft YaHei");
        FontSize = 13;
        Background = Brushes.White;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        sections.Margin = new Thickness(28, 18, 28, 20);
        var scroll = new ScrollViewer
        {
            Content = sections,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        root.Children.Add(scroll);

        var footer = new Grid { Margin = new Thickness(28, 8, 28, 16) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var reset = new Button { Width = 116, Height = 34, Padding = new Thickness(10, 4, 10, 4) };
        var resetContents = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        resetContents.Children.Add(new Image
        {
            Source = UiIcons.Refresh, Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0)
        });
        resetContents.Children.Add(new TextBlock { Text = "恢复默认", VerticalAlignment = VerticalAlignment.Center });
        reset.Content = resetContents;
        reset.Click += (_, _) =>
        {
            var library = draft.Library;
            draft = new ReaderSettings { Library = library };
            BuildSections();
        };
        Grid.SetColumn(reset, 0);
        footer.Children.Add(reset);

        var cancel = new Button { Content = "取消", Width = 88, Height = 34, IsCancel = true };
        cancel.Click += (_, _) => DialogResult = false;
        Grid.SetColumn(cancel, 2);
        footer.Children.Add(cancel);

        var save = new Button { Content = "保存", Width = 88, Height = 34,
            Margin = new Thickness(10, 0, 0, 0), IsDefault = true };
        save.Click += (_, _) =>
        {
            if (colorInputs.Any(box => !Regex.IsMatch(box.Text, "^[0-9a-fA-F]{6}$")))
            {
                MessageBox.Show(this, "颜色必须填写六位十六进制字符（0–9、A–F）。",
                    "阅读设置", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (commit(draft)) DialogResult = true;
        };
        Grid.SetColumn(save, 3);
        footer.Children.Add(save);
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);
        Content = root;
        BuildSections();
    }

    private StackPanel Section(string heading)
    {
        var holder = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        holder.Children.Add(new TextBlock
        {
            Text = heading, FontSize = 15, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 10), Foreground = Brush("#273F53")
        });
        sections.Children.Add(holder);
        return holder;
    }

    private static Brush Brush(string hex) =>
        new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    private static TextBlock Label(string value) => new()
    {
        Text = value, VerticalAlignment = VerticalAlignment.Center, FontSize = 13,
        Foreground = Brush("#344256"), Margin = new Thickness(0, 0, 6, 0)
    };

    // Every section uses the same fixed four-column grid.
    private static void Row(StackPanel parent,
        string first, UIElement firstEditor, string? second = null, UIElement? secondEditor = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8), Height = 34 };
        foreach (double width in new[] { 96d, 196d, 96d, 196d })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        grid.Children.Add(Label(first));
        Grid.SetColumn(firstEditor, 1);
        grid.Children.Add(firstEditor);
        if (second != null && secondEditor != null)
        {
            var label = Label(second);
            Grid.SetColumn(label, 2);
            grid.Children.Add(label);
            Grid.SetColumn(secondEditor, 3);
            grid.Children.Add(secondEditor);
        }
        parent.Children.Add(grid);
    }

    private static ComboBox Choose<T>(IEnumerable<T> candidates, T value, Action<T> changed)
    {
        var combo = new ComboBox
        {
            ItemsSource = candidates.ToArray(), SelectedItem = value,
            Height = 30, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is T choice) changed(choice);
        };
        return combo;
    }

    private static ComboBox ChooseLabels((string Value, string Label)[] candidates,
        string value, Action<string> changed)
    {
        var combo = new ComboBox
        {
            Height = 30, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0)
        };
        foreach (var option in candidates)
        {
            var item = new ComboBoxItem { Content = option.Label, Tag = option.Value };
            combo.Items.Add(item);
            if (option.Value == value) combo.SelectedItem = item;
        }
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem item && item.Tag is string selected)
                changed(selected);
        };
        return combo;
    }

    private UIElement ColorEditor(string hex, Action<string> changed)
    {
        var wrapper = new Grid { Margin = new Thickness(0, 0, 16, 0) };
        wrapper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        wrapper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        var input = new TextBox
        {
            Text = hex.TrimStart('#'), MaxLength = 6, Height = 30,
            VerticalContentAlignment = VerticalAlignment.Center,
            CharacterCasing = CharacterCasing.Upper,
            Margin = new Thickness(0, 0, 8, 0)
        };
        colorInputs.Add(input);
        var swatch = new Ellipse
        {
            Width = 22, Height = 22, Fill = Brush(hex), StrokeThickness = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        input.TextChanged += (_, _) =>
        {
            if (!Regex.IsMatch(input.Text, "^[0-9a-fA-F]{6}$")) return;
            string color = "#" + input.Text.ToUpperInvariant();
            swatch.Fill = Brush(color);
            changed(color);
        };
        wrapper.Children.Add(input);
        Grid.SetColumn(swatch, 1);
        wrapper.Children.Add(swatch);
        return wrapper;
    }

    private UIElement VolumeEditor()
    {
        var layout = new Grid { Margin = new Thickness(0, 0, 16, 0) };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        var number = new TextBlock
        {
            Text = Math.Round(draft.Volume * 100).ToString(CultureInfo.InvariantCulture) + "%",
            VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right
        };
        var slider = new Slider
        {
            Minimum = 0, Maximum = 100, Value = Math.Round(draft.Volume * 100),
            TickFrequency = 5, IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        slider.ValueChanged += (_, _) =>
        {
            draft.Volume = slider.Value / 100;
            number.Text = Math.Round(slider.Value).ToString(CultureInfo.InvariantCulture) + "%";
        };
        layout.Children.Add(slider);
        Grid.SetColumn(number, 1);
        layout.Children.Add(number);
        return layout;
    }

    private void BuildSections()
    {
        sections.Children.Clear();
        colorInputs.Clear();
        var a = draft.Appearance;

        var title = Section("标题");
        Row(title, "字体", Choose(EnglishFonts, a.Title.Font, v => a.Title.Font = v),
            "大小", Choose(new double[] { 16, 18, 20, 22, 24 }, a.Title.Size, v => a.Title.Size = v));
        Row(title, "行高", Choose(Lines, a.Title.LineHeight, v => a.Title.LineHeight = v),
            "段后间距", Choose(new double[] { 3, 6, 12 }, a.Title.ParagraphAfter, v => a.Title.ParagraphAfter = v));
        Row(title, "颜色", ColorEditor(a.Title.Color, v => a.Title.Color = v));

        var body = Section("正文");
        Row(body, "字体", Choose(EnglishFonts, a.Body.Font, v => a.Body.Font = v),
            "大小", Choose(BodySizes, a.Body.Size, v => a.Body.Size = v));
        Row(body, "行高", Choose(Lines, a.Body.LineHeight, v => a.Body.LineHeight = v),
            "段后间距", Choose(new double[] { 8, 12, 16, 20 }, a.Body.ParagraphAfter, v => a.Body.ParagraphAfter = v));
        Row(body, "首行缩进", Choose(new double[] { 0, 1, 2, 3 }, a.Body.FirstLineIndent, v => a.Body.FirstLineIndent = v),
            "颜色", ColorEditor(a.Body.Color, v => a.Body.Color = v));
        Row(body, "阅读背景", ColorEditor(a.Body.Background, v => a.Body.Background = v));

        var vocabulary = Section("词汇");
        Row(vocabulary, "字体", Choose(EnglishFonts, a.Vocabulary.Font, v => a.Vocabulary.Font = v),
            "大小", Choose(new double[] { 14, 15, 16, 17, 18, 20 }, a.Vocabulary.Size, v => a.Vocabulary.Size = v));
        Row(vocabulary, "颜色", ColorEditor(a.Vocabulary.Color, v => a.Vocabulary.Color = v));

        var phonetics = Section("音标");
        Row(phonetics, "大小", Choose(new double[] { 12, 13, 14, 15, 16, 18 }, a.Phonetics.Size, v => a.Phonetics.Size = v),
            "颜色", ColorEditor(a.Phonetics.Color, v => a.Phonetics.Color = v));

        var meanings = Section("含义");
        Row(meanings, "字体", ChooseLabels([("Microsoft YaHei", "微软雅黑"), ("SimSun", "宋体"), ("KaiTi", "楷体")],
            a.Meanings.Font, v => a.Meanings.Font = v),
            "大小", Choose(BodySizes, a.Meanings.Size, v => a.Meanings.Size = v));
        Row(meanings, "行高", Choose(Lines, a.Meanings.LineHeight, v => a.Meanings.LineHeight = v),
            "颜色", ColorEditor(a.Meanings.Color, v => a.Meanings.Color = v));

        var audio = Section("音频");
        Row(audio, "默认发音", ChooseLabels([("uk", "英音"), ("us", "美音")], draft.Accent, v => draft.Accent = v),
            "播放音量", VolumeEditor());
        Row(audio, "前进", Choose(new double[] { 4, 7, 10, 15 }, draft.ForwardStep, v => draft.ForwardStep = v),
            "后退", Choose(new double[] { 5, 8, 11, 16 }, draft.BackwardStep, v => draft.BackwardStep = v));
    }
}
