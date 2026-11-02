using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Enshrouded.SaveDesktop;

// Shared look for every window: palette, theme resources and small control builders.
internal static class Ui
{
    // "Fog and ember": slate (dark) or warm paper (light) surfaces with one warm accent reserved for the next useful action.
    // Controls bind to these keys as dynamic resources, so switching ThemeVariant repaints everything in place.
    public const string Bg = "Bg", Surface = "Surface", Raised = "Raised", Hover = "Hover", Pressed = "Pressed", Line = "Line", Ink = "Ink", Muted = "Muted", Faint = "Faint",
        Ember = "Ember", EmberHover = "EmberHover", EmberPressed = "EmberPressed", OnEmber = "OnEmber", EmberSoft = "EmberSoft", EmberLine = "EmberLine", EmberDisabled = "EmberDisabled", OnEmberDisabled = "OnEmberDisabled",
        Teal = "Teal", TealSoft = "TealSoft", Danger = "Danger", DangerSoft = "DangerSoft", DangerLine = "DangerLine", SelectedRow = "SelectedRow", AttentionCard = "AttentionCard", SwitchTrack = "SwitchTrack", SwitchOn = "SwitchOn", Clear = "Clear",
        ControlFill = "ControlFill", ControlHover = "ControlHover", ControlBorder = "ControlBorder", ControlBorderHover = "ControlBorderHover", Field = "Field", DisabledBg = "DisabledBg", DisabledFg = "DisabledFg", DisabledBorder = "DisabledBorder";
    private static readonly Dictionary<string, (string Dark, string Light)> Palette = new()
    {
        [Bg] = ("#101418", "#F4F2ED"), [Surface] = ("#171C21", "#FFFFFF"), [Raised] = ("#1F262C", "#EEEBE5"), [Hover] = ("#1F262C", "#F4F1EC"), [Pressed] = ("#1A2025", "#E9E5DE"), [Line] = ("#2A3239", "#E4E0D8"),
        [Ink] = ("#ECE7DD", "#23272B"), [Muted] = ("#9AA3AA", "#4F575E"), [Faint] = ("#78828A", "#6F767C"),
        [Ember] = ("#E8A25A", "#C8742A"), [EmberHover] = ("#F2B678", "#D6843A"), [EmberPressed] = ("#D58F49", "#B5661F"), [OnEmber] = ("#1B140C", "#FFFFFF"), [EmberSoft] = ("#2A2119", "#FBEDDD"), [EmberLine] = ("#6B4C2C", "#EBC69C"),
        [EmberDisabled] = ("#2B2620", "#EFE3D4"), [OnEmberDisabled] = ("#6F6252", "#B9A386"),
        [Teal] = ("#62C2A2", "#23866A"), [TealSoft] = ("#16282A", "#E1F2EB"), [Danger] = ("#EE8577", "#C0473A"), [DangerSoft] = ("#2C1B1A", "#FBE7E4"), [DangerLine] = ("#6A3530", "#EDB8B0"),
        [SelectedRow] = ("#211D18", "#FFF7EC"), [AttentionCard] = ("#1C1B19", "#FFFBF5"),
        [SwitchTrack] = ("#171C21", "#E9E6E0"), [SwitchOn] = ("#2A3239", "#FFFFFF"), [Clear] = ("#00000000", "#00000000"), ["TintA"] = ("#74AEE0", "#3B7DBE"), ["TintB"] = ("#B79BE3", "#8660C6"),
        [ControlFill] = ("#1F262C", "#FFFFFF"), [ControlHover] = ("#283038", "#F7F5F1"), [ControlBorder] = ("#2E3840", "#D8D3CA"), [ControlBorderHover] = ("#3E4952", "#C3BDB2"), [Field] = ("#171C21", "#FFFFFF"),
        [DisabledBg] = ("#191E23", "#F1EFEA"), [DisabledFg] = ("#535D66", "#B2B6B9"), [DisabledBorder] = ("#232A30", "#E4E0D8"), ["Selection"] = ("#66E8A25A", "#55C8742A"),
    };
    public static class Icons
    {
        public const string Search = "M15.5 14h-.79l-.28-.27A6.47 6.47 0 0 0 16 9.5 6.5 6.5 0 1 0 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z";
        public const string Refresh = "M17.65 6.35A7.96 7.96 0 0 0 12 4a8 8 0 1 0 7.73 10h-2.08A6 6 0 1 1 12 6c1.66 0 3.14.69 4.22 1.78L13 11h7V4l-2.35 2.35z";
        public const string Upload = "M19.35 10.04A7.49 7.49 0 0 0 12 4C9.11 4 6.6 5.64 5.35 8.04A6 6 0 0 0 6 20h13a5 5 0 0 0 .35-9.96zM14 13v4h-4v-4H7l5-5 5 5h-3z";
        public const string Download = "M5 20h14v-2H5v2zM19 9h-4V3H9v6H5l7 7 7-7z";
        public const string Check = "M9 16.17 4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z";
        public const string Info = "M11 7h2v2h-2zm0 4h2v6h-2zm1-9a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm0 18a8 8 0 1 1 0-16 8 8 0 0 1 0 16z";
        public const string Tune = "M3 17v2h6v-2H3zM3 5v2h10V5H3zm10 16v-2h8v-2h-8v-2h-2v6h2zM7 9v2H3v2h4v2h2V9H7zm14 4v-2H11v2h10zm-6-4h2V7h4V5h-4V3h-2v6z";
        public const string Folder = "M10 4H4a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-8l-2-2z";
        public const string Left = "M15.41 7.41 14 6l-6 6 6 6 1.41-1.41L10.83 12z";
        public const string Right = "M8.59 16.59 10 18l6-6-6-6-1.41 1.41L13.17 12z";
        public const string Play = "M8 5v14l11-7z";
        public const string Game = "M21 6H3a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h18a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2zm-10 7H8v3H6v-3H3v-2h3V8h2v3h3v2zm4.5 2a1.5 1.5 0 1 1 0-3 1.5 1.5 0 0 1 0 3zm4-3a1.5 1.5 0 1 1 0-3 1.5 1.5 0 0 1 0 3z";
        public const string Empty = "M20 6h-8l-2-2H4a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2zm0 12H4V8h16v10z";
        public const string Moon = "M12 3a9 9 0 1 0 9 9c0-.46-.04-.92-.1-1.36a5.39 5.39 0 0 1-4.4 2.26 5.4 5.4 0 0 1-3.14-9.8c-.44-.06-.9-.1-1.36-.1z";
        public const string Sun = "M12 7a5 5 0 1 0 0 10 5 5 0 0 0 0-10zM2 13h2a1 1 0 0 0 0-2H2a1 1 0 0 0 0 2zm18 0h2a1 1 0 0 0 0-2h-2a1 1 0 0 0 0 2zM11 2v2a1 1 0 0 0 2 0V2a1 1 0 0 0-2 0zm0 18v2a1 1 0 0 0 2 0v-2a1 1 0 0 0-2 0zM5.99 4.58a1 1 0 0 0-1.41 1.41l1.06 1.06a1 1 0 0 0 1.41-1.41L5.99 4.58zm12.37 12.37a1 1 0 0 0-1.41 1.41l1.06 1.06a1 1 0 0 0 1.41-1.41l-1.06-1.06zm1.06-10.96a1 1 0 0 0-1.41-1.41l-1.06 1.06a1 1 0 0 0 1.41 1.41l1.06-1.06zM7.05 18.36a1 1 0 0 0-1.41-1.41l-1.06 1.06a1 1 0 0 0 1.41 1.41l1.06-1.06z";
    }
    public static readonly FontFamily Sans = new("PingFang SC, Microsoft YaHei UI, sans-serif"), Mono = new("Menlo, Consolas, monospace");

    // Each target dictionary gets one theme dictionary per variant, mapping resource keys to palette entries.
    public static void Themed(IResourceDictionary target, IEnumerable<(string Resource, string Color)> map, bool accent = false)
    {
        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            var d = new ResourceDictionary();
            foreach (var (resource, color) in map) d[resource] = Brush.Parse(variant == ThemeVariant.Light ? Palette[color].Light : Palette[color].Dark);
            if (accent) foreach (var key in new[] { "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorDark1" }) d[key] = Color.Parse(variant == ThemeVariant.Light ? Palette[Ember].Light : Palette[Ember].Dark);
            target.ThemeDictionaries[variant] = d;
        }
    }
    public static void ApplyTheme(IResourceDictionary r)
    {
        var map = Palette.Keys.Select(k => (k, k)).ToList();
        void Map(string color, params string[] keys) { foreach (var key in keys) map.Add((key, color)); }
        Map(ControlFill, "ButtonBackground"); Map(ControlHover, "ButtonBackgroundPointerOver"); Map(Pressed, "ButtonBackgroundPressed"); Map(DisabledBg, "ButtonBackgroundDisabled");
        Map(Ink, "ButtonForeground", "ButtonForegroundPointerOver", "ButtonForegroundPressed"); Map(DisabledFg, "ButtonForegroundDisabled");
        Map(ControlBorder, "ButtonBorderBrush", "ButtonBorderBrushPressed"); Map(ControlBorderHover, "ButtonBorderBrushPointerOver"); Map(DisabledBorder, "ButtonBorderBrushDisabled");
        Map(Field, "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused"); Map(DisabledBg, "TextControlBackgroundDisabled");
        Map(ControlBorder, "TextControlBorderBrush"); Map(DisabledBorder, "TextControlBorderBrushDisabled"); Map(ControlBorderHover, "TextControlBorderBrushPointerOver"); Map(Ember, "TextControlBorderBrushFocused");
        Map(Ink, "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused"); Map(Faint, "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver", "TextControlPlaceholderForegroundFocused"); Map(DisabledFg, "TextControlForegroundDisabled");
        Map("Selection", "TextControlSelectionHighlightColor");
        Map(Field, "ComboBoxBackground", "ComboBoxBackgroundPressed", "ComboBoxBackgroundFocused"); Map(DisabledBg, "ComboBoxBackgroundDisabled"); Map(ControlHover, "ComboBoxBackgroundPointerOver");
        Map(ControlBorder, "ComboBoxBorderBrush", "ComboBoxBorderBrushPressed", "ComboBoxBorderBrushFocused"); Map(DisabledBorder, "ComboBoxBorderBrushDisabled"); Map(ControlBorderHover, "ComboBoxBorderBrushPointerOver");
        Map(Ink, "ComboBoxForeground", "ComboBoxForegroundFocused", "ComboBoxItemForeground", "ComboBoxItemForegroundPointerOver"); Map(Muted, "ComboBoxDropDownGlyphForeground"); Map(DisabledFg, "ComboBoxForegroundDisabled");
        Map(Surface, "ComboBoxDropDownBackground"); Map(ControlBorder, "ComboBoxDropDownBorderBrush"); Map(Hover, "ComboBoxItemBackgroundPointerOver", "ComboBoxItemBackgroundPressed");
        Map(EmberSoft, "ComboBoxItemBackgroundSelected", "ComboBoxItemBackgroundSelectedPointerOver", "ComboBoxItemBackgroundSelectedPressed"); Map(Ember, "ComboBoxItemForegroundSelected", "ComboBoxItemForegroundSelectedPointerOver");
        Map(Ink, "CheckBoxForegroundUnchecked", "CheckBoxForegroundUncheckedPointerOver", "CheckBoxForegroundUncheckedPressed", "CheckBoxForegroundChecked", "CheckBoxForegroundCheckedPointerOver", "CheckBoxForegroundCheckedPressed");
        Map(ControlBorderHover, "CheckBoxCheckBackgroundStrokeUnchecked", "CheckBoxCheckBackgroundStrokeUncheckedPointerOver"); Map(Ember, "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundStrokeChecked", "CheckBoxCheckBackgroundFillCheckedPointerOver", "CheckBoxCheckBackgroundStrokeCheckedPointerOver"); Map(OnEmber, "CheckBoxCheckGlyphForegroundChecked", "CheckBoxCheckGlyphForegroundCheckedPointerOver");
        Themed(r, map, accent: true);
        r["ControlCornerRadius"] = new CornerRadius(8); r["OverlayCornerRadius"] = new CornerRadius(10);
    }
    public static T Dyn<T>(T control, AvaloniaProperty property, string key) where T : Control { control.Bind(property, control.GetResourceObservable(key)); return control; }
    public static TextBlock Text(string text, double size = 13, bool bold = false, string color = Ink) => Dyn(new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, TextWrapping = TextWrapping.Wrap }, TextBlock.ForegroundProperty, color);
    public static PathIcon Glyph(string data, double size, string? color = null, Thickness margin = default)
    { var icon = new PathIcon { Data = Geometry.Parse(data), Width = size, Height = size, Margin = margin, VerticalAlignment = VerticalAlignment.Center }; return color == null ? icon : Dyn(icon, PathIcon.ForegroundProperty, color); }
    public static Button Btn(string text, string? icon = null, bool iconAfter = false)
    {
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        if (icon != null && !iconAfter) content.Children.Add(Glyph(icon, 14)); content.Children.Add(label); if (icon != null && iconAfter) content.Children.Add(Glyph(icon, 14));
        return new Button { Content = content, FontSize = 13, Padding = new Thickness(14, 8), MinHeight = 36, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand) };
    }
    public static void SetLabel(Button b, string text) { if (b.Content is StackPanel sp) sp.Children.OfType<TextBlock>().First().Text = text; }
    public static void Paint(Button b, string bg, string hover, string pressed, string fg, string hoverFg, string border, string borderHover, string disabledBg, string disabledFg) =>
        Themed(b.Resources, [("ButtonBackground", bg), ("ButtonBackgroundPointerOver", hover), ("ButtonBackgroundPressed", pressed), ("ButtonBackgroundDisabled", disabledBg), ("ButtonForeground", fg), ("ButtonForegroundPointerOver", hoverFg), ("ButtonForegroundPressed", hoverFg), ("ButtonForegroundDisabled", disabledFg), ("ButtonBorderBrush", border), ("ButtonBorderBrushPointerOver", borderHover), ("ButtonBorderBrushPressed", border), ("ButtonBorderBrushDisabled", disabledBg)]);
    public static Button Primary(Button b) { Paint(b, Ember, EmberHover, EmberPressed, OnEmber, OnEmber, Ember, EmberHover, EmberDisabled, OnEmberDisabled); b.FontWeight = FontWeight.SemiBold; return b; }
    public static Button Ghost(Button b, string hover = Hover) { Paint(b, Clear, hover, Pressed, Muted, Ink, Clear, Clear, Clear, DisabledFg); b.Padding = new Thickness(10, 6); b.MinHeight = 32; return b; }
    public static string Size(long bytes) => bytes >= 1048576 ? $"{bytes / 1048576.0:F1} MB" : $"{bytes / 1024.0:F1} KB";
    public static StackPanel Stack(double spacing, params Control[] controls) { var s = new StackPanel { Spacing = spacing }; foreach (var c in controls) s.Children.Add(c); return s; }
    public static StackPanel Row(double spacing, params Control[] controls) { var s = Stack(spacing, controls); s.Orientation = Orientation.Horizontal; return s; }
    public static Grid Columns(string definition, params Control[] controls)
    { var g = new Grid { ColumnDefinitions = new ColumnDefinitions(definition), ColumnSpacing = 12 }; for (var i = 0; i < controls.Length; i++) { Grid.SetColumn(controls[i], i); g.Children.Add(controls[i]); } return g; }
    public static void Add(Grid grid, Control control, int row) { Grid.SetRow(control, row); grid.Children.Add(control); }
    public static Border Box(string background, string? border = null, Thickness? borderThickness = null)
    { var b = Dyn(new Border(), Border.BackgroundProperty, background); if (border != null) { Dyn(b, Border.BorderBrushProperty, border); b.BorderThickness = borderThickness ?? new Thickness(1); } return b; }
    public static Border Pill(string text, string fg, string bg)
    { var pill = Box(bg); pill.CornerRadius = new CornerRadius(5); pill.Padding = new Thickness(6, 2); pill.VerticalAlignment = VerticalAlignment.Center; var t = Text(text, 10.5, true, fg); t.TextWrapping = TextWrapping.NoWrap; pill.Child = t; return pill; }
    // Stable per name (string.GetHashCode is randomised per process), so a friend keeps the same colour.
    public static string Tint(string who) => who switch { "我" => Ember, "A" => "TintA", "B" => "TintB", _ => new[] { Teal, "TintA", "TintB", Ember }[who.Sum(c => c) % 4] };
    public static Border Avatar(string who, double size = 34)
    {
        var avatar = Box(Raised, Tint(who), new Thickness(1.5)); avatar.Width = avatar.Height = size; avatar.CornerRadius = new CornerRadius(size / 2);
        var initial = Text(who.Length > 0 ? who[..1] : "?", size * .4, true, Tint(who)); initial.HorizontalAlignment = HorizontalAlignment.Center; initial.VerticalAlignment = VerticalAlignment.Center; avatar.Child = initial; return avatar;
    }
    public static TextBlock Label(string text) { var t = Text(text, 12, true, Muted); t.Margin = new Thickness(0, 4, 0, -8); return t; }
    public static Border Mark(double size = 40) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size * .275),
        Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.Parse("#F3BC7A"), 0), new GradientStop(Color.Parse("#C8743A"), 1) } },
        Child = new TextBlock { Text = "雾", FontSize = size / 2, FontWeight = FontWeight.Bold, Foreground = Brush.Parse("#20160C"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
    };

    // Dialogs: title + content, then a button bar. Height follows the content.
    // A fixed height scrolls instead (long forms); short dialogs size to their text.
    public static Window Dialog(Window owner, string title, Control content, Control buttons, double width = 460, double? height = null)
    {
        var bar = Box(Bg, Line, new Thickness(0, 1, 0, 0)); bar.Padding = new Thickness(24, 14); bar.Child = buttons;
        var layout = new DockPanel(); DockPanel.SetDock(bar, Dock.Bottom); layout.Children.Add(bar);
        var inner = Stack(16, Text(title, 17, true), content); inner.Margin = new Thickness(24, 22, 24, 20);
        layout.Children.Add(height == null ? inner : new ScrollViewer { Content = inner, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        var window = new Window { Title = title, Width = width, MaxHeight = 760, MinWidth = 400, CanResize = height != null, WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = Sans, FontSize = 13, Content = layout, RequestedThemeVariant = owner.RequestedThemeVariant };
        if (height == null) window.SizeToContent = SizeToContent.Height; else { window.Height = height.Value; window.MinHeight = 420; window.MaxHeight = double.PositiveInfinity; }
        return Dyn(Dyn(window, Window.BackgroundProperty, Surface), Window.ForegroundProperty, Ink);
    }
    public static StackPanel Buttons(params Button[] buttons) { var row = Row(8, buttons); row.HorizontalAlignment = HorizontalAlignment.Right; return row; }
    public static async Task Info(Window owner, string title, string message)
    { var ok = Primary(Btn("知道了")); var window = Dialog(owner, title, Text(message, 13, color: Muted), Buttons(ok)); ok.Click += (_, _) => window.Close(); await window.ShowDialog(owner); }
    public static async Task<bool> Ask(Window owner, string title, string message, string confirm = "确认")
    {
        var yes = Primary(Btn(confirm)); var no = Btn("取消");
        var window = Dialog(owner, title, Text(message, 13, color: Muted), Buttons(no, yes)); yes.Click += (_, _) => window.Close(true); no.Click += (_, _) => window.Close(false); return await window.ShowDialog<bool>(owner);
    }
    // Several answers, last one is the recommended (primary) action. Returns the chosen index, or -1 when closed.
    public static async Task<int> Choose(Window owner, string title, string message, params string[] answers)
    {
        var buttons = answers.Select((a, i) => i == answers.Length - 1 ? Primary(Btn(a)) : Btn(a)).ToArray();
        var window = Dialog(owner, title, Text(message, 13, color: Muted), Buttons(buttons), 500);
        for (var i = 0; i < buttons.Length; i++) { var index = i; buttons[i].Click += (_, _) => window.Close(index); }
        return await window.ShowDialog<int?>(owner) ?? -1;
    }
}
