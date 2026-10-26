// ============================================================
//  AvesUi.cs — composite widget factories for the AVES windows
//  ------------------------------------------------------------
//  Matches the existing programmatic style (cf. UiHelpers in
//  Cmds_Button.cs). Tokens mirror AvesTheme.xaml so you can use
//  either; these helpers build the bits that are easier in code
//  than in XAML (status pills, stat tiles, chips, the brand tile).
//
//  All sizes/colours are taken directly from the approved
//  "Refined Native / AVES Red" design. See README.md for the
//  full token sheet and per-window mapping.
// ============================================================
#nullable disable

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AVES.Theme
{
    internal static class AvesUi
    {
        // ---- token helpers ----
        public static Color Hex(string h) => (Color)ColorConverter.ConvertFromString(h);
        public static SolidColorBrush Brush(string h) => new SolidColorBrush(Hex(h));
        public static readonly FontFamily Ui   = new FontFamily("Segoe UI");
        public static readonly FontFamily Mono = new FontFamily("Consolas");

        // brand + neutrals
        public const string Accent = "#E64A2E", AccentDeep = "#C2402A", AccentTint = "#FBE9E4";
        public const string Surface = "#FFFFFF", Surface2 = "#FAFAFB", Border = "#E5E6EA";
        public const string BorderStrong = "#D4D6DC", Text = "#1F2329", TextDim = "#5A6069", TextFaint = "#8B9099";

        // ===========================================================
        //  STATUS PILL  — replaces the emoji glyphs (✅ ⏭ ❌ ⚠) in reports
        //  kind: "ok" | "skip" | "fail" | "warn" | "info"
        // ===========================================================
        public static Border StatusPill(string kind)
        {
            (string bg, string fg, string label) = kind switch
            {
                "ok"   => ("#E4F3EA", "#2C7A52", "Done"),
                "skip" => ("#EDEFF2", "#5B6573", "Skipped"),
                "fail" => ("#FBE3DE", "#C0381F", "Failed"),
                "warn" => ("#FAF0DA", "#9A6B12", "Cache mismatch"),
                _      => ("#E7EFFB", "#2E66A8", "Info"),
            };

            var dot = new Ellipse
            {
                Width = 7, Height = 7, Fill = Brush(fg),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0)
            };
            var text = new TextBlock
            {
                Text = label, Foreground = Brush(fg), FontFamily = Ui,
                FontSize = 11.5, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(dot);
            row.Children.Add(text);

            return new Border
            {
                Background = Brush(bg),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 5, 10, 5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = row
            };
        }

        // map the existing emoji status strings onto pill kinds
        public static string PillKindFromGlyph(string glyph) => glyph switch
        {
            "✅" => "ok",
            "⏭" => "skip",
            "❌" => "fail",
            "⚠" => "warn",
            "⚠️" => "warn",
            _    => "info",
        };

        // ===========================================================
        //  STAT TILE  — the Run-Summary cards at the top of the report
        //  accent: "" | "ok" | "fail" | "warn"  (colours the value)
        // ===========================================================
        public static Border StatTile(string value, string label, string accent = "")
        {
            string valColor = accent switch
            {
                "ok"   => "#2C7A52",
                "fail" => "#C0381F",
                "warn" => "#9A6B12",
                "accent" => Accent,
                _      => Text,
            };

            var v = new TextBlock
            {
                Text = value, FontFamily = Mono, FontSize = 22, FontWeight = FontWeights.Bold,
                Foreground = Brush(valColor), Margin = new Thickness(0, 0, 0, 4)
            };
            var k = new TextBlock
            {
                Text = label, FontFamily = Ui, FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = Brush(TextFaint)
            };
            var stack = new StackPanel();
            stack.Children.Add(v);
            stack.Children.Add(k);

            return new Border
            {
                Background = Brush(Surface),
                BorderBrush = Brush(Border),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(14, 12, 14, 12),
                Child = stack
            };
        }

        // ===========================================================
        //  CHIP  — a selected parameter token in the inline settings
        // ===========================================================
        public static Border Chip(string text)
        {
            var label = new TextBlock
            {
                Text = text, FontFamily = Mono, FontSize = 11.5,
                Foreground = Brush(AccentDeep), VerticalAlignment = VerticalAlignment.Center
            };
            var x = new TextBlock
            {
                Text = "✕", FontFamily = Ui, FontSize = 10, Opacity = 0.55,
                Foreground = Brush(AccentDeep), Margin = new Thickness(7, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(label);
            row.Children.Add(x);

            return new Border
            {
                Background = Brush(AccentTint),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(11, 6, 8, 6),
                Margin = new Thickness(0, 0, 7, 7),
                Child = row
            };
        }

        // ===========================================================
        //  BRAND TILE  — placeholder mark for title bars (NOT the real logo)
        //  Accent rounded square + white dot. Swap for the real asset.
        // ===========================================================
        public static Border BrandTile(double size = 20)
        {
            var dot = new Ellipse
            {
                Width = size * 0.36, Height = size * 0.36,
                Fill = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255))
            };
            return new Border
            {
                Width = size, Height = size,
                Background = Brush(Accent),
                CornerRadius = new CornerRadius(6),
                Child = dot
            };
        }

        // ===========================================================
        //  NUMBER BADGE  — the little "1..5" circle on inline field labels
        // ===========================================================
        public static Border NumberBadge(int n)
        {
            var t = new TextBlock
            {
                Text = n.ToString(), FontFamily = Ui, FontSize = 10.5, FontWeight = FontWeights.Bold,
                Foreground = Brush(Accent),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            return new Border
            {
                Width = 18, Height = 18, CornerRadius = new CornerRadius(9),
                Background = Brush(AccentTint), Margin = new Thickness(0, 0, 7, 0),
                Child = t
            };
        }
    }
}
