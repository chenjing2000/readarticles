using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Shapes;
using ReadArticles.Infrastructure;

namespace ReadArticles;

// All edits remain in a detached draft until the user presses Save.
internal sealed class SettingsDialog : Window
{
    private ReaderSettings draft;
    private readonly Func<ReaderSettings, bool> commit;
    private readonly StackPanel sections = new();
    private readonly HashSet<string> installedFontNames = Fonts.SystemFontFamilies
        .SelectMany(family => family.FamilyNames.Values.Append(family.Source))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer fontNoticeTimer = new()
        { Interval = TimeSpan.FromSeconds(3) };
    private Popup? fontNotice;
    private readonly List<ComboBox> englishFontSelectors = new();
    private readonly List<(TextBox Input, string Label, (double Min, double Max, int Decimals) Range, Action<double> Apply)> numberInputs = new();
    private string? initiallyMissingFont;

    private static readonly (string Value, string Name)[] TextColors =
    [
        ("#283747", "深蓝灰"), ("#344054", "石墨蓝"), ("#3F4750", "深灰"),
        ("#315247", "墨绿"), ("#513D4D", "紫灰")
    ];
    private static readonly (string Value, string Name)[] WordColors =
    [
        ("#B7394D", "默认红"), ("#9D3E68", "莓红"), ("#7D50A1", "紫罗兰"),
        ("#1F6795", "深蓝"), ("#8B593B", "棕褐")
    ];
    private static readonly (string Value, string Name)[] PhoneticColors =
    [
        ("#0A7785", "默认青蓝"), ("#256CA5", "海蓝"), ("#38735A", "森林绿"),
        ("#7657A0", "柔紫"), ("#995431", "暖棕")
    ];
    private static readonly (string Value, string Name)[] ScriptColors =
    [
        ("#F6EDC2", "浅麦黄"), ("#F5E4D6", "浅杏色"), ("#E1EEDB", "浅绿色"),
        ("#E0ECF4", "浅蓝色"), ("#ECE6F3", "浅紫色")
    ];
    private static readonly (string Value, string Name)[] BackgroundColors =
    [
        ("#F8F8F6", "默认暖白"), ("#F0F3F7", "淡蓝灰"), ("#F7F1E6", "暖米色"),
        ("#EFF5F0", "淡绿"), ("#F1F0F7", "淡紫灰")
    ];
    private static readonly (string Value, string Name)[] SelectionBackgroundColors =
    [
        ("#DDE8F2", "雾蓝"), ("#E5EDE0", "浅绿"), ("#F3E9DC", "暖米"),
        ("#EBE5F2", "灰紫"), ("#F2E5E7", "豆沙粉")
    ];
    private static readonly (string Value, string Label)[] EnglishFonts =
    [
        ("Newsreader", "Newsreader"), ("Source Serif 4", "Source Serif 4"),
        ("Georgia", "Georgia"), ("Nunito", "Nunito"), ("Microsoft YaHei UI", "微软雅黑 UI")
    ];
    private static readonly (string Value, string Label)[] ChineseFonts =
    [
        ("Source Han Serif SC", "思源宋体"), ("Microsoft YaHei", "微软雅黑")
    ];

    internal SettingsDialog(ReaderSettings current, Func<ReaderSettings, bool> commit)
    {
        this.commit = commit;
        draft = current.Copy();
        EnsureUsableDefaultFonts();
        fontNoticeTimer.Tick += (_, _) =>
        {
            fontNoticeTimer.Stop();
            if (fontNotice != null) fontNotice.IsOpen = false;
        };
        Loaded += (_, _) =>
        {
            if (initiallyMissingFont != null && englishFontSelectors.Count > 0)
                ShowNotice(englishFontSelectors[0], $"未检测到字体 {initiallyMissingFont}。");
        };
        Closed += (_, _) =>
        {
            fontNoticeTimer.Stop();
            if (fontNotice != null) fontNotice.IsOpen = false;
        };

        Title = "设置";
        Width = 500;
        Height = 540;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.SingleBorderWindow;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Microsoft YaHei");
        FontSize = 13;
        Background = Brushes.White;
#pragma warning disable WPF0001
        // .NET 10's built-in Windows 11 Fluent theme, only for this dialog.
        ThemeMode = System.Windows.ThemeMode.Light;
#pragma warning restore WPF0001

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        sections.Width = 444;
        sections.Margin = new Thickness(10, 8, 10, 14);
        var scroll = new ScrollViewer
        {
            Content = sections,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        root.Children.Add(scroll);

        var footer = new Grid { Margin = new Thickness(10, 8, 10, 12) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var reset = new Button
        {
            Width = 104, Height = 34, FontSize = 13, Padding = new Thickness(4, 2, 4, 2)
        };
        var resetContents = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center
        };
        resetContents.Children.Add(new Image
        {
            Source = UiIcons.Refresh, Width = 20, Height = 20,
            Margin = new Thickness(0, 0, 5, 0)
        });
        resetContents.Children.Add(new TextBlock
        {
            Text = "恢复默认", VerticalAlignment = VerticalAlignment.Center
        });
        reset.Content = resetContents;
        reset.Click += (_, _) =>
        {
            fontNoticeTimer.Stop();
            if (fontNotice != null) fontNotice.IsOpen = false;
            string? library = draft.Library;
            draft = new ReaderSettings { Library = library };
            initiallyMissingFont = null;
            EnsureUsableDefaultFonts();
            BuildSections();
            if (IsLoaded && initiallyMissingFont != null && englishFontSelectors.Count > 0)
                ShowNotice(englishFontSelectors[0], $"未检测到字体 {initiallyMissingFont}。");
        };
        Grid.SetColumn(reset, 0);
        footer.Children.Add(reset);

        var cancel = new Button { Content = "取消", Width = 80, Height = 34, FontSize = 13, IsCancel = true };
        cancel.Click += (_, _) => DialogResult = false;
        Grid.SetColumn(cancel, 2);
        footer.Children.Add(cancel);

        var save = new Button
        {
            Content = "保存", Width = 80, Height = 34, FontSize = 13,
            Margin = new Thickness(8, 0, 0, 0), IsDefault = true
        };
        save.Click += (_, _) =>
        {
            foreach (var (input, label, range, _) in numberInputs)
            {
                if (double.TryParse(input.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                    out double value) && ReaderSettings.IsValidNumber(value, range)) continue;
                input.BringIntoView();
                input.Focus();
                input.SelectAll();
                ShowNotice(input, label + "必须为 " + NumberRangeDescription(range) + "。");
                return;
            }
            foreach (var (input, _, _, apply) in numberInputs)
                apply(double.Parse(input.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture));
            if (commit(draft)) DialogResult = true;
        };
        Grid.SetColumn(save, 3);
        footer.Children.Add(save);
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);
        Content = root;
        BuildSections();
    }

    private static bool RequiresEnglishFontCheck(string name) =>
        name is "Newsreader" or "Source Serif 4" or "Georgia" or "Nunito";

    private bool IsAvailableEnglishFont(string name) =>
        !RequiresEnglishFontCheck(name) || installedFontNames.Contains(name);

    private void EnsureUsableDefaultFonts()
    {
        string fallback = EnglishFonts.Select(item => item.Value)
            .FirstOrDefault(IsAvailableEnglishFont) ?? "Microsoft YaHei UI";
        foreach (var style in new ReaderTextStyle[]
            { draft.Appearance.Title, draft.Appearance.Body })
        {
            if (!IsAvailableEnglishFont(style.Font))
            {
                initiallyMissingFont ??= style.Font;
                style.Font = fallback;
            }
        }
        if (!IsAvailableEnglishFont(draft.Appearance.Vocabulary.Font))
        {
            initiallyMissingFont ??= draft.Appearance.Vocabulary.Font;
            draft.Appearance.Vocabulary.Font = fallback;
        }
    }

    private void ShowNotice(FrameworkElement anchor, string message)
    {
        fontNotice ??= new Popup
        {
            AllowsTransparency = true,
            Placement = PlacementMode.Bottom,
            StaysOpen = true,
            Child = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = Brush("#EFF4FA"),
                BorderBrush = Brush("#B9CBDC"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 7, 10, 7),
                Child = new TextBlock { Foreground = Brush("#344256"), FontSize = 12 }
            }
        };
        ((TextBlock)((Border)fontNotice.Child).Child).Text = message;
        fontNotice.IsOpen = false;
        fontNotice.PlacementTarget = anchor;
        fontNotice.IsOpen = true;
        fontNoticeTimer.Stop();
        fontNoticeTimer.Start();
    }

    private static Brush Brush(string hex) =>
        new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    private StackPanel Section(string heading)
    {
        var area = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        area.Children.Add(new TextBlock
        {
            Text = heading, FontSize = 15, FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#273F53"), Margin = new Thickness(0, 9, 0, 8)
        });
        sections.Children.Add(area);
        return area;
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text, FontSize = 13, Foreground = Brush("#344256"),
        HorizontalAlignment = HorizontalAlignment.Left,
        TextAlignment = TextAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center
    };

    // Four functional columns separated by 20 DIP: 72 + 140 + 20 + 72 + 140 = 444.
    private static void Row(StackPanel parent, string first, UIElement editor,
        string? second = null, UIElement? secondEditor = null)
    {
        var grid = new Grid { Height = 36, HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 5) };
        foreach (double width in new[] { 72d, 140d, 20d, 72d, 140d })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        grid.Children.Add(Label(first));
        if (editor is FrameworkElement firstControl)
            firstControl.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        if (second != null && secondEditor != null)
        {
            var label = Label(second);
            Grid.SetColumn(label, 3);
            grid.Children.Add(label);
            if (secondEditor is FrameworkElement secondControl)
                secondControl.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(secondEditor, 4);
            grid.Children.Add(secondEditor);
        }
        parent.Children.Add(grid);
    }

    private ComboBox DropdownLabels((string Value, string Label)[] values,
        string selected, Action<string> onChange, bool checkEnglish = false)
    {
        var combo = new ComboBox
        {
            Width = 100, Height = 30, FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        foreach (var value in values)
        {
            var option = new ComboBoxItem
            {
                Content = value.Label, Tag = value.Value,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            combo.Items.Add(option);
            if (value.Value == selected) combo.SelectedItem = option;
        }
        string accepted = selected;
        if (checkEnglish) englishFontSelectors.Add(combo);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not ComboBoxItem item || item.Tag is not string value)
                return;
            if (checkEnglish && !IsAvailableEnglishFont(value))
            {
                combo.SelectedItem = combo.Items.OfType<ComboBoxItem>()
                    .First(option => (string)option.Tag == accepted);
                ShowNotice(combo, $"未检测到字体 {value}。");
                return;
            }
            accepted = value;
            onChange(value);
        };
        return combo;
    }

    private static string NumberRangeDescription((double Min, double Max, int Decimals) range) =>
        range.Min.ToString("0.##", CultureInfo.InvariantCulture) + "–" +
        range.Max.ToString("0.##", CultureInfo.InvariantCulture) +
        (range.Decimals == 0 ? " 之间的整数" : " 之间最多两位小数的数值");

    private TextBox NumberInput(string label, double selected,
        (double Min, double Max, int Decimals) range, Action<double> apply)
    {
        int integerDigits = range.Max >= 10 ? 2 : 1;
        var input = new TextBox
        {
            Text = selected.ToString("0.##", CultureInfo.InvariantCulture),
            Width = 100, Height = 30, FontSize = 13,
            MaxLength = integerDigits + (range.Decimals == 0 ? 0 : 1 + range.Decimals),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            AcceptsReturn = false, TextWrapping = TextWrapping.NoWrap, AllowDrop = false,
            ToolTip = "请输入 " + NumberRangeDescription(range) + "。"
        };
        InputMethod.SetIsInputMethodEnabled(input, false);
        HideClearButton(input);
        bool CanEdit(string text)
        {
            var parts = text.Split('.');
            return parts.Length <= (range.Decimals == 0 ? 1 : 2) &&
                parts[0].Length <= integerDigits &&
                (parts.Length == 1 || parts[1].Length <= range.Decimals) &&
                parts.All(part => part.All(c => c is >= '0' and <= '9'));
        }
        bool CanInsert(string text) => CanEdit(input.Text.Remove(input.SelectionStart, input.SelectionLength)
            .Insert(input.SelectionStart, text));
        input.PreviewTextInput += (_, e) => e.Handled = !CanInsert(e.Text);
        input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Space) e.Handled = true;
        };
        DataObject.AddPastingHandler(input, (_, e) =>
        {
            if (e.DataObject.GetData(DataFormats.UnicodeText) is not string text || !CanInsert(text))
                e.CancelCommand();
            else
                e.FormatToApply = DataFormats.UnicodeText;
        });
        // Removing a decimal point (including Cut/Undo) can make the remaining
        // integer too long; retain the last syntactically valid edit.
        string lastText = input.Text;
        input.TextChanged += (_, _) =>
        {
            if (CanEdit(input.Text))
                lastText = input.Text;
            else
            {
                int caret = Math.Min(input.CaretIndex, lastText.Length);
                input.Text = lastText;
                input.CaretIndex = caret;
            }
        };
        numberInputs.Add((input, label, range, apply));
        return input;
    }

    // Keep the Fluent single-line TextBox, but hide its built-in clear action.
    private static void HideClearButton(TextBox input)
    {
        input.Loaded += (_, _) =>
        {
            input.ApplyTemplate();
            if (input.Template?.FindName("DeleteButton", input) is UIElement clearButton)
                clearButton.Visibility = Visibility.Collapsed;
        };
    }

    private static UIElement WithUnit(UIElement control, string unit)
    {
        var host = new Grid { Width = 120, Height = 34 };
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        host.Children.Add(control);
        var unitText = new TextBlock
        {
            Text = unit, Width = 20, FontSize = 12, Foreground = Brush("#687583"),
            TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(unitText, 1);
        host.Children.Add(unitText);
        return host;
    }

    // All five circular swatches have exactly 28 DIP of space each. No hex
    // input, no border, and the selected swatch displays a check mark.
    private static UIElement ColorPalette((string Value, string Name)[] colors,
        string current, Action<string> changed)
    {
        var host = new Grid { Width = 140, Height = 30 };
        for (int i = 0; i < colors.Length; i++)
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

        var checks = new List<TextBlock>();
        for (int index = 0; index < colors.Length; index++)
        {
            var (hex, name) = colors[index];
            var content = new Grid { Width = 20, Height = 20 };
            content.Children.Add(new Ellipse
            {
                Width = 20, Height = 20, Fill = Brush(hex),
                Stroke = Brushes.Transparent, StrokeThickness = 0
            });
            var check = new TextBlock
            {
                Text = "✓", FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = colors == ScriptColors ? Brush("#283747") :
                    colors == BackgroundColors || colors == SelectionBackgroundColors
                        ? Brush("#344054") : Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = hex.Equals(current, StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible : Visibility.Collapsed
            };
            checks.Add(check);
            content.Children.Add(check);
            // Render just the content, not the platform Button rectangle.
            var button = new Button
            {
                Width = 28, Height = 30, Content = content,
                BorderThickness = new Thickness(0), Padding = new Thickness(0),
                Background = Brushes.Transparent, ToolTip = name + "  " + hex,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            var template = new ControlTemplate(typeof(Button));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            template.VisualTree = presenter;
            button.Template = template;
            button.Click += (_, _) =>
            {
                changed(hex);
                for (int j = 0; j < checks.Count; j++)
                    checks[j].Visibility = colors[j].Value == hex
                        ? Visibility.Visible : Visibility.Collapsed;
            };
            Grid.SetColumn(button, index);
            host.Children.Add(button);
        }
        return host;
    }

    private void BuildSections()
    {
        sections.Children.Clear();
        englishFontSelectors.Clear();
        numberInputs.Clear();
        var a = draft.Appearance;

        var title = Section("标题");
        Row(title, "字体", DropdownLabels(EnglishFonts, a.Title.Font, v => a.Title.Font = v, checkEnglish: true),
            "文字大小", WithUnit(NumberInput("标题文字大小", a.Title.Size, ReaderSettings.FontSizeRange, v => a.Title.Size = v), "px"));
        Row(title, "行高", WithUnit(NumberInput("标题行高", a.Title.LineHeight, ReaderSettings.LineHeightRange,
            v => a.Title.LineHeight = v), "倍"),
            "段后间距", WithUnit(NumberInput("标题段后间距", a.Title.ParagraphAfter, ReaderSettings.ParagraphAfterRange,
                v => a.Title.ParagraphAfter = v), "px"));
        Row(title, "颜色", ColorPalette(TextColors, a.Title.Color, v => a.Title.Color = v));

        var body = Section("正文");
        Row(body, "字体", DropdownLabels(EnglishFonts, a.Body.Font, v => a.Body.Font = v, checkEnglish: true),
            "文字大小", WithUnit(NumberInput("正文文字大小", a.Body.Size, ReaderSettings.FontSizeRange, v => a.Body.Size = v), "px"));
        Row(body, "行高", WithUnit(NumberInput("正文行高", a.Body.LineHeight, ReaderSettings.LineHeightRange,
            v => a.Body.LineHeight = v), "倍"),
            "段后间距", WithUnit(NumberInput("正文段后间距", a.Body.ParagraphAfter, ReaderSettings.ParagraphAfterRange,
                v => a.Body.ParagraphAfter = v), "px"));
        Row(body, "首行缩进", WithUnit(NumberInput("首行缩进", a.Body.FirstLineIndent, ReaderSettings.FirstLineIndentRange,
            v => a.Body.FirstLineIndent = v), "em"),
            "文字颜色", ColorPalette(TextColors, a.Body.Color, v => a.Body.Color = v));
        Row(body, "背景颜色", ColorPalette(BackgroundColors, a.Body.Background,
            v => a.Body.Background = v),
            "播放颜色", ColorPalette(ScriptColors, a.Body.ScriptColor,
                v => a.Body.ScriptColor = v));
        Row(body, "选词加号", WithUnit(
            NumberInput("选词加号", a.Body.SelectionAddSize, ReaderSettings.SelectionAddSizeRange,
                v => a.Body.SelectionAddSize = v), "em"),
            "选词背景", ColorPalette(SelectionBackgroundColors, a.Body.SelectionBackground,
                v => a.Body.SelectionBackground = v));

        var vocabulary = Section("词头");
        Row(vocabulary, "字体", DropdownLabels(EnglishFonts, a.Vocabulary.Font, v => a.Vocabulary.Font = v, checkEnglish: true),
            "文字大小", WithUnit(NumberInput("词头文字大小", a.Vocabulary.Size, ReaderSettings.FontSizeRange, v => a.Vocabulary.Size = v), "px"));
        Row(vocabulary, "颜色", ColorPalette(WordColors, a.Vocabulary.Color, v => a.Vocabulary.Color = v));

        var phonetics = Section("音标");
        Row(phonetics, "文字大小", WithUnit(NumberInput("音标文字大小", a.Phonetics.Size, ReaderSettings.FontSizeRange, v => a.Phonetics.Size = v), "px"),
            "颜色", ColorPalette(PhoneticColors, a.Phonetics.Color, v => a.Phonetics.Color = v));

        var meanings = Section("释义");
        Row(meanings, "字体", DropdownLabels(ChineseFonts, a.Meanings.Font, v => a.Meanings.Font = v),
            "文字大小", WithUnit(NumberInput("释义文字大小", a.Meanings.Size, ReaderSettings.FontSizeRange, v => a.Meanings.Size = v), "px"));
        Row(meanings, "行高", WithUnit(NumberInput("释义行高", a.Meanings.LineHeight, ReaderSettings.LineHeightRange,
            v => a.Meanings.LineHeight = v), "倍"),
            "颜色", ColorPalette(TextColors, a.Meanings.Color, v => a.Meanings.Color = v));

        var audio = Section("音频");
        Row(audio, "默认发音", DropdownLabels([("uk", "英音"), ("us", "美音")], draft.Accent,
            v => draft.Accent = v));
        Row(audio, "前进", WithUnit(NumberInput("音频前进", draft.ForwardStep, ReaderSettings.SeekStepRange,
            v => draft.ForwardStep = v), "秒"),
            "后退", WithUnit(NumberInput("音频后退", draft.BackwardStep, ReaderSettings.SeekStepRange,
                v => draft.BackwardStep = v), "秒"));

        var other = Section("其它");
        var row = new Grid { Height = 36, HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(332) });
        row.Children.Add(Label("需排除的文件夹"));
        var excluded = new TextBox
        {
            Text = draft.ExcludedFolders, Width = 332, Height = 30, FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            AcceptsReturn = false, TextWrapping = TextWrapping.NoWrap,
            ToolTip = "使用逗号或分号分隔多个文件夹名称，忽略大小写。"
        };
        HideClearButton(excluded);
        excluded.TextChanged += (_, _) => draft.ExcludedFolders = excluded.Text;
        Grid.SetColumn(excluded, 1);
        row.Children.Add(excluded);
        other.Children.Add(row);
    }
}
