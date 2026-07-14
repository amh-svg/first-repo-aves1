// ============================================================
//  Cmds_PullDown.cs  —  AVES Revit 2025 Add-In
//  UI: "Refined Native / AVES Red" design pass
// ============================================================
#nullable disable

// Autodesk
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

// WPF — aliases resolve every collision with Autodesk.Revit.DB / Autodesk.Revit.UI
using System.Windows;
using Microsoft.Win32;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using WpfGrid = System.Windows.Controls.Grid;
using WpfColor = System.Windows.Media.Color;
using WpfEllipse = System.Windows.Shapes.Ellipse;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfToggleButton = System.Windows.Controls.Primitives.ToggleButton;
using WpfBinding = System.Windows.Data.Binding;
using WpfPoint = System.Windows.Point;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfVisibility = System.Windows.Visibility;
using WpfBorder = System.Windows.Controls.Border;
using WpfSetter = System.Windows.Setter;
using WpfTrigger = System.Windows.Trigger;

namespace AVES.Cmds_PullDown
{
    // ══════════════════════════════════════════════════════════════════════
    //  Av  —  inline design token helpers (no XAML resource dictionary needed)
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Inline design tokens and composite-widget factories.
    /// Mirrors AvesTheme.xaml + AvesUi.cs from the design handoff so that
    /// all windows share a single source of truth without requiring a
    /// merged ResourceDictionary at add-in startup.
    /// </summary>
    internal static class Av
    {
        // ── Color tokens ──────────────────────────────────────────────────
        public const string Accent = "#E64A2E";
        public const string AccentDeep = "#C2402A";
        public const string AccentTint = "#FBE9E4";
        public const string Surface = "#FFFFFF";
        public const string Surface2 = "#FAFAFB";
        public const string Border = "#E5E6EA";
        public const string BorderStrong = "#D4D6DC";
        public const string Text = "#1F2329";
        public const string TextDim = "#5A6069";
        public const string TextFaint = "#8B9099";
        public const string Zebra = "#F7F8FA";

        // ── Font families ─────────────────────────────────────────────────
        public static readonly FontFamily Ui = new FontFamily("Segoe UI");
        public static readonly FontFamily Mono = new FontFamily("Consolas");

        // ── Primitive converters ──────────────────────────────────────────
        public static WpfColor Hex(string h) => (WpfColor)ColorConverter.ConvertFromString(h);
        public static SolidColorBrush Brush(string h) => new SolidColorBrush(Hex(h));

        // ── Status pill ───────────────────────────────────────────────────
        /// <summary>
        /// Renders a rounded status pill replacing the old emoji column.
        /// kind: "ok" | "skip" | "fail" | "warn" | "info"
        /// </summary>
        public static WpfBorder StatusPill(string kind)
        {
            (string bg, string fg, string label) = kind switch
            {
                "ok" => ("#E4F3EA", "#2C7A52", "Done"),
                "skip" => ("#EDEFF2", "#5B6573", "Skipped"),
                "fail" => ("#FBE3DE", "#C0381F", "Failed"),
                "warn" => ("#FAF0DA", "#9A6B12", "Cache mismatch"),
                _ => ("#E7EFFB", "#2E66A8", "Info"),
            };
            var dot = new WpfEllipse
            {
                Width = 6,
                Height = 6,
                Fill = Brush(fg),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            };
            var txt = new TextBlock
            {
                Text = label,
                Foreground = Brush(fg),
                FontFamily = Ui,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(dot);
            row.Children.Add(txt);
            return new WpfBorder
            {
                Background = Brush(bg),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 2, 7, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Child = row
            };
        }

        public static string PillKind(string glyph) => glyph switch
        {
            "✅" => "ok",
            "⏭" => "skip",
            "❌" => "fail",
            "⚠" => "warn",
            "⚠️" => "warn",
            _ => "info",
        };

        // ── Stat tile ─────────────────────────────────────────────────────
        /// <summary>Large numeric tile used in the report summary row.</summary>
        public static WpfBorder StatTile(string value, string label, string accent = "")
        {
            string valColor = accent switch
            {
                "ok" => "#2C7A52",
                "fail" => "#C0381F",
                "warn" => "#9A6B12",
                "accent" => Accent,
                _ => Text,
            };
            var v = new TextBlock
            {
                Text = value,
                FontFamily = Mono,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = Brush(valColor),
                Margin = new Thickness(0, 0, 0, 4)
            };
            var k = new TextBlock
            {
                Text = label,
                FontFamily = Ui,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush(TextFaint)
            };
            var stack = new StackPanel();
            stack.Children.Add(v);
            stack.Children.Add(k);
            return new WpfBorder
            {
                Background = Brush(Surface),
                BorderBrush = Brush(Border),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(14, 12, 14, 12),
                Child = stack
            };
        }

        // ── Chip ──────────────────────────────────────────────────────────
        /// <summary>A selected-parameter token chip for inline settings.</summary>
        public static WpfBorder Chip(string text)
        {
            var lbl = new TextBlock
            {
                Text = text,
                FontFamily = Mono,
                FontSize = 11.5,
                Foreground = Brush(AccentDeep),
                VerticalAlignment = VerticalAlignment.Center
            };
            var x = new TextBlock
            {
                Text = "✕",
                FontFamily = Ui,
                FontSize = 10,
                Opacity = 0.55,
                Foreground = Brush(AccentDeep),
                Margin = new Thickness(7, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(lbl);
            row.Children.Add(x);
            return new WpfBorder
            {
                Background = Brush(AccentTint),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(11, 6, 8, 6),
                Margin = new Thickness(0, 0, 7, 7),
                Child = row
            };
        }

        // ── Number badge ──────────────────────────────────────────────────
        public static WpfBorder NumberBadge(int n)
        {
            var t = new TextBlock
            {
                Text = n.ToString(),
                FontFamily = Ui,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brush(Accent),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            return new WpfBorder
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = Brush(AccentTint),
                Margin = new Thickness(0, 0, 7, 0),
                Child = t
            };
        }

        // ── Brand tile — Øgland System / Hilti logo embedded as Base64 PNG ──
        private const string LogoBase64 =
            "iVBORw0KGgoAAAANSUhEUgAAAPgAAABaCAYAAACVBZvuAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8" +
            "YQUAAAAJcEhZcwAAFxEAABcRAcom8z8AADThSURBVHhe7V0HeBzF2T6KdLsnmeYYbOt2VzYdEkIPAWJD" +
            "Ali4SJZ0kis2PU9IIfwJvSck1CT0FAgllBBaCIEUQqg2xkW6qt7c5Y477ve/79zsee+0J91JJ8l29n2e" +
            "75Fup89878w3ZWddDhz0FFGXa78aVX/Or2g3fuhyHSgfO9iXEXAdkdfX8m/+zRt+eIPqLQgdrB8qs5IS" +
            "7Unh+0OYh4jrhFyZpT0Ou2Q+P3N5D6tSBw793HXYQWbepRdXUNFuXaAWRptVI9qg6h+Fco3jpFOnMOt/" +
            "tmvowLAyVPO7jEPMOpFeOkWby1Dod6HLq8pHWQE7KbOMXYm17f7hOsrNZ8zT712uHPm4UzTKMBSWRz4W" +
            "nSbLZbpZhfFb/fYJqlTt9FZ34aiA2zsqqOg3oNGXhhWtHX97XQKKtjKk6psximwPKdr7LarxnN+tX41s" +
            "7RfLXSKWuIZ4gqr2UIuq28bXl1Kr6O0ht1YFUpSy7pZ4CkeF8oYdIbPab/B7jJMb1cJfNu+uo+WQlajf" +
            "dtmuSwOKfgnzjLpf2ARyN3uMHbWq9iCVUEbTASRPE/Qk5NZfqWPZERcEceurEOcy/madNCuFN1Tn6ifI" +
            "YLZAmHcXq8MQXp+XTZLDEnmjAXmQ5bYV5rNVKUTbFdwvg7mQj9+go2tn2DpV/7RGKTSkU0ogzJMM0wrB" +
            "/2/Ix64qhGW57PLB+oH+1qHur6C+fJrGYNYtRF0jD0Qj39GETKKRNy5Uh0Vb4j1570o9pM1TGF0EaUaa" +
            "YVX7Q4uqXed3H1Eos5cSrLxGhO+LfHYlLAf/st4oSzzDolCe6oWewidrVGM8e3KZ7T7BLHQuEbRnRNW+" +
            "WIC6ZR0zT5TF8i/FbGf+RccqygCla+oqvxFVf34p9IT1bxc3/zIutmuTUHrvSBm0A0CA6lWe4ayv5ey0" +
            "5eMewe/WLm3yGLtYNrNt5iMvrAuroH2iXyDtYK73BRnUFVSNF5Yj/wy3En8R18+kU0qEFOM1hlkKCanG" +
            "TPnYFXZrR7JctIxYV9a0WyDMF/PIOmvy6LPCiv5TBMuOrlTnFhzd4tGvqFONVjORWvwFyUE0XfztTWEa" +
            "rJA6xViDSnh9ASpDZi0tBHMLi0GeKJTNNv5kob/eLhfjpwQhHA1F+VR9G5R41kKlcASy3etEn5c/5CtQ" +
            "zpkroLhoW5AXbapoK5C/jzuKtpGKjP9FvqlwfrdxnoyqA+a5TsupV7UXSVx2CO1UaEXvGLeizyahqNRC" +
            "FGOj313wEk1ZGVUc8Ps5dQ8dy8JsEPxDmL11Hv0zkojtwLJRGD+kEc+aTcHzZuh+ayhH+7kMDoLrz5D4" +
            "DFMLqVG0NaG8gpOksy3g92WGYWeH/2HgxAALZzjTpS6grrfh/xa4NzEfaJONi+GfQj1mWHa0SPMTvzLk" +
            "HBlF5kDvfCAq9XaYCItYCWyA3lb8ZKHSxXp/4xH0WmfLrGUEWB3zYBLZxp8sJDc7L6ZJhbY2fG8K02G6" +
            "bOD5yGurYryOvPhkEbKO2QOGDqwBuUwFbcXfevyGcp0uvSQgqHgnN6r6BioY/bN+4DdurloRcblyoZgv" +
            "LAIZWYfsPBapxgPVqvcM6SUOWgDwey2siDCJbo5UflWvkF7iyCbB73S59g+o+qski9nG8Y7dPfQCv+ot" +
            "qFIGG1YJuPVhEZf3MBlFAsEpbULHtD9LZ1vAX6cEj3U22oJ69xHDxDoF15eQH+j+9Zgq3YIw61k/1BWm" +
            "hynVuqo8/TsymvQRcBuXg1z1JEasMXcrY18J08a8rQ6F/6HMVsao9ehDUBkRmH+2aViFvTD+bgzCbKOC" +
            "Yj70BSuzr0huFY58yM8OlP/NmWnM7TIF0qhYKOuEf0Huhz912c/t5oFMqMMHmmBhmCQgcZG/LcEc79ek" +
            "tzgCqveeZcIigVUA8zegeH8Sc0kNWAOFiK+Oik+SQ6FXBNSCb0hngWwSPOwp/DotCxLFHLTMskXc3qOk" +
            "t05hJbhpIfJ30KNfKb10APx2SnARHtaCfNwBNOXZFtQPMz2M8J+/6nIdIL10DUzmL0fj7GIP0dcjtik0" +
            "R9Abvj/PNfxgma1uIawYtyyJVWaXIkZ5jPYyqCuiaOdCqZdxdOtrkjM9Kh8bEB1sw7zcIWmtVKeDD12D" +
            "8qFMrbQWQMBojeKdOyvv8JQLfdVu7cJFqBsqFElB4jKPsdHP+94816HxNqICYqSvZ7zNcMfIfLt06hJV" +
            "IBbC1rHtV2PaEFC1z6STQLYI3uZyKSDFLGG1SGKyPHGCd7HYZ8IkOPKzM6hoG9hesfrUZzXmHzVIeksA" +
            "0kmD4FoLLJtOCYu6+DWtW+aX5jo61V9Ip9SYi8bBPOLvaMRdfWmeJkvMtNPfpSLKrHUL3FoAwR+j2WOX" +
            "jlXYuJgDwzzTviuDC8xWhn4TvfMaVmZ/1AfTpOkaUbU2KNG3ZLZ6BL+qXYcpzw4SHHPQhTTXpVMHhJUj" +
            "NUzR5lAf2FEiD48FPcbFDMs6o3VXdWDMrKdSckeF6wlUdvy/pNaVOm47BDz6HehMapohSOuv6G3j20/Z" +
            "IDjziHr9IdpzBy0F5DEQUgpuYqfFzitTgjM/6Cw2RNz6VcjfclqB7Ayr3d4rpLcEIO2sEJzAAPQGO0OW" +
            "A3lfyS1j6dQRIYwQ6D0bY4si/UlusUL+nxmuYwfIrHUbkXz9BCqa2UN3Jqa5xk5OBo+jSjG+ye2j/hjJ" +
            "KUyTvTTM9S9CytBKma1ugXNexPks65kkhfk8VzrZAqPoU+ZKcZNirArkGKdw/or5YB0Vix1fQNHxyOVa" +
            "cLB+KEi5jmShoiKdbk+t7JANgnOLCeG3sjxiJdujj6nN836V1lu3CY6pVONRR7nDbuOupSg3SY6Ob3VQ" +
            "8XKxNAHwmzWCV7v1C5oUfS0HJrZnQNXvkU6JiCjD9ZDiXbsOZhE99odwu4SKBCX+x4yBA3tMboIjjdUE" +
            "60ykufmnz1Lsr36uFpwFBVjFPNrlv7eFDc8tIrEekmuMltnKGNzvRh2DhLGVcMxzy6RTAtgRQNFK0Ams" +
            "YB2yg6nOLYin61e8tzFfsVFQa6f/uoHHDggo2np2lFR01Oc10ntW0FOC12HQQN6ebvRg+on8oQ4+8h9s" +
            "HBLMG/odK8HDucbxMkinMAmOvzu5GDZHGQprR1vGNmL5Ed+ryQdUoGtZIzjhd+ufcp2IIzny8Uv5OBHz" +
            "lCE6M8v9UFRivwiU7kmk/5uemuVWQPGqWXgrke0EZRfbOKjYH8ugtqhW9LPRALb57wuBOfkkFPHpiGpc" +
            "J7OUMbjPTGVmp0fi1uV1XCQjXq2oOAAj8yoSm5YL5ngzrQdaeEzVJDhM1KUkePsRJ+WB4GsZf62qLQip" +
            "g21X5LsL1EGPCB7yGJetQHhaGBxlMecv4nMOBFaCp3NghbASnGsPfFbtHnp/G+JnPNC9nUHVOEt4loC+" +
            "ZZXgKNNotiM6LXS0elA+3vcROw6ptYgRD5XdmTShctCbB3kgRgbfZwECnss6oTJTMcI2W1ccdeB+L/zt" +
            "oCLWq3oT98ylswDiuXk3wfVFfBZStZ9DqXcIJcX8WXjMInpCcB7oQbha5pfWIv6/Szq5Ih6jyOz0Yjqh" +
            "zcDff1kFdfAvEPkVq4VnJTgXCOXj/dCJvs4RlfUMor9Xa1njQFxZJXgQHTQ7LHYobE/5eN8HF2tQWbtQ" +
            "iXEipxIuwmGuOUMG3aeRDsH9uQVjOD/lWgwX+PwWMpiwIzhI/S6fMQz/Fx6ziJ4QHNbI7eaBmmZPYRsJ" +
            "JZ1cNXm6MNFJEuoDScZ0rLKSp9hUbWeda/f0MQXBeTruolbVWEPi0WIIKN6HpVP2Ce7RT2U6ZuckH+/b" +
            "4AIdlOGfbRiZWejOhEoMc217SDVKZfB9Gl0RnOcGOIJxAY6jEPz/XjolwHYEV7TX2FmKXQtF/5vwaAEs" +
            "qgmI8w34S0u4Soxp218jmEIyfHcJHnZ7R8Fs3sRyYyRexw6M0w2+oEQBOXycipgER9rr0EGtsUp97BRl" +
            "g3WakorgBEj93mI1fhZgYXV+bOEO8fcNwTlnWKQaZYF+lqDinVSnakNltrKCUN6wk6h48QbrREhwKnuq" +
            "fctU4Fx2qTqs3K5MfSVhSC1kxsD0dxy6InjArb3MQyp0AynWcQdBOiXASnDM/RbzWVjVxkVUbRtHSsQz" +
            "i2+LCc8ScH9oa95RHUbHZDE7CcbPrTlznaA7BOcBEBDzNcbF9oaltgPxLEGeV8dF1ddTV0gSoTMoc+Qg" +
            "72HJwgU5MCh+hLgzgvOwTMitzWc9ctuXh3/4HHl4KZsEDwwYdoxporMtxMMPXQcfgkg+WIWE2Rj9ISwg" +
            "DzOgcpp5JE9kLEugSZTO4hpFLCAp2h86eyPKDlCUcWwMzudYFrsy9rYwfSoYSSuz1SX87oLzeBSWykzl" +
            "8+dpJ/I5FTfs0S5qVnWxCky3as/uVfNkQH9+bO7BYmRew/BthqFgtPuCYbnABCJNkd4FEP+tUPaNqPeU" +
            "An3YBBFTK7Et6dafN9umOwT3Qxf4AgfLa7YT47AKnyHNOMEzOckWy09HghOsI7TRDjGAoE4C7iGjUD+P" +
            "ZZXgsLAYNw/YoLN6WzwM5g49lo1gDuv9IbEKN7YGVP1HIlNZBCqojSamXbpWYaNy2yuoFE6XQTMC5nXv" +
            "MZ10LIXeEKbLfWi/W/9YZqlLhHMHH1+jaq0kJiXk8V7G55zWoNNazzf1qPAo21wo2f4iUBJ4dBWK+gyn" +
            "QFQuEO+fJDjNXTzfxm0ycSjGoyXUa8Q1KH+5xxgc6UT46iwUdQnzJuKw7GxkSnCeHa9TtHZ29gizBe39" +
            "HuotYfFMiKLPoyUXJ3iG++CpCM6XWVDXftanHEj+g7T+RjJmi+B+RWth+Wh1IR+xbbKQR3uiPxWTwgZE" +
            "gdtFhrKIsOKdCKVfx17TLl2rcGsB+QhXpbnvmYyavGEXcJ5lF3dfCbd70MDrQ6o37TUEEPmBdiiSICc6" +
            "Q2jZgRxduZtARUSdRLivK713AOd9pv7wHLt5oIOr74jvJR5RpTsU9b+Zvrc9D/NlvtTCckHZd4Hs10un" +
            "jAkO0/wJdhLSmquXjzugJr9wBI9lU2eySXDCr+hT61CvjJemNAX+s0JwDI7lKNtqlo9rCwG1MHZ2369q" +
            "C2Sh+02EAsBcQXay+lokTLLfyt7MNl2rsDLRA/5DBs0YEXXYmU1K4VJxxLWfhOVkeUGsB2W2ugT8frdV" +
            "LfySJMK8mK8mNpoKGDuOatwqvdoCdfbPmFKJ1zuXBnP0U6UT3SppFYXh1gzF5mkr6dQluAWFPH3AEY/v" +
            "QCNPjS2H7n4XIROCg9yVDegoWCbRGXn0sdKpA8KewlHWffBsEpyATv6NawBWnewpwavcg46KwFpi2SgY" +
            "LGPbkgGPUYQHq9m4ZmL9IbwsIuLWvy0ylSXU5BYc3aDo1bGRyT5dUyLwA2Ju5yKfDN4toJN6lHNRuzT6" +
            "SoQCK9psc7U5HWBkCXJuyvCsLyp3E8oBgj4qvdiCZEB64ngkLzkIK/qvpZNAIHfoMY2K7qc1wA4A1tTy" +
            "Kpsjm3YIKwWPihV4xE2CY4RKOOqaEcExiHFUFvN4Rfs7pxXSqQMwNRD74L1F8GBu4bGosxbWidlmPSF4" +
            "KEc/De39Jtud7SDy7hl6inDE3Oo3sVNbHRWlr4SdS6NSuCb5dcCeokbRvrUkTZOZygdTdZXdxQKZAA3z" +
            "fH/XJ4Vk5QKazFaX4GUNtYqx2LQ+qNhC6fKG2V5YwIMubYoxEfPrzZxeMT3MW2dyW016iYNvvqGNd3Au" +
            "zjUCEGddMHaJhS14OQTi/BNHfOaFi4AwzRejbQ6SXgRIcO7PgwDz5SNbsNNpVY2tTJvXHTUelhhPMjoQ" +
            "3GUMlk6dIl2CE8jzg6xfxs8ypiK4XO1vlI8TEACJQ27vZQ2KsZrhWb+0dmCd3BbX46Bq3N3fI47opRT9" +
            "dyJDWQLfOoJZ9ng6731TRMWo3l+ip7RdSEoXIdUYjznj2v60iKg0HIWTj0V2Bc5vrbpQD4JByV5sVfQ7" +
            "zJVkzq+b8RukmcvRlSYv2w9zvs94OkxEZAN0nk9xe4idAfeYQc6VjAfPvye9gFhDT0b6dyAff7XGDaIv" +
            "5DxfeouDBBcjvKKthaLfg993dBTtaabJ9iDBg27tbhk8JUyCxxedoUd2cSNvd1jXOjIhOA/IwJJYzkVM" +
            "DgapCC4tjjVht3aXNW24/dFsZw5OHL3F69wgt4wiBgQO0BM995dQqWBiPCKzlDWgbGvZuKbCphLmodVj" +
            "7AjIs8g9BZRWrGQml7OvhIrJBueFiDJLXYL366EuPoiZeXIkQDxUvNj1Stoikhp1uo6/TX/yLPeMrm6x" +
            "5RtntTD3WzzGEo5K1DnGIzoixMu4kcZyztep1FzwZPwgpn+2xzhZRpMAKPpsdhYcac073ZKFHQDLIUZC" +
            "RfuvDNopSHCSxVxFZydjF/fmvCPZAf5dBuOe9rMxi0Lf1RXBMZDsh3nyNC4As/OR9f2RdBYEZ52zfuzK" +
            "x3JxxGa5yB/Mv1uCbu/3ZfDdYEEYCRusv4T772jgrI7gEVUrboApyEqwktlOqMRoqJnzLAs43YUgiqK1" +
            "8x1gu7L2lTB9lO1TKpLMWqcAWW5np8QOESRu51SDW0UkEPWDCkXFYl2J34xf0VfXqtoDM/OOSP3ecRLq" +
            "3fqwBkV7CnnbyVGSaZpKSyVn3NTJesVYVd3F22dIP8ALHO3KbxWa+EhnTWcXOFqBacaYdsTLvNnFZwrX" +
            "HNDWr8lgXMV+iRcxsg5n5w49Rj7uFBHFeJ36z4VRcGCWfGxeurgylR6xXdgBRhT9txhQ7g+luH1HmFz9" +
            "LbxU0Hz7JltAoV+WvWkHQltFjlK8gYMr+D0GCeVXvWc2oEx2Ze0rYfoYIc5Nh+A8Oci6kATeaZqxfFuu" +
            "1q1dFFC9n6CjbA2pWisI2AqivMU2Mw/FdAc8Mdfg1i9Aup+CECJu+ffnzPvnaSxsYaQ9uTWNeqafOTne" +
            "r8pgXYLvsacTL+vAegd8IHfYMXwGXfpWqteMk8EFN4Zhetb1Dm4xck0qlR7V4Dk6hG7dSbjXo85tFDZ4" +
            "9DnWVcpUwl4QJuKGbB+P3ZuATuAAzuswKrSBYCH5OA52ErRMMEE8kH/l46ygN+N2sI+C5nk6ozclZpLq" +
            "TQu92bs4f2/FZ5g3VuV0fs2vAwf9Co4EIOyTPD1lR2ir0DznvA//V6RjyjpwsNcBE3nbV/P6UhZ6Ct8I" +
            "u3XbS+kyBS8MxBxoC7cOkgmdLFwlbfUYW0J56Z+u6gpRVwUvGvxto6LblrWvhOmjHp5Gx5Xxth+/8RZ2" +
            "F9zdYlOGejyr9XhtrwEOqtpdTUrhm/SHjvONQF56897YGW393laEafMYr0c8+lXSKY6FB3kPC3s61msd" +
            "f6vaS3Ue/TTpNQHBnCGn2pWjM6mBBBTtNeulhXheuTiDV1up03zDTwaPg+cHYFn+pQZ5Yt6rVe3FD3sw" +
            "NfHnaCe2KLF8sZxB1fsr6RQDV+84inGVtL9kQ574/MzTMks9QiDX+xPMq3fF9zA7EW5/hFXtZR6skMF7" +
            "jKjrzv3RgKu53WNX1r4S8dKM2/uKzFbamJtbMBrKF+IKMrdfzBVuU7gtw5cjIor+xExlcMKNN9wT5kUI" +
            "9MP74vhKZDr3cwfQuXO7jXnmKjq3iKSTgF/VftykGKu5cmy3HUb9bVKNTejQbpRB4gjmaiVrkJdY3mPC" +
            "OKx1RT3gM9Od23d8cca61YU83rcx78h4mOQ8JMsW+A24Y6+EWlGDOuMWIP0wTfHWl8e4WDpnjIBSMI11" +
            "zTxxVR910CadYsBo8555Yqi/hIqEXjjts9OpQPM8rGjvssHs0kkWVMquoCc7q+cmuFCEkaxNKKpNmn0h" +
            "nHow/SplqO2726kQOtA7skHVt3D7KrYHLcqQ8Pomn9E6EicEFe3v1qkNR2Io2JtUNoYPKPqGrk6NtbmM" +
            "QxAmwK0fHldG55hwUWC1R7/S3D7jegnLZs0P6noLw9Id1hi3mn5KPZDBQfCCi9lZyXBCkIbdzT47zf+5" +
            "74x4t/PtMxkNT3zeSULSXYZPqJdkgQ5uDChefi8sAXwVmnk2t29ZV+jQPrHmORPA0phj6hrLybqUTjEE" +
            "PMx4eoToLWFvCXPlLetdVd0BT5FBGb60NmYqEcqiaEvr849JuFusp8CoeWWtqm+nktil2xfC8rOxuU0m" +
            "s9UlPgcRI279FY6+tH5IqrBi3BpJfn3TE7vqiAoqlFPRbpZRCARUbVybWriZR15RD7v8qs3hCwsiincS" +
            "O5R65lkxFtfnW7ad8o443PxWGOuzRdXXciqXkKecoSeHoDvcO2eeRP4t7zTM9ehDmAZfeqGgfip4cyz+" +
            "1pMY4nSbqn8E0hWbflBvE/DMZ70TwCQ49Qbui3g/fEI+koSvwdpdGJpMcNZ1m2psQZuVSy9pA21xGupZ" +
            "vEDDuOwJrmr3840heuhPEb03eluZrW4h6DYuZWeFxrFNwypsXJ44y/biGpTtSV6Ra5dmXwkJDkXc6Vf0" +
            "c2S2ugS/E0YTmArHETHo7njvmgmMbPdQmWhqopNs53e6pJO4MQUKPIOn0UhMWlQ8xSadE8D1AYy4EaYL" +
            "Yu5C3SW8qBJRC3wkldlh+XPs74BnGyJPz/GFFHG0U9Xe5ZXN0tkW0JFP6ZcdAgaGLqeHJsGpN4g/0QzO" +
            "AMkEpzAPQbUgZX3bgWUGmcVljmY8tgQPeQpGo1E387ic6bGvhQ3IXhyFzOjstBW8QgeN1pzO4hqlFf4w" +
            "Qk3MJsF5gQF69zc5wtml2VfCKUpE1Z6iySyz1iXMu7w4UpLgVZ4htotWBA9gLEeHzLkqT2HxYI90EvDn" +
            "ekt5jp061eYp3Bx0238MD2leBeXeStLUqNoXyZ9Lwpx+PE/OxUZNPfh50ssmVvCSDh4d5ZoSy1/VxbkG" +
            "kGwmCS6nc8/JxymRRPD53V23MQnOU4IsF4Vkh96s5j3t0luXCCrDvoUpyUZyh+1ltlsHghMYyZromKwo" +
            "fSkifUX7Q3cJR7MI8YjbQ5LjThZWbp2ifVmTY8ReqcsSMBqdwwZjpdul21fCXh15sL/0PgWSCT6vk85W" +
            "3LKiaj/GtOp6lPn66vzBCffXRVwn5EJhZ/F1UxIU5Hgr+RALw9Qo+hyOujxKSktSOsVhEjw23dD/3dkr" +
            "nvzoITrW6xHn9cjbtZ35Jfqb4NLS+Aw6X8U6EIeyFKPLjzISTBv1+zjrhmFB6ncwuM1fgPzZEhwj50Ns" +
            "1P5UTI68yORGjoIyWxkBBXsahd2ZThmESeTWXqQiyuA9hjCZVOMazgX7sx7ZjrDI5pOEMmtpgaMw826a" +
            "6NVKwTTp1C2EEd6cq/MGk+Q326oxx+V5ds7VGxV9md29Z1yVF2sB8MNzDfPcBVm7L6C/CS4OYinaoyjj" +
            "D/miE8mKumjguoP0mhLQtQNB8LXMCyzRdfzYATraWTLOjgTny+FsXJqtbNz+EM7D2JjI+E0yWxkB4d4X" +
            "iz5S0VMJFRhzx52h3Oze/dboOuwgdFCb+/MlE450S5A+RsMPZLbSBt/jxug3lyvRHMVRTxtDudpN/GCE" +
            "9JIRaFEFMb8midguULx/SieBgDu2cs7tIii87Q5KWC0o4zSABKfJD4VeGnYb50vnHqG/CS7L/SKfoW5W" +
            "iZ0J1pPlzrlU8CsFU7nbwS09s145PRVx2hGc5gwq8ZdtqlGDBPpNQI5aZPQhma20gTlh+XzVWEvFTCZ0" +
            "srCB/Kq2PNNbU7tCILegvFbRQ5hL2patLyQMgZldg84u7dVzK/iZaCqZmMJAZKffEFSMx5bm6ydkahXA" +
            "MryW9c240D7reEsrn5OkmJ9vIMHQkaxMdd8bryVG2KfklEOYsVDgDW0eY27Q471tFfKUfAlEukB9dYvg" +
            "7JQQtkU+zhiJBNf/wmdI/wccwbmTEFL0Ranqg2B5azD9YV5QdzvMtmaeUhJ8bwdGrO+le++a7IGfkUGz" +
            "hu726Hsa+CYZTWcqMkcV/mWdSXLVNnPenZv6PjMr2ImibRYwHrml9gbriUeJOU3iwio62y7PP8D8fJaX" +
            "WYp8QGjtMU4eEkFnNIMfNcx0H7m7BOeUA/XAjx/ciHJcbyesI7hPt8uTleBI91U+4+IyyrOdi5IsY7iT" +
            "m3jCijaBeWb5kdYS8621fZbgcnGtXvR+aQhv/QxleADkfw28Mw0j5j+gbBs4ryMROIJyAZEmM0z5bRFF" +
            "e2dO7tBjZZCUCCnenzAOXrqIDniH/G72SioyLJ4v/G6jUHpNCW69tSqFIznPxDx1G+PjdIR5Itm5l92g" +
            "GHPrVe26/7jSe6e/uwTnIMJpHq0K/rYTvhOO+BfavTJqR3Ba0AHF+292erzhxW/zFRiCFg06ST/bQc63" +
            "b+XaD2T/fZbgfM2TJiUr3UpkO6FS1Sn6vEwuI/xfBu+0W6gUPgnl+YiKS1Jx5KDJzREVo2kzz0JL77ag" +
            "e5Oqi5N9IMcukpKjIKcCPP6Zya6J+A65apSBlPy6aoAKzRGdCk9rgGZuo8eY3dCJiWuiuwSnHu3uWOxl" +
            "Gc90KFqT3TalHcEJXu/djHk1Cc6bacM5hV+XTnFU5xYczfTpB9bLQgxu4qYb1CHPHqRPcH4udQVPAGF0" +
            "7C/BPGvwTMth/1RAgR5r9BQKxWHhUwl7Xp51hkn4Mxk0K9iYlO++ltmQFn7PvYsrk3oCnsgCEUZxARRE" +
            "FV86YZ2SUBFFX8zTYtKrLarUgp9hLi92ONgR8568esVYbD0KmilYdnQSo/hyBUbwVSQ688SFJ4yGVV29" +
            "wNFdgsdMY205v8nOK7HspMldOIqXZXBklcHjSEVwAvXrZ93yoFRYMR5L3lr0K9oT3BbDdGcnyhi/ASlj" +
            "godBggbMb5CBzRDbs7a9LTUQjBSruQUgs9UBvFgRFR9iQyFMp8L5DRRhNS+Hl8F7DFTmI4jTNv99JZia" +
            "bGngNlM3F9YyxRqYwJj7vcm9a6QvFuJQD7dIZ1uEXAfzCyfrSA6SnGSEMj8unXuMRepRXuRptuhwED9X" +
            "3P1dfDyyuwSPWSJaq3ycMTojOD+CGLudVujrtpoBw4+WTq65bu1IWCqNsbUMfbO1U82Y4CKAoj/J+RbN" +
            "H5pU/SGcL4cVrWGe61DbeRVGZR9Ghk3prJ5znoYesE4G7TE4clA5aK7a5b23hWUWe6cevvjQt19C5dyy" +
            "TjHe4ZaaPB7Z6aoy9Gm/oEe7haOP2VbZvqKrOv+oQZyHczGQ7YI8dbpV2EOCZ2WbDOkmEJxWWK2i/Yf1" +
            "Sn0FkZ/lc3g6gPXHhWSeB4Ae/9FqHWRMcBPoRR5iBfCjACZR+lrEm25u73P8QKLMlkDE5cpFJ/Tb2Dle" +
            "+7CmcNRgw2DUeEoG7xECbu0BnhlIp2PpDWF52NBQgg3VqlYis9VtxK7o1X0ga1kwz/5YaTKQj/u40ESC" +
            "QxE7XPOUjFB+4UgSyqwz3kUmnWwRhonLr922QWjuysedolq+ycbzFFD0+G2ndtgTCU7wRB/rlVYOiLyM" +
            "OxGzBxw3EJbnenbsXFwL5BYkvGfebYITKNjDrDTRU8vG6UuhMrM3q1O0WQHLSaagWgCzTNucDskEIRBH" +
            "zYCCuMnTXdQo+q8XoiLTWdTrDWF5qWQwzVdXdzJ9yQT8AgzqcsMavpev6pusFwmmAkj9cGzVN02CI69W" +
            "gneVBuJ8i/lZzTx59Nnp3NdGUpPcezPBq3MHnxBRY6fUoPfc554WVoeO4/SG5cIIX03dl94FekRwogZz" +
            "rEa1sJYJkCyigfpYmDaU48tArjaOeeJfTh/SyQ8rB4Rs4GWMokDdBOa5j1Cp+4vcLAfTR5nvy+Sq4nQA" +
            "BW5mRxqziLr+OAA6+3vNERxKVSMfp0SmBK872CgEydezjRd6jK0IUyGdUgIj3hscjKSJ/qF8bIs9leAE" +
            "pn+vsF6p8/DzSgh55fSmzWNs86vaddJbHD0mOMFVy0aPMZMms7mY0dcjOldfodwbeBgjoHib09n7Zh65" +
            "lgBz/gZZlIwRzNWK2xTjNVZ6X5Ob+SexuQ2Enn0Lyp7VXQATIbf2IBfOqMCYy4Y72+MmORsVPUjyyfvR" +
            "sz6CRwYNyoeyruG2k7ASVGMGrzuSzh3ARUb4m8/8x15j1S+RTrboIcF7ZZHNRE2edmKd3HVg23Odh3UN" +
            "832h3au3WSE4McM1cECLqo3D/OATzhGodMxEOqNoNoTKzsJmQjS5er6OR0llMdIGVyoXeIxLEcdWjlZ2" +
            "8feWsHwkA+dctYq+Fgr713Q/RN8d8MAJph8rOIqT6FCYhnq3/u2qnN13nYU9hV9vxjPkr5F+qHwIszyd" +
            "m2MyJTiUdn++bAI9WxOzXAQhPub94fygofQm5uqYm5aGFO0L7g9zBEfew129SAQ/3SI48wIStQdQD3b3" +
            "lVulBpJ8JDodgjPvYVX7I7fLqPPkl3id2u29EvXS4dxA1ghuRdCj8+aSD3lcUIwwyAx7WzZgb4/smXQq" +
            "tDYwj5kts50WqFyoqFvqMErRZOXiRm+XifGzlzYPS/AtKz6H5fAID5zIrPUqqlTt9CbFWEJlEm2Kv6i7" +
            "rcjbr6BAD2Gk3shn7GTpzk7Pn+tN/BZWCmRKcBNcdCJpmR5Fbssthdn6AF8vRucn6ytm0jaqeh2/niKD" +
            "p0R3Cc68U/c4wHUm7Ci5EBv2FCQcWEmH4ATfuUdZlrPM7Oyhi1H/gfYXePQKwQmO6Dzwj4q+mS8AoDed" +
            "y8zQJCYx+lt4WoqX+VVDGWSWbcEvMcJEvbBG9Y7H/KcOlbmKxw15WZ9dvNkWEoV1hnpsj7AOIbUe7aLq" +
            "ND9bm03MVb1n8pYWbsmYxKJiU3lIInaYrFO09UqM3lNTbWEmg9dprc07EuWMlbmmixNwJvwuHs/UpyIf" +
            "m3lQibsH7AiZH8bDPJGkPD2G/ASrlMKEiyBTAYSoYX42QRD/G/JxSnDn5Mu8o2R7dbxkMVnYnqxDfgFU" +
            "RiHASxc5KG5BXEj3X/KxLTANnUc95GWP6GjfSfW1FBIcHd4XjBPlWiAf9w6qFW16m2rcBROj36UGwp6X" +
            "HZHMni3qBg4cwCuKMGrey8sfUUkPQX4GsY23N4R1Vpfj/ZrMUr9iTs4RX21CnlAXf+JoRUKR6ByZoGh/" +
            "XYK8wu106T0t8NAGy8g2YXkzvQuvyu0tWoTwINpHnKIxP5wPo1P8Eh3S43VovzkYHaX3LhH26FcwP4wz" +
            "nXME/LzyEnVYh3ZLJSwnJfkNPH7/jvfaMS7k3fYKKhOYioxFnd8l0nUXpvyQJC1OpHkt/fHFK/nYgYOu" +
            "EfAYp9SBzCR0M6S7t39mC+ysmQ/mh/kKZPDNMQcOHDhw4MCBAwcOHDhw4MCBAwcOHDhw4MCBAwcOHDhw" +
            "4MCBAwcOHDhw4MCBAwcOHDhw4MCBAwcOHDhw4MCBAwcOHDjY2xBUCkfM9wy/PujRHcmihCHV+YMHyWp2" +
            "4KB/wO9/b8o/Orowb7gjWZLFkCWQ5CubHDjoc4RU4z4qZdBjOJIlCUMi/D9vz7iGy8H/MByCZ18cgjvY" +
            "Y2BL8LzCaODAIdFq1yFJciiv5hXuCf6TBe5+1yCb8IdEAwcMiQZyhtq7ub1wK7B3QxjbdFPmNbvCcqeq" +
            "E+bbmjeH4A72GNgRvPqAgdGWsVOia//2z0R5/e1oxHsyFB1ks/hPFv8Bg6Ltt93XMTyktWRatOn8Ulu3" +
            "+q+NjDaeM8bWrenbZaJzSE5L5HXMZNsw2ZQa/ZSUdVJ/0nkJdeIQ3MEeAzuCV7nyo4v/746oHWqGn4lR" +
            "6zAxoqaSKldedMP7n8gQiVhyw8+jCy77kfyViGYQv23CVfJXIhZcfq2wCqz5jOV1QHTxj2+TvnoPdcef" +
            "m7JOmr9TjrwdHs/THk3wsrFl5ZPKJ/3JN873iHyUEmMuGHN0RUnFq75i39sV4yve9pX4Hh85susvQfYF" +
            "yovKT5lYOvGl8nHlf5tQOuFt5PHBoqKig+DU4fMv+yj2Gz92/MWTfZNfmFg+8W3fWJ/tJ4bsCT4guuj7" +
            "N0n13Y1dW7aI0SqYPywa/spxKSWQVxBd/+77MlQiFl97W3T+lO/JX4lo+lZxtLXsUvkrEfOnXpNAot15" +
            "PSi66Hs3SF+9h7pjz05ZJ00jx0f9+x0Rz9MeTXAQ4uMrplwRrSiuiILkKb+HVTyq+NiJZRNbK8dXbkOY" +
            "efA/b3L55ChI9XppaWlWv36ZKUrHlH59wvgJiy6pvCSKvIXMvPnG+5aNHz8+qx+f31NRemHp4ejUvmS5" +
            "J5VN+gQEHymdEpAJwaO7dkW3r1gV3da+PLp92YqUsq19mVB8O/QKwa+5UfrqPdQdd87eT/Cy4rILMHov" +
            "B3GjFJDjYemUAJDnSChNDZRnBckkH7vKxpS9NX3S9GhZSZn4zG/puNIL8WzUnXfeuT8tgpIxJeI7Wxjt" +
            "K6dPnP4SRvxr8FN8MXHqhVPzysaVXWrGR0sACjoReagEKQcizvGlF5deiN9jL5lwyYtw89lZC1dffXUO" +
            "0np/2oRpUSj1ffKxC/7/b0rFlGh5cfmT8pELI9y5tFYQ5zPssPhM5LXYVwFLZrJvjO9MjP7P4f87mFbF" +
            "2AodHdqz+HtTRUXFAXyGfE1Gnn3I+zlTfFNeQtjrReQSZ5999gDk5x6k8xL+3suy8DnSHlk6tvQa+D8B" +
            "6f8c8T5ddG6ROBjBMrMt8K+wNoqLiwegrq4oKysbzt9JYH6nod5prfDj8CIM8vM9tFEUbhtZFuQ3n8+T" +
            "kRHBs4BeIfh3r4/u2r69S2EH1QF4Zuc3WeqO+eY+QPBxZTdMmziNxL4Ro94nE8omzBo7dmyHz8xUjq48" +
            "/9JJl0ZB3ir5KI5rrrkmrkhQrjeEghWXNV057cro+DHjn8Hv9ypLKhdVllbWQqnb0UnMhuIPmzR20lfw" +
            "Owq/dzIs0vWAjGuQl+XlF5efjr/zoLBb4bcF4Zs5MiG/H4mELGCHgpF7G9KZcafrzoTPrfp8vmHonI7n" +
            "/+gs7kYnth1TjGWQlfh/eeXYylPphnQXgNg7kdZyEGuTGP1LfK14XoV4v2TZy0vK74HX/ZhH+N2OvysR" +
            "T+3Uiql0e43xIC9HwzyuQ7m2wC1cWVy5FP5qrz7t6hyU50WYzyTgJkgQ+dqJeOaNHz3+eKQbRpqbQGzx" +
            "fTK4vzrJN2mnJH0cIO5xk8smz0OH2Sbrcwn+flJaVOpFOk/Cmooi3e1I6012TjJYAvYWgreUTIvOdR0g" +
            "8pYonqh//8HR8KDjE6Rm+BnRuhO+BTkXo+/Z0dqjz4p+WdcoY9uNjTPnRuu+NjJae8xZMb8ppDpnYHTp" +
            "DT+ToXZjryH45DGTD4VyzIQyLOHv8rHlf75s8mVUwGLhwQIoy7lyNJzB3zTJEe53IMHDkLsR5lY+x7M/" +
            "kwxQ2HehnNfj9weME3/vpTuenS8IMa78iaKiIjcUnOQQH6CXBF8Ht1VQ2DPwtwZkWYNR7wy6Q6n/hTxs" +
            "AykTPg3MNQRBwHHlKT/uxpEbYZdjVPX7LvIdVjG6YjDI1oZ8zqQ70l2M0ZikvgYj7jH42w6rgXFOJwHh" +
            "dynK+Ir0u4kjJf5ezt+I44FLKoTfEuTxtCumXnEdpgZi/gs/3yepS0eVngr35y+dKDqK12gNII3Hr7zk" +
            "yijq/aewHKaZ6Yk4i30hxFvNOuFvE3j+MuvPJD78PCziGFd+FzozlR0m/CzrbF1kbyH4unf+E13+wOPR" +
            "Fb/5fQdZ/uAT0WW/fDhBGk6/SJTDv9/hGPm/IhYGN/vDMrbdWPfex1G/2Jo7LOY3hXDhcOlN98hQu7HX" +
            "ELxkbMl3qCxQkqUkERTjMmmm302zVXoTIMElMcWneUtKSgoRbj6UdDHnvfh/J58jjregZNWmguH3k/g9" +
            "f/rI6Qp/E0irBUr9+yIQnAoJZRWdAxUUhLCO4A0gb1AEAmiCUpkR5wvykQD8TLh8yuVRxPuufCTAMqCD" +
            "OJ1TAOTvZoYdP27849KZ+XgBZv0OjLLTkFaInQ2JTzf4r0bYKMopGgzuc5Cu+Pwr/t8I2W6Sj/GLuiku" +
            "nwXiHlw5pvKr+P+/HGUxmtahjjb5RvmORfjnLp2Mzg/TARnPw8w3LKcfTZ8+XcHo2w63GZBp6Gw242+H" +
            "T/Xi2asob7386WI4xL8E+f0VTXJBcFohqEvppQP2FoJnisXX3S7KIcrEPWyQeHOwRrruxvr/fhoNHnRk" +
            "NJBbkFAHycK49mqCgzD/xogWhako5t9UDio5FGT9lKIpXH2OAyb6CMyhSfAa+UgAI08Bw0GhV/M33N+B" +
            "fCwcATx/FmmsAQniH42DkjbDzzNQyFxB8JKy2Op9NGb+ws0keD38RoQbALfrMDoyf3+QjwSE2eqbXAdS" +
            "0KSOnwVG2AoQmHl+DZ3ItbQk8Pd30pl5fQ1m8XbfaJSixFcjyy4WGeEWwnQlitFcfBAfz6sR31+k20b8" +
            "v50WEH+Xjis9SxL8Izy/Y2olOs1i39v4/SPfWN8v8P9O37d9BQj3vCT4ZSKe4vJHSXDEfTN/4/l9eLYA" +
            "Ye7E350c5fncCvh5CxKWPzlXPwX1uw5xJBA8eeS3IhOCcy668rGno+13PxRd9guMlCmk/a77o1sammWo" +
            "RPQVwbmyzvm5KNP/OsFpYoPYrRg9VkHxyri4BNOXo/idGJF3YXS7X3oVEMpTWvkATNNdIEOgZEzJefA7" +
            "DQrVBKGCraI/xPUOOg5h9hKI/6Yrp4pR9/dwO5G/aYrC/w0VIyvyOWfE8xV0g5+n5Rx1kTkHB2l34O9d" +
            "44rGnQSlD5OEiL/Dh9HLx5TfxZ0A+FlNawSku5GKD/9flo8u/4avyDcI5V2OEXWWTOsypLUTc+aXRXhM" +
            "U5IIHrYSHL/9CCNGcPj5gh0jOou/MC6k+RmtGBIdfn7BcMWji8/H6K/B/QmkGa24oELH/39KIvhjJDie" +
            "3yLiHev7LupjLcIvRRpzTzvttBw+t4LxS6vrJ0wbbfGc6PQwtUB6h4jOFvWZNYJv3RoNDzwOc+ED4Sc/" +
            "pcxB97z29XdkqERwz7rfCB6qla67sf6DGekT/OZfyFC7wTrZ4wkORbnv6ulXc/73G/lIAMox6apLriJR" +
            "/isfJQCK9GuO5FRwOWo9gbhuoxLTHb/fg8wTniXw+1HOb2klkNwg+RN8LlaKi33/IDlIbPj7DL+34u8a" +
            "MQcvLg9AYRdBmauZFv2B3L8VkSaBZIDbr6ngXCCjf5BlC8p3kfTCTuAqkDzAtMS0otj3blFR0SDTcmAe" +
            "J1w84Rjht7i8iWsO6AjF/B9+6yBvCbdx5WthfnOh7BOzHmgNccSFuT8CBP1CdlTrUNZPOKLD/QcI97yY" +
            "LxeXXyXiKS7/HdsA/u7gbwL/fyYtjYTFNRPQr/3g52nmn/Up10Ueohs6q+EsO9LZkDnB88VIa4faY76Z" +
            "oMx2wiOc6//9oQyRiCU/uSu6YNoP5K9EkChtFVfIXz3D4h/eIkgp8iQJvqWhRbruxoYZc9IkeH60/fb7" +
            "ZKhENJ1ftocTfJxv2OWTLx8xoWRCoXwkMHHcxCOmTZ42AkokVnPtIJS4ZMJ5FP6mqQqFHMH/oVwnghTx" +
            "bTQTk8ZPGolw5/OvfCQwderUvMmVk0dMLpt8flFR0UEYfU/FSPZNc44PYszlavu0ymnnww/TSFgbSEbF" +
            "mIrTKsuQjm/SSOTlJPk4jtGjRw9mWlPKp5xnrgtwrg6SfAPhRphzVxnPCFou/M18oXM4nlMX+N2EuDeL" +
            "LTSkZQ1HlFxc8rXpE6afh07vZI6ql0+7fATrBeU5inXLOqY/xHekbAMN6eSyzOigaiBfMi0RmT32N+vT" +
            "N373XrfvLJ/KtqkcV3lW8hqKFXYEDygF0ZphZ0SbL6xIlAt80dChR8NdS/CfLAHFG2047YKO4SG1w88U" +
            "xz7t3MKHnxCNFHzd1i1TqRl2uihHPF+qHm08e0wHfw1njAIRC4W7tQzJwrhqj/5Gh/Csk/ARJybUyR45" +
            "B9+TgdF9KAjB6cNC+WiPADsbWDG0DqI0++XjHgOdxikcfSeVTtrBRUP5uFdgR/DYCxx8IeSwDhJUQIQ8" +
            "i187QXjuWduFZ7yxF0ps3PiyCUZSO7dMRZwNt7wAQjFX061id/zVVkSd8GWTxPCU2Msmu/06BM8QRUVF" +
            "bloFGME7zLf7E9zPpvlM0595lI97DMR1EDqNMWUX9355bQnuSI/EIbiDPQYOwbMvDsEd7DFwCJ592TMI" +
            "7nL9P5K4gFTpOi+HAAAAAElFTkSuQmCC";

        public static System.Windows.Controls.Image BrandTile(double size = 28)
        {
            var bytes = Convert.FromBase64String(LogoBase64);
            var ms = new System.IO.MemoryStream(bytes);
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            // Decode directly at the target height (x2 for high-DPI sharpness) so
            // WPF never has to downscale a full-resolution bitmap at render time.
            bmp.DecodePixelHeight = (int)(size * 2);
            bmp.EndInit();
            bmp.Freeze();

            var img = new System.Windows.Controls.Image
            {
                Source = bmp,
                Height = size,
                Width = double.NaN,
                Stretch = System.Windows.Media.Stretch.Uniform,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new System.Windows.Thickness(0, 0, 0, 0)
            };
            // Use bicubic interpolation instead of the default nearest-neighbour
            // to get a clean smooth result at small display sizes.
            System.Windows.Media.RenderOptions.SetBitmapScalingMode(
                img, System.Windows.Media.BitmapScalingMode.HighQuality);
            return img;
        }

        // ── Button factories ──────────────────────────────────────────────
        public static Button PrimaryButton(string label, double width = 0)
        {
            var b = new Button
            {
                Content = label,
                FontFamily = Ui,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Cursor = Cursors.Hand,
                Padding = new Thickness(16, 9, 16, 9),
                Template = PrimaryTemplate()
            };
            if (width > 0) b.Width = width;
            return b;
        }

        public static Button SecondaryButton(string label, double width = 0)
        {
            var b = new Button
            {
                Content = label,
                FontFamily = Ui,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush(Text),
                Cursor = Cursors.Hand,
                Padding = new Thickness(16, 9, 16, 9),
                Template = SecondaryTemplate()
            };
            if (width > 0) b.Width = width;
            return b;
        }

        public static Button GhostButton(string label, double width = 0)
        {
            var b = new Button
            {
                Content = label,
                FontFamily = Ui,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush(TextDim),
                Cursor = Cursors.Hand,
                Padding = new Thickness(14, 9, 14, 9),
                Template = GhostTemplate()
            };
            if (width > 0) b.Width = width;
            return b;
        }

        // ── Control templates ─────────────────────────────────────────────
        private static ControlTemplate PrimaryTemplate()
        {
            var t = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(WpfBorder));
            bd.Name = "bd";
            bd.SetValue(WpfBorder.CornerRadiusProperty, new CornerRadius(5));
            bd.SetValue(WpfBorder.BackgroundProperty, Brush(Accent));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(ContentPresenter.MarginProperty, new Thickness(16, 9, 16, 9));
            bd.AppendChild(cp);
            t.VisualTree = bd;
            var hover = new WpfTrigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new WpfSetter(WpfBorder.BackgroundProperty, Brush(AccentDeep), "bd"));
            t.Triggers.Add(hover);
            return t;
        }

        private static ControlTemplate SecondaryTemplate()
        {
            var t = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(WpfBorder));
            bd.Name = "bd";
            bd.SetValue(WpfBorder.CornerRadiusProperty, new CornerRadius(5));
            bd.SetValue(WpfBorder.BackgroundProperty, Brush(Surface));
            bd.SetValue(WpfBorder.BorderBrushProperty, Brush(BorderStrong));
            bd.SetValue(WpfBorder.BorderThicknessProperty, new Thickness(1));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(ContentPresenter.MarginProperty, new Thickness(16, 9, 16, 9));
            bd.AppendChild(cp);
            t.VisualTree = bd;
            var hover = new WpfTrigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new WpfSetter(WpfBorder.BackgroundProperty, Brush(Surface2), "bd"));
            t.Triggers.Add(hover);
            return t;
        }

        private static ControlTemplate GhostTemplate()
        {
            var t = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(WpfBorder));
            bd.Name = "bd";
            bd.SetValue(WpfBorder.CornerRadiusProperty, new CornerRadius(5));
            bd.SetValue(WpfBorder.BackgroundProperty, new SolidColorBrush(Colors.Transparent));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(ContentPresenter.MarginProperty, new Thickness(14, 9, 14, 9));
            bd.AppendChild(cp);
            t.VisualTree = bd;
            var hover = new WpfTrigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new WpfSetter(WpfBorder.BackgroundProperty, Brush(Surface2), "bd"));
            t.Triggers.Add(hover);
            return t;
        }

        // ── Picker button (replaces ComboBox — white, bordered, chevron) ──
        public static Button PickerButton(string currentValue)
        {
            var label = new TextBlock
            {
                Text = currentValue,
                FontFamily = Ui,
                FontSize = 13,
                Foreground = Brush(Text),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var btn = new Button
            {
                Content = label,
                Tag = label,
                Height = 38,
                Padding = new Thickness(10, 0, 10, 0),
                Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Template = PickerButtonTemplate()
            };
            return btn;
        }

        private static ControlTemplate PickerButtonTemplate()
        {
            var t = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(WpfBorder));
            bd.Name = "bd";
            bd.SetValue(WpfBorder.BackgroundProperty, Brushes.White);
            bd.SetValue(WpfBorder.BorderBrushProperty, Brush(BorderStrong));
            bd.SetValue(WpfBorder.BorderThicknessProperty, new Thickness(1));
            bd.SetValue(WpfBorder.CornerRadiusProperty, new CornerRadius(5));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.MarginProperty, new Thickness(10, 0, 10, 0));
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            bd.AppendChild(cp);
            t.VisualTree = bd;
            var hover = new WpfTrigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new WpfSetter(WpfBorder.BorderBrushProperty, Brush(Accent), "bd"));
            hover.Setters.Add(new WpfSetter(WpfBorder.BackgroundProperty, Brush(AccentTint), "bd"));
            t.Triggers.Add(hover);
            return t;
        }

        /// <summary>Updates the label text inside a PickerButton after selection.</summary>
        public static void SetPickerValue(Button btn, string value)
        {
            if (btn.Tag is TextBlock lbl) lbl.Text = value;
        }

        // ── Section label (uppercase + faint + bold) ──────────────────────
        public static TextBlock SectionLabel(string text) => new TextBlock
        {
            Text = text.ToUpperInvariant(),
            FontFamily = Ui,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = Brush(TextFaint),
            Margin = new Thickness(0, 0, 0, 10)
        };

        // ── Field label row (badge + label) ───────────────────────────────
        public static StackPanel FieldLabel(int n, string text)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };
            row.Children.Add(NumberBadge(n));
            row.Children.Add(new TextBlock
            {
                Text = text,
                FontFamily = Ui,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush(TextDim),
                VerticalAlignment = VerticalAlignment.Center
            });
            return row;
        }

        // ── Styled DataGrid ───────────────────────────────────────────────
        public static DataGrid StyledDataGrid()
        {
            var dg = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserSortColumns = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = Brush(Border),
                BorderBrush = Brush(Border),
                BorderThickness = new Thickness(1),
                Background = Brush(Surface),
                RowBackground = Brush(Surface),
                AlternatingRowBackground = Brush(Zebra),
                AlternationCount = 2,
                RowHeight = 34,
                FontFamily = Ui,
                FontSize = 12.5,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var headerStyle = new Style(typeof(DataGridColumnHeader));
            headerStyle.Setters.Add(new WpfSetter(DataGridColumnHeader.FontFamilyProperty, Ui));
            headerStyle.Setters.Add(new WpfSetter(DataGridColumnHeader.FontSizeProperty, 10.5));
            headerStyle.Setters.Add(new WpfSetter(DataGridColumnHeader.FontWeightProperty, FontWeights.Bold));
            headerStyle.Setters.Add(new WpfSetter(DataGridColumnHeader.ForegroundProperty, Brush(TextFaint)));
            headerStyle.Setters.Add(new WpfSetter(DataGridColumnHeader.BackgroundProperty, Brush(Surface2)));
            headerStyle.Setters.Add(new WpfSetter(DataGridColumnHeader.PaddingProperty, new Thickness(12, 9, 12, 9)));
            headerStyle.Setters.Add(new WpfSetter(DataGridColumnHeader.BorderBrushProperty, Brush(BorderStrong)));
            headerStyle.Setters.Add(new WpfSetter(DataGridColumnHeader.BorderThicknessProperty, new Thickness(0, 0, 0, 1.5)));
            dg.ColumnHeaderStyle = headerStyle;

            var cellStyle = new Style(typeof(DataGridCell));
            cellStyle.Setters.Add(new WpfSetter(DataGridCell.PaddingProperty, new Thickness(12, 7, 12, 7)));
            cellStyle.Setters.Add(new WpfSetter(DataGridCell.BorderThicknessProperty, new Thickness(0)));
            cellStyle.Setters.Add(new WpfSetter(DataGridCell.ForegroundProperty, Brush(Text)));
            dg.CellStyle = cellStyle;

            return dg;
        }

        // ── Text column helper ────────────────────────────────────────────
        public static DataGridTextColumn TextColumn(string header, string bindingPath, double width)
            => new DataGridTextColumn
            {
                Header = header,
                Binding = new WpfBinding(bindingPath),
                Width = new DataGridLength(width)
            };

        // ── Pill template column ──────────────────────────────────────────
        public static DataGridTemplateColumn PillColumn(string bindingPath, double width)
        {
            var col = new DataGridTemplateColumn
            {
                Header = "Status",
                Width = new DataGridLength(width)
            };

            var dt = new DataTemplate();
            var factory = new FrameworkElementFactory(typeof(ContentControl));
            factory.SetBinding(ContentControl.TagProperty, new WpfBinding(bindingPath));
            factory.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler((s, e) =>
            {
                if (s is ContentControl cc && cc.Tag is string glyph)
                    cc.Content = StatusPill(PillKind(glyph));
            }));
            dt.VisualTree = factory;
            col.CellTemplate = dt;
            return col;
        }

        // ── Footer strip ─────────────────────────────────────────────────
        public static WpfBorder FooterStrip(UIElement content)
            => new WpfBorder
            {
                Background = Brush(Surface2),
                BorderBrush = Brush(Border),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(20, 14, 20, 14),
                Child = content
            };

        // ── Segmented toggle (Instance / Type) ────────────────────────────
        public static (WpfBorder container, WpfToggleButton left, WpfToggleButton right)
            SegmentedToggle(string leftLabel, string rightLabel, bool leftSelected)
        {
            var leftBtn = MakeSegment(leftLabel, leftSelected);
            var rightBtn = MakeSegment(rightLabel, !leftSelected);

            leftBtn.Checked += (s, e) => rightBtn.IsChecked = false;
            rightBtn.Checked += (s, e) => leftBtn.IsChecked = false;

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(leftBtn);
            row.Children.Add(rightBtn);

            var container = new WpfBorder
            {
                BorderBrush = Brush(BorderStrong),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Background = Brush(Surface2),
                Padding = new Thickness(3),
                Child = row
            };
            return (container, leftBtn, rightBtn);
        }

        private static WpfToggleButton MakeSegment(string label, bool selected)
        {
            var tb = new WpfToggleButton
            {
                Content = label,
                IsChecked = selected,
                FontFamily = Ui,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                Padding = new Thickness(14, 7, 14, 7),
                Template = SegmentTemplate()
            };
            return tb;
        }

        private static ControlTemplate SegmentTemplate()
        {
            var t = new ControlTemplate(typeof(WpfToggleButton));
            var bd = new FrameworkElementFactory(typeof(WpfBorder));
            bd.Name = "bd";
            bd.SetValue(WpfBorder.CornerRadiusProperty, new CornerRadius(3));
            bd.SetValue(WpfBorder.BackgroundProperty, new SolidColorBrush(Colors.Transparent));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(ContentPresenter.MarginProperty, new Thickness(14, 7, 14, 7));
            bd.AppendChild(cp);
            t.VisualTree = bd;

            var checkedTrigger = new WpfTrigger { Property = WpfToggleButton.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new WpfSetter(WpfBorder.BackgroundProperty, Brush(Surface), "bd"));
            checkedTrigger.Setters.Add(new WpfSetter(WpfBorder.BorderBrushProperty, Brush(Border), "bd"));
            checkedTrigger.Setters.Add(new WpfSetter(WpfBorder.BorderThicknessProperty, new Thickness(1), "bd"));
            t.Triggers.Add(checkedTrigger);

            return t;
        }

        // ── Rounded progress bar ──────────────────────────────────────────
        public static ProgressBar RoundedProgressBar(int total)
        {
            var pb = new ProgressBar
            {
                Minimum = 0,
                Maximum = total,
                Height = 10,
                Template = ProgressTemplate()
            };
            return pb;
        }

        private static ControlTemplate ProgressTemplate()
        {
            var t = new ControlTemplate(typeof(ProgressBar));

            var track = new FrameworkElementFactory(typeof(WpfBorder));
            track.SetValue(WpfBorder.CornerRadiusProperty, new CornerRadius(999));
            track.SetValue(WpfBorder.BackgroundProperty, Brush(Border));
            track.SetValue(WpfBorder.ClipToBoundsProperty, true);

            var grid = new FrameworkElementFactory(typeof(WpfGrid));
            grid.Name = "PART_Track";

            var indicator = new FrameworkElementFactory(typeof(WpfBorder));
            indicator.Name = "PART_Indicator";
            indicator.SetValue(WpfBorder.CornerRadiusProperty, new CornerRadius(999));
            indicator.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);

            var gradient = new LinearGradientBrush
            {
                StartPoint = new WpfPoint(0, 0),
                EndPoint = new WpfPoint(1, 0)
            };
            gradient.GradientStops.Add(new GradientStop(Hex(Accent), 0.0));
            gradient.GradientStops.Add(new GradientStop(Hex("#FF9E7A"), 1.0));
            indicator.SetValue(WpfBorder.BackgroundProperty, gradient);

            grid.AppendChild(indicator);
            track.AppendChild(grid);
            t.VisualTree = track;
            return t;
        }

        // ── Separator ─────────────────────────────────────────────────────
        public static WpfBorder HairlineSeparator() => new WpfBorder
        {
            Height = 1,
            Background = Brush(Border),
            Margin = new Thickness(0, 16, 0, 16)
        };

        // ── Config panel key/value row ─────────────────────────────────────
        public static WpfBorder ConfigRow(string key, string value, bool lastRow = false)
        {
            var grid = new WpfGrid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var k = new TextBlock
            {
                Text = key,
                FontFamily = Ui,
                FontSize = 11.5,
                Foreground = Brush(TextDim),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            var v = new TextBlock
            {
                Text = value,
                FontFamily = Mono,
                FontSize = 11.5,
                Foreground = Brush(Text),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            WpfGrid.SetColumn(k, 0);
            WpfGrid.SetColumn(v, 1);
            grid.Children.Add(k);
            grid.Children.Add(v);

            return new WpfBorder
            {
                Padding = new Thickness(0, 9, 0, 9),
                BorderBrush = lastRow ? Brushes.Transparent : Brush(Border),
                BorderThickness = new Thickness(0, 0, 0, lastRow ? 0 : 1),
                Child = grid
            };
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  AddInConstants
    // ══════════════════════════════════════════════════════════════════════

    internal static class AddInConstants
    {
        internal const string SharedParameterFilePath =
            @"C:\Users\anghi\Desktop\Noruegos 2.0\07-JUL 15 Familias Hilti\MEKANOS.txt";

        internal const int FamilyScanCap = 16;
        internal const string SelectionMode = "Use Current Selection";

        internal static readonly IReadOnlyList<string> ModeChoices = new[]
        {
            SelectionMode, "Generic Models", "Furniture",
            "Mechanical Equipment", "Pipe Fittings", "Duct Fittings", "Specialty Equipment"
        };

        internal static readonly IReadOnlyList<string> RevitGroupChoices = new[]
        {
            "Identity Data", "Text", "Dimensions", "Graphics",
            "Constraints", "Data", "Analysis Results", "General"
        };

        internal static readonly IReadOnlyDictionary<string, ForgeTypeId> GroupForgeIds =
            new Dictionary<string, ForgeTypeId>
            {
                { "Identity Data",    GroupTypeId.IdentityData    },
                { "Text",             GroupTypeId.Text            },
                { "Dimensions",       GroupTypeId.Geometry        },
                { "Graphics",         GroupTypeId.Graphics        },
                { "Constraints",      GroupTypeId.Constraints     },
                { "Data",             GroupTypeId.Data            },
                { "Analysis Results", GroupTypeId.AnalysisResults },
                { "General",          GroupTypeId.General         },
            };
    }

    // ══════════════════════════════════════════════════════════════════════
    //  FamilyResolver
    // ══════════════════════════════════════════════════════════════════════

    internal static class FamilyResolver
    {
        internal static List<Family> Resolve(string mode, UIDocument uiDoc, Document doc)
        {
            if (mode == AddInConstants.SelectionMode)
                return ResolveFromSelection(uiDoc, doc);

            return new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => f.FamilyCategory?.Name == mode)
                .ToList();
        }

        private static List<Family> ResolveFromSelection(UIDocument uiDoc, Document doc)
        {
            var result = new List<Family>();
            var seenIds = new HashSet<ElementId>();
            foreach (ElementId id in uiDoc.Selection.GetElementIds())
            {
                Family family = ExtractFamily(doc.GetElement(id));
                if (family != null && seenIds.Add(family.Id))
                    result.Add(family);
            }
            return result;
        }

        private static Family ExtractFamily(Element element)
        {
            if (element is Family f) return f;
            if (element is FamilyInstance fi) return fi.Symbol?.Family;
            if (element is FamilySymbol fs) return fs.Family;
            return null;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  DefaultFamilyLoadOptions
    // ══════════════════════════════════════════════════════════════════════

    internal class DefaultFamilyLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        { overwriteParameterValues = true; return true; }

        public bool OnSharedFamilyFound(
            Family sharedFamily, bool familyInUse,
            out FamilySource source, out bool overwriteParameterValues)
        { source = FamilySource.Family; overwriteParameterValues = true; return true; }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  AvesBaseWindow
    // ══════════════════════════════════════════════════════════════════════

    internal class AvesBaseWindow : Window
    {
        protected readonly StackPanel BodyPanel;
        private readonly WpfGrid _rootGrid;

        protected AvesBaseWindow(string windowTitle)
        {
            FontFamily = Av.Ui;
            Background = Av.Brush(Av.Surface);
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            _rootGrid = new WpfGrid();
            _rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ── Header strip ─────────────────────────────────────────────
            var headerGrid = new WpfGrid { Height = 52 };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var brand = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0)
            };
            var tile = Av.BrandTile(20);
            tile.Margin = new Thickness(0, 0, 10, 0);
            brand.Children.Add(tile);
            brand.Children.Add(new TextBlock
            {
                Text = windowTitle,
                FontFamily = Av.Ui,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Av.Brush(Av.Text),
                VerticalAlignment = VerticalAlignment.Center
            });
            WpfGrid.SetColumn(brand, 0);
            headerGrid.Children.Add(brand);

            var header = new WpfBorder
            {
                Background = Av.Brush(Av.Surface),
                BorderBrush = Av.Brush(Av.Border),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = headerGrid
            };
            WpfGrid.SetRow(header, 0);
            _rootGrid.Children.Add(header);

            // ── Body scroll area ──────────────────────────────────────────
            BodyPanel = new StackPanel { Margin = new Thickness(20) };
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = BodyPanel
            };
            WpfGrid.SetRow(scroll, 1);
            _rootGrid.Children.Add(scroll);

            base.Content = _rootGrid;
        }

        protected void SetFooter(UIElement footerContent)
        {
            var footer = Av.FooterStrip(footerContent);
            WpfGrid.SetRow(footer, 2);
            _rootGrid.Children.Add(footer);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  SinglePickerWindow
    //  FIX: Added PassWheelToSelf handler on the ListBox so the outer
    //  ScrollViewer in AvesBaseWindow no longer intercepts mouse wheel events.
    // ══════════════════════════════════════════════════════════════════════

    internal class SinglePickerWindow : AvesBaseWindow
    {
        private readonly ListBox _listBox;
        public string SelectedValue { get; private set; }

        public static string Show(
            string title, IEnumerable<string> items, string current, Window owner)
        {
            var w = new SinglePickerWindow(title, items, current) { Owner = owner };
            return w.ShowDialog() == true ? w.SelectedValue : null;
        }

        private SinglePickerWindow(string title, IEnumerable<string> items, string current)
            : base(title)
        {
            Width = 360; Height = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            _listBox = new ListBox
            {
                // FIX: Give the ListBox a fixed height so it owns its own
                // scroll region inside the outer BodyPanel ScrollViewer.
                Height = 240,
                Margin = new Thickness(0, 0, 0, 0),
                FontFamily = Av.Ui,
                FontSize = 13,
                Background = Av.Brush(Av.Surface),
                BorderBrush = Av.Brush(Av.Border),
                BorderThickness = new Thickness(1)
            };
            foreach (string item in items) _listBox.Items.Add(item);
            if (current != null) _listBox.SelectedItem = current;
            _listBox.MouseDoubleClick += (_, __) => Confirm();

            // FIX: Intercept the bubbling MouseWheel event and re-fire it
            // directly on the ListBox so it scrolls instead of the outer ScrollViewer.
            _listBox.PreviewMouseWheel += PassWheelToSelf;

            BodyPanel.Children.Add(_listBox);

            // Footer
            var cancelBtn = Av.GhostButton("Cancel");
            cancelBtn.Click += (_, __) => { DialogResult = false; };
            var okBtn = Av.PrimaryButton("OK");
            okBtn.Click += (_, __) => Confirm();

            var footerRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            footerRow.Children.Add(cancelBtn);
            footerRow.Children.Add(new WpfBorder { Width = 10 });
            footerRow.Children.Add(okBtn);
            SetFooter(footerRow);
        }

        private void Confirm()
        {
            if (_listBox.SelectedItem == null) return;
            SelectedValue = (string)_listBox.SelectedItem;
            DialogResult = true;
        }

        // FIX: Marks the bubbling event as handled, then re-raises it as a
        // direct event on the ListBox itself so the ListBox scrolls normally.
        private static void PassWheelToSelf(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled) return;
            e.Handled = true;
            var args = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            };
            ((UIElement)sender).RaiseEvent(args);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  MultiPickerWindow
    //  FIX: Added PassWheelToSelf handler on the inner ScrollViewer so the
    //  outer ScrollViewer in AvesBaseWindow no longer intercepts mouse wheel events.
    // ══════════════════════════════════════════════════════════════════════

    internal class MultiPickerWindow : AvesBaseWindow
    {
        private readonly List<CheckBox> _checkBoxes = new List<CheckBox>();
        private readonly TextBlock _selectionCount;
        private List<string> _allItems;

        public List<string> SelectedValues { get; private set; }

        public static List<string> Show(
            string title, IEnumerable<string> items, IEnumerable<string> current, Window owner)
        {
            var w = new MultiPickerWindow(title, items, current) { Owner = owner };
            return w.ShowDialog() == true ? w.SelectedValues : null;
        }

        private MultiPickerWindow(
            string title, IEnumerable<string> items, IEnumerable<string> current)
            : base(title)
        {
            Width = 420; Height = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;

            _allItems = items.ToList();
            var currentSet = current != null
                ? new HashSet<string>(current)
                : new HashSet<string>();

            // ── Search box ────────────────────────────────────────────────
            var searchRow = new WpfBorder
            {
                Background = Av.Brush(Av.Surface2),
                BorderBrush = Av.Brush(Av.BorderStrong),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 9, 10, 9),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var searchGrid = new WpfGrid();
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var magnifier = new TextBlock
            {
                Text = "⌕",
                FontSize = 14,
                Foreground = Av.Brush(Av.TextFaint),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var searchBox = new WpfTextBox
            {
                FontFamily = Av.Ui,
                FontSize = 13,
                Foreground = Av.Brush(Av.Text),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            };
            searchBox.GotFocus += (_, __) => { };
            searchBox.TextChanged += (s, e) => FilterItems(searchBox.Text);

            WpfGrid.SetColumn(magnifier, 0);
            WpfGrid.SetColumn(searchBox, 1);
            searchGrid.Children.Add(magnifier);
            searchGrid.Children.Add(searchBox);
            searchRow.Child = searchGrid;
            BodyPanel.Children.Add(searchRow);

            // ── Checkbox list in a bordered scroll ─────────────────────────
            var itemStack = new StackPanel();
            foreach (string item in _allItems)
            {
                var cb = MakeCheckRow(item, currentSet.Contains(item));
                cb.Checked += (_, __) => UpdateCount();
                cb.Unchecked += (_, __) => UpdateCount();
                _checkBoxes.Add(cb);
                itemStack.Children.Add(cb);
            }

            // FIX: Extract the inner ScrollViewer so we can attach the
            // wheel handler to it before wrapping it in the border.
            var innerScroll = new ScrollViewer
            {
                // FIX: Fixed height ensures this ScrollViewer owns the scroll
                // region rather than deferring to the outer AvesBaseWindow ScrollViewer.
                Height = 300,
                Content = itemStack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            // FIX: Intercept the bubbling MouseWheel event and re-fire it
            // directly on the inner ScrollViewer so it scrolls instead of the outer one.
            innerScroll.PreviewMouseWheel += PassWheelToSelf;

            var listBorder = new WpfBorder
            {
                BorderBrush = Av.Brush(Av.Border),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                ClipToBounds = true,
                Child = innerScroll
            };
            BodyPanel.Children.Add(listBorder);

            // ── Footer ─────────────────────────────────────────────────────
            _selectionCount = new TextBlock
            {
                FontFamily = Av.Ui,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Av.Brush(Av.TextDim),
                VerticalAlignment = VerticalAlignment.Center
            };
            UpdateCount();

            var cancelBtn = Av.GhostButton("Cancel");
            cancelBtn.Click += (_, __) => { DialogResult = false; };
            var addBtn = Av.PrimaryButton("Add parameters");
            addBtn.Click += (_, __) =>
            {
                SelectedValues = _checkBoxes
                    .Where(c => c.IsChecked == true && c.IsVisible)
                    .Select(c => (string)c.Tag)
                    .ToList();
                DialogResult = true;
            };

            var footerRow = new WpfGrid();
            footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            WpfGrid.SetColumn(_selectionCount, 0);
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
            btnRow.Children.Add(cancelBtn);
            btnRow.Children.Add(new WpfBorder { Width = 10 });
            btnRow.Children.Add(addBtn);
            WpfGrid.SetColumn(btnRow, 1);

            footerRow.Children.Add(_selectionCount);
            footerRow.Children.Add(btnRow);
            SetFooter(footerRow);
        }

        private CheckBox MakeCheckRow(string item, bool isChecked)
        {
            var cb = new CheckBox
            {
                Tag = item,
                IsChecked = isChecked,
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = new TextBlock
                {
                    Text = item,
                    FontFamily = Av.Mono,
                    FontSize = 12,
                    Foreground = Av.Brush(Av.Text),
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            return cb;
        }

        private void FilterItems(string query)
        {
            foreach (var cb in _checkBoxes)
            {
                string name = (string)cb.Tag;
                cb.Visibility = string.IsNullOrEmpty(query) ||
                                name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    ? WpfVisibility.Visible
                    : WpfVisibility.Collapsed;
            }
            UpdateCount();
        }

        private void UpdateCount()
        {
            int n = _checkBoxes.Count(c => c.IsChecked == true);
            _selectionCount.Text = $"{n} selected";
        }

        // FIX: Marks the bubbling event as handled, then re-raises it as a
        // direct event on the inner ScrollViewer so it scrolls normally.
        private static void PassWheelToSelf(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled) return;
            e.Handled = true;
            var args = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            };
            ((UIElement)sender).RaiseEvent(args);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  BatchProgressWindow
    // ══════════════════════════════════════════════════════════════════════

    internal class BatchProgressWindow : Window
    {
        private readonly ProgressBar _progressBar;
        private readonly TextBlock _familyLabel;
        private readonly TextBlock _countLabel;
        private readonly TextBlock _captionLabel;
        private int _total;

        public bool IsCancelled { get; private set; }

        public BatchProgressWindow(string windowTitle, int total)
        {
            _total = total;
            Width = 480;
            Height = 220;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            FontFamily = Av.Ui;
            Background = Brushes.White;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            var root = new WpfGrid { Background = Brushes.White };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var brand = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0)
            };
            var tile = Av.BrandTile(20);
            tile.Margin = new Thickness(0, 0, 10, 0);
            brand.Children.Add(tile);
            brand.Children.Add(new TextBlock
            {
                Text = windowTitle,
                FontFamily = Av.Ui,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Av.Brush(Av.Text),
                VerticalAlignment = VerticalAlignment.Center
            });
            var header = new WpfBorder
            {
                Height = 52,
                Background = Av.Brush(Av.Surface),
                BorderBrush = Av.Brush(Av.Border),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = brand
            };
            WpfGrid.SetRow(header, 0);
            root.Children.Add(header);

            var body = new StackPanel { Margin = new Thickness(20, 18, 20, 20) };

            var statusGrid = new WpfGrid { Margin = new Thickness(0, 0, 0, 12) };
            statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _familyLabel = new TextBlock
            {
                Text = "Starting…",
                FontFamily = Av.Ui,
                FontSize = 13,
                Foreground = Av.Brush(Av.Text),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            WpfGrid.SetColumn(_familyLabel, 0);

            _countLabel = new TextBlock
            {
                Text = $"0 / {total}",
                FontFamily = Av.Mono,
                FontSize = 12,
                Foreground = Av.Brush(Av.TextDim),
                VerticalAlignment = VerticalAlignment.Center
            };
            WpfGrid.SetColumn(_countLabel, 1);

            statusGrid.Children.Add(_familyLabel);
            statusGrid.Children.Add(_countLabel);
            body.Children.Add(statusGrid);

            _progressBar = Av.RoundedProgressBar(total);
            _progressBar.Margin = new Thickness(0, 0, 0, 10);
            body.Children.Add(_progressBar);

            _captionLabel = new TextBlock
            {
                FontFamily = Av.Ui,
                FontSize = 11.5,
                Foreground = Av.Brush(Av.TextFaint)
            };
            body.Children.Add(_captionLabel);

            WpfGrid.SetRow(body, 1);
            root.Children.Add(body);

            Content = root;
        }

        public void Update(int current, int total, string status)
        {
            _total = total;
            _familyLabel.Text = status;
            _countLabel.Text = $"{current} / {total}";
            _progressBar.Maximum = total;
            _progressBar.Value = current;

            int remaining = total - current;
            _captionLabel.Text = remaining > 0
                ? $"{remaining} {(remaining == 1 ? "family" : "families")} remaining"
                : "Finishing up…";

            Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.Render);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Shared report row types
    // ══════════════════════════════════════════════════════════════════════

    public class ReportSummaryRow { public string Setting { get; set; } public string Value { get; set; } }
    public class ReportWarningRow { public string Item { get; set; } public string Reason { get; set; } }

    public class AddParamReportRow
    {
        public string FamilyName { get; set; }
        public string Status { get; set; }
        public string Added { get; set; }
        public string AlreadyExisted { get; set; }
        public string AddedParameters { get; set; }
        public string Notes { get; set; }
    }

    public class RemoveParamReportRow
    {
        public string FamilyName { get; set; }
        public string Status { get; set; }
        public string Removed { get; set; }
        public string NotFound { get; set; }
        public string RemovedParameters { get; set; }
        public string Notes { get; set; }
    }

    public class CsvItemReportRow
    {
        public string ItemNumber { get; set; }
        public string Status { get; set; }
        public string SetCount { get; set; }
        public string FailedCount { get; set; }
        public string SkippedCount { get; set; }
        public string SetParameters { get; set; }
        public string FailedParameters { get; set; }
        public string SkippedParameters { get; set; }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  UnifiedReportWindow
    // ══════════════════════════════════════════════════════════════════════

    internal class UnifiedReportWindow : AvesBaseWindow
    {
        private readonly string _clipboardText;

        public UnifiedReportWindow(
            string title,
            IReadOnlyList<(string setting, string value)> summary,
            IReadOnlyList<(string header, double width, string bindingPath)> resultColumns,
            IEnumerable<object> resultRows,
            IReadOnlyList<(string item, string reason)> warnings = null,
            string footerText = null)
            : base(title)
        {
            Width = 900; Height = 620;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var resultList = resultRows.ToList();

            BodyPanel.Children.Add(Av.SectionLabel("Run Summary"));

            var summaryGrid = new WpfGrid { Margin = new Thickness(0, 0, 0, 20) };
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });

            var tilesPanel = BuildStatTiles(summary);
            WpfGrid.SetColumn(tilesPanel, 0);
            summaryGrid.Children.Add(tilesPanel);

            var configPanel = BuildConfigPanel(summary);
            WpfGrid.SetColumn(configPanel, 1);
            summaryGrid.Children.Add(configPanel);

            BodyPanel.Children.Add(summaryGrid);

            var sectionRow = new WpfGrid { Margin = new Thickness(0, 0, 0, 8) };
            sectionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sectionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var sectionLbl = Av.SectionLabel("Results per Family");
            sectionLbl.Margin = new Thickness(0);
            WpfGrid.SetColumn(sectionLbl, 0);
            sectionRow.Children.Add(sectionLbl);

            var countLbl = new TextBlock
            {
                Text = $"showing {resultList.Count} {(resultList.Count == 1 ? "family" : "families")}",
                FontFamily = Av.Ui,
                FontSize = 11.5,
                Foreground = Av.Brush(Av.TextFaint),
                VerticalAlignment = VerticalAlignment.Center
            };
            WpfGrid.SetColumn(countLbl, 1);
            sectionRow.Children.Add(countLbl);
            BodyPanel.Children.Add(sectionRow);

            var resultGrid = Av.StyledDataGrid();
            foreach (var (header, width, bindingPath) in resultColumns)
            {
                if (bindingPath == "Status")
                    resultGrid.Columns.Add(Av.PillColumn("Status", width));
                else
                    resultGrid.Columns.Add(Av.TextColumn(header, bindingPath, width));
            }
            resultGrid.ItemsSource = resultList;
            resultGrid.Margin = new Thickness(0, 0, 0, 16);
            BodyPanel.Children.Add(resultGrid);

            if (warnings != null && warnings.Count > 0)
            {
                BodyPanel.Children.Add(Av.SectionLabel("Warnings"));
                var warnGrid = Av.StyledDataGrid();
                warnGrid.Columns.Add(Av.TextColumn("Item", "Item", 240));
                warnGrid.Columns.Add(Av.TextColumn("Reason", "Reason", 500));
                warnGrid.ItemsSource = warnings
                    .Select(w => new ReportWarningRow { Item = w.item, Reason = w.reason })
                    .ToList();
                warnGrid.Margin = new Thickness(0, 0, 0, 16);
                BodyPanel.Children.Add(warnGrid);
            }

            _clipboardText = BuildClipboardText(title, summary, resultColumns, resultList, warnings);

            var sourceCaption = new TextBlock
            {
                Text = !string.IsNullOrEmpty(footerText)
                    ? $"Source · {System.IO.Path.GetFileName(footerText)}"
                    : string.Empty,
                FontFamily = Av.Ui,
                FontSize = 11,
                Foreground = Av.Brush(Av.TextFaint),
                VerticalAlignment = VerticalAlignment.Center
            };

            var copyBtn = Av.SecondaryButton("⧉  Copy to clipboard");
            copyBtn.Click += (_, __) =>
            {
                Clipboard.SetText(_clipboardText);
                copyBtn.Content = "✓  Copied!";
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (s, e) => { copyBtn.Content = "⧉  Copy to clipboard"; timer.Stop(); };
                timer.Start();
            };

            var closeBtn = Av.PrimaryButton("Close");
            closeBtn.Click += (_, __) => Close();

            var footerGrid = new WpfGrid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            WpfGrid.SetColumn(sourceCaption, 0);
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
            btnRow.Children.Add(copyBtn);
            btnRow.Children.Add(new WpfBorder { Width = 10 });
            btnRow.Children.Add(closeBtn);
            WpfGrid.SetColumn(btnRow, 1);

            footerGrid.Children.Add(sourceCaption);
            footerGrid.Children.Add(btnRow);
            SetFooter(footerGrid);
        }

        private static UniformGrid BuildStatTiles(IReadOnlyList<(string setting, string value)> summary)
        {
            var grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 12, 0) };

            foreach (var (setting, value) in summary)
            {
                string accent = "";
                if (setting.Contains("✅") || setting.ToLower().Contains("processed")) accent = "ok";
                else if (setting.Contains("❌") || setting.ToLower().Contains("failed")) accent = "fail";
                else if (setting.Contains("⚠") || setting.ToLower().Contains("mismatch")) accent = "warn";
                else if (setting.ToLower().Contains("added") && !setting.ToLower().Contains("already")) accent = "accent";

                bool looksNumeric = int.TryParse(value, out _);
                if (!looksNumeric) continue;

                string label = setting.Replace("✅", "").Replace("⏭", "").Replace("❌", "").Replace("⚠", "").Trim();
                var tile = Av.StatTile(value, label, accent);
                tile.Margin = new Thickness(0, 0, 8, 8);
                grid.Children.Add(tile);
            }

            return grid;
        }

        private static WpfBorder BuildConfigPanel(IReadOnlyList<(string setting, string value)> summary)
        {
            var configRows = summary
                .Where(r => !int.TryParse(r.value, out _))
                .ToList();

            var stack = new StackPanel();
            for (int i = 0; i < configRows.Count; i++)
            {
                bool last = i == configRows.Count - 1;
                string key = configRows[i].setting;
                string value = configRows[i].value;
                stack.Children.Add(Av.ConfigRow(key, value, last));
            }

            return new WpfBorder
            {
                Background = Av.Brush(Av.Surface2),
                BorderBrush = Av.Brush(Av.Border),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(14, 6, 14, 6),
                Child = stack,
                VerticalAlignment = VerticalAlignment.Top
            };
        }

        private static string BuildClipboardText(
            string title,
            IReadOnlyList<(string setting, string value)> summary,
            IReadOnlyList<(string header, double width, string bindingPath)> resultColumns,
            List<object> rows,
            IReadOnlyList<(string item, string reason)> warnings)
        {
            var sb = new StringBuilder();
            sb.AppendLine(title);
            sb.AppendLine(new string('=', title.Length));
            sb.AppendLine();

            sb.AppendLine("RUN SUMMARY");
            sb.AppendLine("Setting\tValue");
            foreach (var (setting, value) in summary)
                sb.AppendLine($"{setting}\t{value}");
            sb.AppendLine();

            sb.AppendLine("RESULTS");
            sb.AppendLine(string.Join("\t", resultColumns.Select(c => c.header)));
            foreach (object row in rows)
            {
                Type rowType = row.GetType();
                var cells = resultColumns.Select(col =>
                {
                    PropertyInfo prop = rowType.GetProperty(col.bindingPath);
                    return prop?.GetValue(row)?.ToString() ?? string.Empty;
                });
                sb.AppendLine(string.Join("\t", cells));
            }

            if (warnings != null && warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("WARNINGS");
                sb.AppendLine("Item\tReason");
                foreach (var (item, reason) in warnings)
                    sb.AppendLine($"{item}\t{reason}");
            }

            return sb.ToString();
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  SharedParamFileManager
    //  Per-user remembered path to a shared parameters .txt file that each
    //  user downloads manually from the company Google Drive. No network
    //  calls — this only manages a local file picker + a remembered path
    //  cached in %AppData%\AVES\shared_param_path.txt.
    // ══════════════════════════════════════════════════════════════════════

    internal static class SharedParamFileManager
    {
        private static readonly string ConfigDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AVES");

        private static readonly string ConfigFile =
            System.IO.Path.Combine(ConfigDir, "shared_param_path.txt");

        /// <summary>
        /// Returns the remembered path if it exists and is still valid on disk;
        /// otherwise returns null.
        /// </summary>
        internal static string TryGetSavedPath()
        {
            try
            {
                if (!System.IO.File.Exists(ConfigFile)) return null;
                string saved = System.IO.File.ReadAllText(ConfigFile).Trim();
                return (!string.IsNullOrEmpty(saved) && System.IO.File.Exists(saved)) ? saved : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Persists the given path so it's remembered for this Windows user next time.
        /// </summary>
        internal static void SavePath(string path)
        {
            try
            {
                System.IO.Directory.CreateDirectory(ConfigDir);
                System.IO.File.WriteAllText(ConfigFile, path ?? string.Empty);
            }
            catch { /* Non-fatal — worst case the user is asked to pick again next run. */ }
        }

        /// <summary>
        /// Shows an OpenFileDialog for the user to select a shared parameters .txt file
        /// (typically one they've manually downloaded from the company Google Drive folder).
        /// Returns the chosen path, or null if the user cancelled.
        /// </summary>
        internal static string PromptForFile(Window owner = null)
        {
            var openDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Shared Parameters File (downloaded from Google Drive)",
                Filter = "Shared Parameter Files (*.txt)|*.txt|All Files (*.*)|*.*",
                DefaultExt = ".txt"
            };
            bool? result = owner != null ? openDialog.ShowDialog(owner) : openDialog.ShowDialog();
            return result == true ? openDialog.FileName : null;
        }

        /// <summary>
        /// Resolves the shared parameters file path for the current run: uses the
        /// remembered path if still valid, otherwise prompts the user and saves the choice.
        /// Holding Shift while launching the command forces the picker even if a
        /// saved path already exists, so users can re-point the add-in after
        /// re-downloading an updated file from Google Drive.
        /// </summary>
        internal static string Resolve(Window owner = null)
        {
            bool forceRePick = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

            if (!forceRePick)
            {
                string saved = TryGetSavedPath();
                if (saved != null) return saved;
            }

            string picked = PromptForFile(owner);
            if (picked == null) return null;

            SavePath(picked);
            return picked;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Cmd_BatchSharedParams
    // ══════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    public class Cmd_BatchSharedParams : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;
            var doc = uiDoc.Document;
            var app = uiApp.Application;

            string sharedParamPath = SharedParamFileManager.Resolve();
            if (sharedParamPath == null) return Result.Cancelled;

            app.SharedParametersFilename = sharedParamPath;
            DefinitionFile sharedParamFile = app.OpenSharedParameterFile();

            if (sharedParamFile == null)
            {
                TaskDialog.Show("Error",
                    $"Could not open the shared parameter file.\n\nPath: {sharedParamPath}");
                return Result.Cancelled;
            }

            var groupsByName = new Dictionary<string, DefinitionGroup>();
            foreach (DefinitionGroup group in sharedParamFile.Groups)
                groupsByName[group.Name] = group;

            var sortedGroupNames = groupsByName.Keys.OrderBy(n => n).ToList();

            if (sortedGroupNames.Count == 0)
            {
                TaskDialog.Show("Error", "The shared parameter file contains no groups.");
                return Result.Cancelled;
            }

            string defaultMode = uiDoc.Selection.GetElementIds().Count > 0
                ? AddInConstants.SelectionMode : "Generic Models";

            var settings = new SharedParamSettings
            {
                GroupName = sortedGroupNames[0],
                SelectedParams = new List<string>(),
                RevitGroupName = AddInConstants.RevitGroupChoices[0],
                Mode = defaultMode,
                IsInstance = true
            };

            while (true)
            {
                var win = new SharedParamSettingsWindow(settings, sortedGroupNames, groupsByName);
                if (win.ShowDialog() != true) return Result.Cancelled;
                if (settings.SelectedParams.Count > 0) break;
                TaskDialog.Show("Validation", "You must select at least one parameter before running.");
            }

            ForgeTypeId targetGroup = ResolveGroupForgeTypeId(settings.RevitGroupName);

            var definitionsToAdd = groupsByName[settings.GroupName].Definitions
                .Cast<Definition>().OfType<ExternalDefinition>()
                .Where(d => settings.SelectedParams.Contains(d.Name)).ToList();

            var familiesToProcess = FamilyResolver.Resolve(settings.Mode, uiDoc, doc);
            if (familiesToProcess.Count == 0)
            {
                TaskDialog.Show("No Families Found",
                    $"No families matched the selected mode / category:\n\"{settings.Mode}\"");
                return Result.Cancelled;
            }

            var familyResults = new List<AddParamReportRow>();
            int processedCount = 0, skippedCount = 0, failedCount = 0;
            int totalAdded = 0, totalAlreadyHad = 0, totalGroupMismatches = 0;

            var familyIds = familiesToProcess.Select(f => f.Id).ToList();
            var familyNames = familiesToProcess.Select(f => f.Name).ToList();
            int familyCount = familyIds.Count;

            var loadOptions = new DefaultFamilyLoadOptions();
            var progressWindow = new BatchProgressWindow("Processing Families…", familyCount);
            progressWindow.Show();

            for (int i = 0; i < familyCount; i++)
            {
                if (progressWindow.IsCancelled) break;

                string familyName = familyNames[i];
                progressWindow.Update(i + 1, familyCount, $"Processing: {familyName}");

                Family family = doc.GetElement(familyIds[i]) as Family;
                if (family == null)
                {
                    failedCount++;
                    familyResults.Add(new AddParamReportRow
                    {
                        FamilyName = familyName,
                        Status = "❌",
                        Added = "—",
                        AlreadyExisted = "—",
                        AddedParameters = "—",
                        Notes = "Family element is no longer accessible."
                    });
                    continue;
                }

                if (!family.IsEditable)
                {
                    skippedCount++;
                    familyResults.Add(new AddParamReportRow
                    {
                        FamilyName = familyName,
                        Status = "⏭",
                        Added = "0",
                        AlreadyExisted = "—",
                        AddedParameters = "—",
                        Notes = "System / Non-Editable"
                    });
                    continue;
                }

                Document familyDocument = null;
                try { familyDocument = doc.EditFamily(family); }
                catch (Exception ex)
                {
                    failedCount++;
                    familyResults.Add(new AddParamReportRow
                    {
                        FamilyName = familyName,
                        Status = "❌",
                        Added = "—",
                        AlreadyExisted = "—",
                        AddedParameters = "—",
                        Notes = $"Cannot open for editing — {ex.Message}"
                    });
                    continue;
                }

                app.SharedParametersFilename = sharedParamPath;
                DefinitionFile refreshedSpFile = app.OpenSharedParameterFile();
                List<ExternalDefinition> currentDefinitions = definitionsToAdd;

                if (refreshedSpFile != null)
                {
                    DefinitionGroup refreshedGroup = null;
                    foreach (DefinitionGroup g in refreshedSpFile.Groups)
                        if (g.Name == settings.GroupName) { refreshedGroup = g; break; }

                    if (refreshedGroup != null)
                    {
                        var freshDefs = refreshedGroup.Definitions
                            .Cast<Definition>().OfType<ExternalDefinition>()
                            .Where(d => settings.SelectedParams.Contains(d.Name)).ToList();
                        if (freshDefs.Count > 0) currentDefinitions = freshDefs;
                    }
                }

                try
                {
                    FamilyManager familyManager = familyDocument.FamilyManager;
                    var existingNames = new HashSet<string>(
                        familyManager.Parameters.Cast<FamilyParameter>().Select(p => p.Definition.Name));

                    var toAdd = currentDefinitions.Where(d => !existingNames.Contains(d.Name)).ToList();
                    var toSkip = currentDefinitions.Where(d => existingNames.Contains(d.Name))
                                                   .Select(d => d.Name).ToList();
                    List<string> groupMismatches = new List<string>();

                    if (toAdd.Count > 0)
                    {
                        using (var transaction = new Transaction(familyDocument, "Add Shared Parameters"))
                        {
                            transaction.Start();
                            foreach (ExternalDefinition definition in toAdd)
                                familyManager.AddParameter(definition, targetGroup, settings.IsInstance);
                            transaction.Commit();
                        }

                        familyDocument.LoadFamily(doc, loadOptions);
                        FamilyNameGuard.RestoreIfTruncated(doc, familyIds[i], familyName);

                        groupMismatches = DetectGroupCacheMismatches(
                            doc, familyIds[i], toAdd, targetGroup, settings.IsInstance);

                        totalAdded += toAdd.Count;
                        totalGroupMismatches += groupMismatches.Count;
                        processedCount++;
                    }
                    else
                    {
                        skippedCount++;
                    }

                    totalAlreadyHad += toSkip.Count;

                    string statusIcon = toAdd.Count > 0
                        ? (groupMismatches.Count > 0 ? "⚠" : "✅") : "⏭";
                    string notes = groupMismatches.Count == 0 ? string.Empty
                        : "Project cache forced different group: " + string.Join("; ", groupMismatches);

                    familyResults.Add(new AddParamReportRow
                    {
                        FamilyName = familyName,
                        Status = statusIcon,
                        Added = toAdd.Count.ToString(),
                        AlreadyExisted = toSkip.Count.ToString(),
                        AddedParameters = toAdd.Count > 0 ? string.Join(", ", toAdd.Select(d => d.Name)) : "—",
                        Notes = notes
                    });
                    familyDocument.Close(false);
                }
                catch (Exception ex)
                {
                    SafeClose(familyDocument);
                    failedCount++;
                    familyResults.Add(new AddParamReportRow
                    {
                        FamilyName = familyName,
                        Status = "❌",
                        Added = "—",
                        AlreadyExisted = "—",
                        AddedParameters = "—",
                        Notes = ex.Message
                    });
                }
            }

            progressWindow.Close();

            var summary = new (string, string)[]
            {
                ("Shared Parameter Group",     settings.GroupName),
                ("Mode / Category",            settings.Mode),
                ("Parameter Scope",            settings.IsInstance ? "Instance" : "Type"),
                ("Families Found",             familyCount.ToString()),
                ("Families Processed  ✅",     processedCount.ToString()),
                ("Families Skipped    ⏭",     skippedCount.ToString()),
                ("Families Failed     ❌",     failedCount.ToString()),
                ("Parameters Added",           totalAdded.ToString()),
                ("Parameters Already Existed", totalAlreadyHad.ToString()),
                ("Group Cache Mismatches  ⚠", totalGroupMismatches.ToString()),
            };

            var resultColumns = new (string, double, string)[]
            {
                ("Family Name",      220, "FamilyName"),
                ("Status",            95, "Status"),
                ("Added",             55, "Added"),
                ("Already Existed",  110, "AlreadyExisted"),
                ("Added Parameters", 350, "AddedParameters"),
                ("Notes",            280, "Notes"),
            };

            new UnifiedReportWindow(
                "Batch Parameter Injection — Results",
                summary, resultColumns,
                familyResults.Cast<object>()).ShowDialog();

            return Result.Succeeded;
        }

        private static ForgeTypeId ResolveGroupForgeTypeId(string groupName)
        {
            return AddInConstants.GroupForgeIds.TryGetValue(groupName, out ForgeTypeId id)
                ? id : GroupTypeId.General;
        }

        private static List<string> DetectGroupCacheMismatches(
            Document doc, ElementId familyId,
            List<ExternalDefinition> addedDefinitions,
            ForgeTypeId requestedGroup, bool isInstance)
        {
            var mismatches = new List<string>();
            if (addedDefinitions.Count == 0) return mismatches;
            try
            {
                Family reloaded = doc.GetElement(familyId) as Family;
                if (reloaded == null) return mismatches;

                Element probe = null;
                if (isInstance)
                {
                    probe = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                        .FirstOrDefault(fi => fi.Symbol?.Family?.Id == familyId);
                }
                if (probe == null)
                {
                    ICollection<ElementId> symbolIds = reloaded.GetFamilySymbolIds();
                    if (symbolIds.Count > 0)
                        probe = doc.GetElement(symbolIds.First()) as FamilySymbol;
                }
                if (probe == null) return mismatches;

                foreach (ExternalDefinition def in addedDefinitions)
                {
                    Parameter p = probe.LookupParameter(def.Name);
                    if (p == null) continue;
                    if (!(p.Definition is InternalDefinition internalDef)) continue;
                    ForgeTypeId actualGroup;
                    try { actualGroup = internalDef.GetGroupTypeId(); } catch { continue; }
                    if (actualGroup == null || actualGroup.Equals(requestedGroup)) continue;
                    string actualLabel;
                    try { actualLabel = LabelUtils.GetLabelForGroup(actualGroup); } catch { actualLabel = actualGroup.TypeId; }
                    mismatches.Add($"{def.Name} → {actualLabel}");
                }
            }
            catch { }
            return mismatches;
        }

        private static void SafeClose(Document fd) { try { fd?.Close(false); } catch { } }

        private class SharedParamSettings
        {
            public string GroupName { get; set; }
            public List<string> SelectedParams { get; set; }
            public string RevitGroupName { get; set; }
            public string Mode { get; set; }
            public bool IsInstance { get; set; }
        }

        private class SharedParamSettingsWindow : AvesBaseWindow
        {
            private readonly SharedParamSettings _settings;
            private readonly List<string> _availableGroups;
            private readonly Dictionary<string, DefinitionGroup> _groupsByName;
            private readonly Button _groupBtn;
            private readonly WrapPanel _chipsPanel;
            private readonly Button _revitGroupBtn;
            private readonly Button _modeBtn;
            private readonly WpfToggleButton _instanceBtn;
            private readonly WpfToggleButton _typeBtn;

            public SharedParamSettingsWindow(
                SharedParamSettings settings,
                List<string> availableGroups,
                Dictionary<string, DefinitionGroup> groupsByName)
                : base("Batch Shared Parameters — Settings")
            {
                _settings = settings;
                _availableGroups = availableGroups;
                _groupsByName = groupsByName;

                Width = 520; Height = 620;
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                ResizeMode = ResizeMode.NoResize;

                BodyPanel.Children.Add(Av.FieldLabel(1, "Shared parameter group"));
                _groupBtn = Av.PickerButton(_settings.GroupName);
                _groupBtn.Margin = new Thickness(0, 0, 0, 16);
                _groupBtn.Click += (s, e) =>
                {
                    string picked = SinglePickerWindow.Show(
                        "Select Shared Parameter Group", _availableGroups, _settings.GroupName, this);
                    if (picked != null && picked != _settings.GroupName)
                    {
                        _settings.GroupName = picked;
                        _settings.SelectedParams.Clear();
                        Av.SetPickerValue(_groupBtn, picked);
                        RefreshChips();
                    }
                };
                BodyPanel.Children.Add(_groupBtn);

                BodyPanel.Children.Add(Av.FieldLabel(2, "Parameters to inject"));
                _chipsPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
                BodyPanel.Children.Add(_chipsPanel);

                var addChipBtn = new Button
                {
                    Cursor = Cursors.Hand,
                    FontFamily = Av.Ui,
                    FontSize = 12,
                    Foreground = Av.Brush(Av.TextDim),
                    Margin = new Thickness(0, 0, 0, 16),
                    Template = DashedChipTemplate()
                };
                addChipBtn.Content = "+ Add parameters";
                addChipBtn.Click += (s, e) =>
                {
                    if (!_groupsByName.TryGetValue(_settings.GroupName, out DefinitionGroup group)) return;
                    var names = group.Definitions.Cast<Definition>().Select(d => d.Name).OrderBy(n => n).ToList();
                    var picked = MultiPickerWindow.Show("Select Parameters to Inject", names, _settings.SelectedParams, this);
                    if (picked != null)
                    {
                        _settings.SelectedParams = picked;
                        RefreshChips();
                    }
                };
                BodyPanel.Children.Add(addChipBtn);

                var twoCol = new WpfGrid { Margin = new Thickness(0, 0, 0, 16) };
                twoCol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                twoCol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                twoCol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var leftStack = new StackPanel();
                leftStack.Children.Add(Av.FieldLabel(3, "Target Revit group"));
                _revitGroupBtn = Av.PickerButton(_settings.RevitGroupName);
                _revitGroupBtn.Click += (s, e) =>
                {
                    string picked = SinglePickerWindow.Show(
                        "Select Target Parameter Group",
                        AddInConstants.RevitGroupChoices, _settings.RevitGroupName, this);
                    if (picked != null)
                    {
                        _settings.RevitGroupName = picked;
                        Av.SetPickerValue(_revitGroupBtn, picked);
                    }
                };
                leftStack.Children.Add(_revitGroupBtn);
                WpfGrid.SetColumn(leftStack, 0);
                twoCol.Children.Add(leftStack);

                var rightStack = new StackPanel();
                rightStack.Children.Add(Av.FieldLabel(4, "Mode / category"));
                _modeBtn = Av.PickerButton(_settings.Mode);
                _modeBtn.Click += (s, e) =>
                {
                    string picked = SinglePickerWindow.Show(
                        "Select Mode / Category",
                        AddInConstants.ModeChoices, _settings.Mode, this);
                    if (picked != null)
                    {
                        _settings.Mode = picked;
                        Av.SetPickerValue(_modeBtn, picked);
                    }
                };
                rightStack.Children.Add(_modeBtn);
                WpfGrid.SetColumn(rightStack, 2);
                twoCol.Children.Add(rightStack);

                BodyPanel.Children.Add(twoCol);

                BodyPanel.Children.Add(Av.FieldLabel(5, "Parameter scope"));
                var (toggleContainer, instanceBtn, typeBtn) =
                    Av.SegmentedToggle("Instance", "Type", _settings.IsInstance);
                _instanceBtn = instanceBtn;
                _typeBtn = typeBtn;
                _instanceBtn.Checked += (s, e) => _settings.IsInstance = true;
                _typeBtn.Checked += (s, e) => _settings.IsInstance = false;
                toggleContainer.Margin = new Thickness(0, 0, 0, 0);
                BodyPanel.Children.Add(toggleContainer);

                var changeFileBtn = Av.GhostButton("Change Shared Parameter File");
                changeFileBtn.Click += (_, __) =>
                {
                    string picked = SharedParamFileManager.PromptForFile(this);
                    if (picked == null) return;
                    SharedParamFileManager.SavePath(picked);
                    TaskDialog.Show("Shared Parameter File Updated",
                        $"New file saved:\n{picked}\n\n" +
                        "This will be used the next time you run this command.");
                };
                var changeFileBorder = new WpfBorder
                {
                    BorderBrush = Av.Brush(Av.BorderStrong),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Child = changeFileBtn
                };

                var cancelBtn = Av.GhostButton("Cancel");
                cancelBtn.Click += (_, __) => { DialogResult = false; };
                var runBtn = Av.PrimaryButton("▶  Run Script");
                runBtn.Click += (_, __) => { DialogResult = true; };

                var footerRow = new WpfGrid();
                footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                WpfGrid.SetColumn(changeFileBorder, 0);
                changeFileBorder.HorizontalAlignment = HorizontalAlignment.Left;
                footerRow.Children.Add(changeFileBorder);

                var rightButtons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                rightButtons.Children.Add(cancelBtn);
                rightButtons.Children.Add(new WpfBorder { Width = 10 });
                rightButtons.Children.Add(runBtn);
                WpfGrid.SetColumn(rightButtons, 1);
                footerRow.Children.Add(rightButtons);

                SetFooter(footerRow);

                RefreshChips();
            }

            private void RefreshChips()
            {
                _chipsPanel.Children.Clear();
                foreach (string p in _settings.SelectedParams)
                    _chipsPanel.Children.Add(Av.Chip(p));
                if (_settings.SelectedParams.Count == 0)
                {
                    _chipsPanel.Children.Add(new TextBlock
                    {
                        Text = "None selected",
                        FontFamily = Av.Ui,
                        FontSize = 12,
                        Foreground = Av.Brush(Av.TextFaint),
                        Margin = new Thickness(0, 0, 0, 8)
                    });
                }
            }

            private static ControlTemplate DashedChipTemplate()
            {
                var t = new ControlTemplate(typeof(Button));
                var bd = new FrameworkElementFactory(typeof(WpfBorder));
                bd.Name = "bd";
                bd.SetValue(WpfBorder.CornerRadiusProperty, new CornerRadius(6));
                bd.SetValue(WpfBorder.BackgroundProperty, new SolidColorBrush(Colors.Transparent));
                bd.SetValue(WpfBorder.PaddingProperty, new Thickness(11, 6, 11, 6));
                bd.SetValue(WpfBorder.BorderBrushProperty, Av.Brush(Av.BorderStrong));
                bd.SetValue(WpfBorder.BorderThicknessProperty, new Thickness(1));
                var cp = new FrameworkElementFactory(typeof(ContentPresenter));
                cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                bd.AppendChild(cp);
                t.VisualTree = bd;
                var hover = new WpfTrigger { Property = UIElement.IsMouseOverProperty, Value = true };
                hover.Setters.Add(new WpfSetter(WpfBorder.BackgroundProperty, Av.Brush(Av.Surface2), "bd"));
                t.Triggers.Add(hover);
                return t;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Cmd_BatchRemoveParams
    // ══════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    public class Cmd_BatchRemoveParams : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiDoc = commandData.Application.ActiveUIDocument;
            var doc = uiDoc.Document;

            string defaultMode = uiDoc.Selection.GetElementIds().Count > 0
                ? AddInConstants.SelectionMode : "Generic Models";

            var settings = new RemoveSettings
            {
                SelectedParams = new List<string>(),
                Mode = defaultMode
            };

            while (true)
            {
                var win = new RemoveSettingsWindow(settings, uiDoc, doc);
                if (win.ShowDialog() != true) return Result.Cancelled;
                if (settings.SelectedParams.Count > 0) break;
                TaskDialog.Show("Validation", "You must select at least one parameter before running.");
            }

            var familiesToProcess = FamilyResolver.Resolve(settings.Mode, uiDoc, doc);
            if (familiesToProcess.Count == 0)
            {
                TaskDialog.Show("No Families Found",
                    $"No families matched the selected mode / category:\n\"{settings.Mode}\"");
                return Result.Cancelled;
            }

            var familyResults = new List<RemoveParamReportRow>();
            int processedCount = 0, skippedCount = 0, failedCount = 0, totalRemoved = 0;

            var familyIds = familiesToProcess.Select(f => f.Id).ToList();
            var familyNames = familiesToProcess.Select(f => f.Name).ToList();
            int familyCount = familyIds.Count;

            var loadOptions = new DefaultFamilyLoadOptions();
            var progressWindow = new BatchProgressWindow("Purging Parameters…", familyCount);
            progressWindow.Show();

            for (int i = 0; i < familyCount; i++)
            {
                if (progressWindow.IsCancelled) break;

                string familyName = familyNames[i];
                progressWindow.Update(i + 1, familyCount, $"Processing: {familyName}");

                Family family = doc.GetElement(familyIds[i]) as Family;
                if (family == null)
                {
                    failedCount++;
                    familyResults.Add(new RemoveParamReportRow
                    {
                        FamilyName = familyName,
                        Status = "❌",
                        Removed = "—",
                        NotFound = "—",
                        RemovedParameters = "—",
                        Notes = "Family element is no longer accessible."
                    });
                    continue;
                }

                if (!family.IsEditable)
                {
                    skippedCount++;
                    familyResults.Add(new RemoveParamReportRow
                    {
                        FamilyName = familyName,
                        Status = "⏭",
                        Removed = "0",
                        NotFound = "—",
                        RemovedParameters = "—",
                        Notes = "System / Non-Editable"
                    });
                    continue;
                }

                Document familyDocument = null;
                try { familyDocument = doc.EditFamily(family); }
                catch (Exception ex)
                {
                    failedCount++;
                    familyResults.Add(new RemoveParamReportRow
                    {
                        FamilyName = familyName,
                        Status = "❌",
                        Removed = "—",
                        NotFound = "—",
                        RemovedParameters = "—",
                        Notes = $"Cannot open for editing — {ex.Message}"
                    });
                    continue;
                }

                try
                {
                    FamilyManager familyManager = familyDocument.FamilyManager;
                    var parametersToRemove = familyManager.Parameters
                        .Cast<FamilyParameter>()
                        .Where(p => settings.SelectedParams.Contains(p.Definition.Name))
                        .ToList();

                    int notFoundCount = settings.SelectedParams.Count - parametersToRemove.Count;

                    if (parametersToRemove.Count == 0)
                    {
                        skippedCount++;
                        familyResults.Add(new RemoveParamReportRow
                        {
                            FamilyName = familyName,
                            Status = "⏭",
                            Removed = "0",
                            NotFound = notFoundCount.ToString(),
                            RemovedParameters = "—",
                            Notes = "No matching parameters found"
                        });
                        familyDocument.Close(false);
                        continue;
                    }

                    var targetNames = parametersToRemove
                        .Select(p => p.Definition?.Name ?? "(unknown)").ToList();

                    int removedThisFamily = 0;
                    using (var transaction = new Transaction(familyDocument, "Purge Parameters"))
                    {
                        transaction.Start();
                        foreach (FamilyParameter parameter in parametersToRemove)
                        {
                            try { familyManager.RemoveParameter(parameter); removedThisFamily++; }
                            catch { }
                        }
                        transaction.Commit();
                    }

                    familyDocument.LoadFamily(doc, loadOptions);
                    FamilyNameGuard.RestoreIfTruncated(doc, familyIds[i], familyName);

                    totalRemoved += removedThisFamily;
                    processedCount++;
                    familyResults.Add(new RemoveParamReportRow
                    {
                        FamilyName = familyName,
                        Status = removedThisFamily > 0 ? "✅" : "⏭",
                        Removed = removedThisFamily.ToString(),
                        NotFound = notFoundCount.ToString(),
                        RemovedParameters = removedThisFamily > 0 ? string.Join(", ", targetNames) : "—",
                        Notes = string.Empty
                    });
                    familyDocument.Close(false);
                }
                catch (Exception ex)
                {
                    SafeClose(familyDocument);
                    failedCount++;
                    familyResults.Add(new RemoveParamReportRow
                    {
                        FamilyName = familyName,
                        Status = "❌",
                        Removed = "—",
                        NotFound = "—",
                        RemovedParameters = "—",
                        Notes = ex.Message
                    });
                }
            }

            progressWindow.Close();

            var summary = new (string, string)[]
            {
                ("Mode / Category",        settings.Mode),
                ("Families Found",         familiesToProcess.Count.ToString()),
                ("Families Processed  ✅", processedCount.ToString()),
                ("Families Skipped    ⏭", skippedCount.ToString()),
                ("Families Failed     ❌", failedCount.ToString()),
                ("Parameters Removed",     totalRemoved.ToString()),
            };

            var resultColumns = new (string, double, string)[]
            {
                ("Family Name",        220, "FamilyName"),
                ("Status",              95, "Status"),
                ("Removed",             70, "Removed"),
                ("Not Found",           80, "NotFound"),
                ("Removed Parameters", 350, "RemovedParameters"),
                ("Notes",              170, "Notes"),
            };

            new UnifiedReportWindow(
                "Batch Parameter Removal — Results",
                summary, resultColumns,
                familyResults.Cast<object>()).ShowDialog();

            return Result.Succeeded;
        }

        private static void SafeClose(Document fd) { try { fd?.Close(false); } catch { } }

        private class RemoveSettings
        {
            public List<string> SelectedParams { get; set; }
            public string Mode { get; set; }
        }

        private class RemoveSettingsWindow : AvesBaseWindow
        {
            private readonly RemoveSettings _settings;
            private readonly UIDocument _uiDoc;
            private readonly Document _doc;
            private readonly WrapPanel _chipsPanel;
            private readonly Button _modeBtn;

            public RemoveSettingsWindow(RemoveSettings settings, UIDocument uiDoc, Document doc)
                : base("Universal Parameter Remover — Settings")
            {
                _settings = settings;
                _uiDoc = uiDoc;
                _doc = doc;

                Width = 480; Height = 460;
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                ResizeMode = ResizeMode.NoResize;

                BodyPanel.Children.Add(Av.FieldLabel(1, "Parameters to remove"));
                _chipsPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
                BodyPanel.Children.Add(_chipsPanel);

                var addChipBtn = new Button
                {
                    Cursor = Cursors.Hand,
                    FontFamily = Av.Ui,
                    FontSize = 12,
                    Foreground = Av.Brush(Av.TextDim),
                    Margin = new Thickness(0, 0, 0, 16),
                    Template = DashedChipTemplate()
                };
                addChipBtn.Content = "+ Select parameters";
                addChipBtn.Click += (s, e) => ScanAndPickParameters();
                BodyPanel.Children.Add(addChipBtn);

                BodyPanel.Children.Add(Av.FieldLabel(2, "Mode / category"));
                _modeBtn = Av.PickerButton(_settings.Mode);
                _modeBtn.Margin = new Thickness(0, 0, 0, 16);
                _modeBtn.Click += (s, e) =>
                {
                    string picked = SinglePickerWindow.Show(
                        "Select Mode / Category",
                        AddInConstants.ModeChoices, _settings.Mode, this);
                    if (picked != null && picked != _settings.Mode)
                    {
                        _settings.Mode = picked;
                        _settings.SelectedParams.Clear();
                        Av.SetPickerValue(_modeBtn, picked);
                        RefreshChips();
                    }
                };
                BodyPanel.Children.Add(_modeBtn);

                var cancelBtn = Av.GhostButton("Cancel");
                cancelBtn.Click += (_, __) => { DialogResult = false; };
                var runBtn = Av.PrimaryButton("▶  Run Removal");
                runBtn.Click += (_, __) => { DialogResult = true; };

                var footerRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                footerRow.Children.Add(cancelBtn);
                footerRow.Children.Add(new WpfBorder { Width = 10 });
                footerRow.Children.Add(runBtn);
                SetFooter(footerRow);

                RefreshChips();
            }

            private void RefreshChips()
            {
                _chipsPanel.Children.Clear();
                foreach (string p in _settings.SelectedParams)
                    _chipsPanel.Children.Add(Av.Chip(p));
                if (_settings.SelectedParams.Count == 0)
                {
                    _chipsPanel.Children.Add(new TextBlock
                    {
                        Text = "None selected",
                        FontFamily = Av.Ui,
                        FontSize = 12,
                        Foreground = Av.Brush(Av.TextFaint),
                        Margin = new Thickness(0, 0, 0, 8)
                    });
                }
            }

            private void ScanAndPickParameters()
            {
                var families = FamilyResolver.Resolve(_settings.Mode, _uiDoc, _doc);
                if (families.Count == 0)
                {
                    TaskDialog.Show("No Families",
                        "No families found to scan for the current mode / category.");
                    return;
                }

                int scanCap = Math.Min(families.Count, AddInConstants.FamilyScanCap);
                var foundNames = new HashSet<string>();
                var scanWindow = new BatchProgressWindow("Scanning families for parameters…", scanCap);
                scanWindow.Show();

                for (int i = 0; i < scanCap; i++)
                {
                    if (scanWindow.IsCancelled) break;
                    Family family = families[i];
                    if (!family.IsEditable) continue;
                    scanWindow.Update(i + 1, scanCap, $"Scanning: {family.Name}");

                    Document familyDocument = null;
                    try
                    {
                        familyDocument = _doc.EditFamily(family);
                        foreach (FamilyParameter p in familyDocument.FamilyManager.Parameters)
                        {
                            bool isUserDefined = p.IsShared ||
                                (p.Definition is InternalDefinition internalDef &&
                                 internalDef.BuiltInParameter == BuiltInParameter.INVALID);
                            if (isUserDefined) foundNames.Add(p.Definition.Name);
                        }
                        familyDocument.Close(false);
                    }
                    catch { try { familyDocument?.Close(false); } catch { } }
                }

                scanWindow.Close();

                if (foundNames.Count == 0)
                {
                    TaskDialog.Show("No Parameters",
                        "No user-defined parameters were found in the scanned families.");
                    return;
                }

                var picked = MultiPickerWindow.Show(
                    "Select Parameters to Purge",
                    foundNames.OrderBy(n => n).ToList(),
                    _settings.SelectedParams, this);

                if (picked != null)
                {
                    _settings.SelectedParams = picked;
                    RefreshChips();
                }
            }

            private static ControlTemplate DashedChipTemplate()
            {
                var t = new ControlTemplate(typeof(Button));
                var bd = new FrameworkElementFactory(typeof(WpfBorder));
                bd.Name = "bd";
                bd.SetValue(WpfBorder.CornerRadiusProperty, new CornerRadius(6));
                bd.SetValue(WpfBorder.BackgroundProperty, new SolidColorBrush(Colors.Transparent));
                bd.SetValue(WpfBorder.PaddingProperty, new Thickness(11, 6, 11, 6));
                bd.SetValue(WpfBorder.BorderBrushProperty, Av.Brush(Av.BorderStrong));
                bd.SetValue(WpfBorder.BorderThicknessProperty, new Thickness(1));
                var cp = new FrameworkElementFactory(typeof(ContentPresenter));
                cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                bd.AppendChild(cp);
                t.VisualTree = bd;
                var hover = new WpfTrigger { Property = UIElement.IsMouseOverProperty, Value = true };
                hover.Setters.Add(new WpfSetter(WpfBorder.BackgroundProperty, Av.Brush(Av.Surface2), "bd"));
                t.Triggers.Add(hover);
                return t;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Cmd_CsvImporter
    // ══════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    public class Cmd_CsvImporter : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiDoc = commandData.Application.ActiveUIDocument;
            var doc = uiDoc.Document;

            var openDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Data CSV File",
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                DefaultExt = ".csv"
            };
            if (openDialog.ShowDialog() != true) return Result.Cancelled;
            string csvPath = openDialog.FileName;

            Dictionary<string, Dictionary<string, string>> dataMap;
            try { dataMap = LoadDataMap(csvPath); }
            catch (Exception ex)
            {
                string hint = ex.Message.ToLower().Contains("encoding") ||
                              ex is System.Text.DecoderFallbackException
                    ? "\n\nTip: Re-export from Excel as 'CSV UTF-8 (Comma delimited)'." : string.Empty;
                TaskDialog.Show("CSV Error", $"Error reading CSV:\n{ex.Message}{hint}");
                return Result.Cancelled;
            }

            if (dataMap.Count == 0)
            {
                TaskDialog.Show("CSV Error",
                    "The CSV file is empty or does not contain an 'Item Number' column.");
                return Result.Cancelled;
            }

            var selectionIds = uiDoc.Selection.GetElementIds();
            if (selectionIds.Count == 0)
            {
                TaskDialog.Show("No Selection",
                    "Select one or more elements in the model before running this command.");
                return Result.Cancelled;
            }

            var materialCache = BuildMaterialCache(doc);
            Material defaultMaterial;
            try { defaultMaterial = GetDefaultMaterial(materialCache); }
            catch (Exception ex) { TaskDialog.Show("Error", ex.Message); return Result.Cancelled; }

            var parameterLog = new List<CsvParamLogEntry>();
            var foundItemNumbers = new HashSet<string>();
            var missingItemNumbers = new HashSet<string>();
            var processedTypeIds = new HashSet<ElementId>();

            try
            {
                using (var transaction = new Transaction(doc, "Import CSV Data"))
                {
                    transaction.Start();
                    foreach (ElementId elementId in selectionIds)
                    {
                        Element element = doc.GetElement(elementId);
                        if (element == null) continue;

                        ElementId typeId; Element elementType;
                        try
                        {
                            typeId = element.GetTypeId();
                            if (typeId == ElementId.InvalidElementId) continue;
                            elementType = doc.GetElement(typeId);
                            if (elementType == null) continue;
                        }
                        catch { continue; }

                        string itemNumber = GetItemNumber(element, elementType);
                        if (string.IsNullOrEmpty(itemNumber)) continue;

                        if (!dataMap.TryGetValue(itemNumber, out var rowData))
                        { missingItemNumbers.Add(itemNumber); continue; }

                        foundItemNumbers.Add(itemNumber);
                        bool alreadyProcessedType = processedTypeIds.Contains(typeId);
                        processedTypeIds.Add(typeId);

                        foreach (var kvp in rowData)
                        {
                            string paramName = kvp.Key, paramValue = kvp.Value;
                            if (string.IsNullOrEmpty(paramName)) continue;

                            Parameter typeParam = elementType?.LookupParameter(paramName);
                            Parameter instanceParam = element.LookupParameter(paramName);
                            bool isTypeLevel = typeParam != null &&
                                (instanceParam == null || typeParam.Id != instanceParam.Id);

                            if (isTypeLevel && alreadyProcessedType) continue;

                            var (success, scope, status, detail) = ResolveAndSetParameter(
                                element, elementType, paramName, paramValue,
                                doc, materialCache, defaultMaterial);

                            parameterLog.Add(new CsvParamLogEntry
                            {
                                ItemNumber = itemNumber,
                                ParamName = paramName,
                                Value = paramValue,
                                Scope = scope,
                                Status = status,
                                Detail = detail
                            });
                        }
                    }
                    transaction.Commit();
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Transaction Error", $"An error occurred during processing:\n{ex.Message}");
                return Result.Cancelled;
            }

            var aggregated = AggregateResults(parameterLog);
            int totalSet = aggregated.Values.Sum(v => v.SetParams.Count);
            int totalFailed = aggregated.Values.Sum(v => v.FailedParams.Count);
            int totalSkipped = aggregated.Values.Sum(v => v.SkippedParams.Count);

            var resultRows = aggregated.Values
                .OrderBy(v => v.ItemNumber)
                .Select(v =>
                {
                    string status = v.FailedParams.Count == 0 && v.SetParams.Count > 0 ? "✅"
                                  : v.FailedParams.Count > 0 && v.SetParams.Count > 0 ? "⚠️"
                                  : v.FailedParams.Count > 0 && v.SetParams.Count == 0 ? "❌" : "⏭";
                    return (object)new CsvItemReportRow
                    {
                        ItemNumber = v.ItemNumber,
                        Status = status,
                        SetCount = v.SetParams.Count.ToString(),
                        FailedCount = v.FailedParams.Count.ToString(),
                        SkippedCount = v.SkippedParams.Count.ToString(),
                        SetParameters = string.Join(", ", v.SetParams.OrderBy(s => s)),
                        FailedParameters = string.Join(", ", v.FailedParams.OrderBy(s => s)),
                        SkippedParameters = string.Join(", ", v.SkippedParams.OrderBy(s => s))
                    };
                }).ToList();

            var summary = new (string, string)[]
            {
                ("CSV Source",            System.IO.Path.GetFileName(csvPath)),
                ("Elements Selected",     selectionIds.Count.ToString()),
                ("Types Processed",       processedTypeIds.Count.ToString()),
                ("Matched in CSV",        foundItemNumbers.Count.ToString()),
                ("Missing from CSV",      missingItemNumbers.Count.ToString()),
                ("Parameters SET    ✅",  totalSet.ToString()),
                ("Parameters FAILED ❌",  totalFailed.ToString()),
                ("Parameters SKIPPED ⏭", totalSkipped.ToString()),
            };

            var resultColumns = new (string, double, string)[]
            {
                ("Item Number",         110, "ItemNumber"),
                ("Status",               95, "Status"),
                ("SET",                  45, "SetCount"),
                ("FAILED",               55, "FailedCount"),
                ("SKIPPED",              65, "SkippedCount"),
                ("Set Parameters",      160, "SetParameters"),
                ("Failed Parameters",   160, "FailedParameters"),
                ("Skipped Parameters",  160, "SkippedParameters"),
            };

            var warnings = missingItemNumbers.Count > 0
                ? missingItemNumbers.OrderBy(s => s)
                    .Select(s => (s, "Item Number not found in CSV")).ToList()
                : null;

            new UnifiedReportWindow(
                "CSV Import — Results",
                summary, resultColumns, resultRows,
                warnings, csvPath).ShowDialog();

            return Result.Succeeded;
        }

        private static Dictionary<string, CsvAggregatedEntry> AggregateResults(
            List<CsvParamLogEntry> log)
        {
            var aggregated = new Dictionary<string, CsvAggregatedEntry>();
            foreach (var entry in log)
            {
                if (!aggregated.TryGetValue(entry.ItemNumber, out var agg))
                {
                    agg = new CsvAggregatedEntry { ItemNumber = entry.ItemNumber };
                    aggregated[entry.ItemNumber] = agg;
                }
                string label = string.IsNullOrEmpty(entry.Detail)
                    ? entry.ParamName : $"{entry.ParamName} ({entry.Detail})";

                if (entry.Status == "SET") agg.SetParams.Add(entry.ParamName);
                else if (entry.Status == "FAILED") agg.FailedParams.Add(label);
                else if (entry.Status == "SKIPPED") agg.SkippedParams.Add(label);
            }
            return aggregated;
        }

        private static Dictionary<string, Material> BuildMaterialCache(Document doc) =>
            new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>()
                .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        private static Material GetDefaultMaterial(Dictionary<string, Material> cache)
        {
            if (cache.TryGetValue("Default", out Material m)) return m;
            Material first = cache.Values.FirstOrDefault();
            if (first != null) return first;
            throw new InvalidOperationException("No materials found in the project.");
        }

        private static (Material material, string status) GetOrCreateMaterial(
            Document doc, string name, Dictionary<string, Material> cache, Material defaultMaterial)
        {
            if (string.IsNullOrWhiteSpace(name)) return (null, "NO_VALUE");
            name = name.Trim();
            if (cache.TryGetValue(name, out Material existing)) return (existing, "FOUND");
            if (defaultMaterial.Duplicate(name) is not Material newMat) return (null, "FAILED");
            cache[name] = newMat;
            return (newMat, "CREATED");
        }

        private static bool IsMaterialParameter(Parameter p)
        {
            if (p.StorageType != StorageType.ElementId) return false;
            try { return p.Definition.GetDataType().Equals(SpecTypeId.Reference.Material); }
            catch { return p.Definition.Name.IndexOf("material", StringComparison.OrdinalIgnoreCase) >= 0; }
        }

        private static string GetItemNumber(Element element, Element elementType)
        {
            foreach (Element src in new[] { element, elementType })
            {
                if (src == null) continue;
                Parameter p = src.LookupParameter("Item Number");
                if (p?.StorageType == StorageType.String)
                {
                    string v = p.AsString();
                    if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
                }
            }
            return null;
        }

        private static (bool success, string status, string detail) TrySetParameter(
            Parameter p, string value, Document doc,
            Dictionary<string, Material> materialCache, Material defaultMaterial)
        {
            if (p.IsReadOnly) return (false, "Read-Only", string.Empty);
            try
            {
                if (IsMaterialParameter(p))
                {
                    var (mat, matStatus) = GetOrCreateMaterial(doc, value, materialCache, defaultMaterial);
                    if (mat != null) { p.Set(mat.Id); return (true, "SET", $"Material {matStatus}"); }
                    return (false, "FAILED", "No Material Value");
                }
                switch (p.StorageType)
                {
                    case StorageType.String: p.Set(value); break;
                    case StorageType.Double:
                        p.Set(double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)); break;
                    case StorageType.Integer: p.Set(int.Parse(value)); break;
                    case StorageType.ElementId: return (false, "SKIPPED", "ElementId — not a Material parameter");
                    default: return (false, "SKIPPED", "Unsupported StorageType");
                }
                return (true, "SET", string.Empty);
            }
            catch (Exception ex)
            {
                return (false, "FAILED", $"Format Error: {ex.Message.Replace("\t", " ").Replace("\n", " ")}");
            }
        }

        private static (bool success, string scope, string status, string detail)
            ResolveAndSetParameter(
                Element element, Element elementType, string name, string value,
                Document doc, Dictionary<string, Material> materialCache, Material defaultMaterial)
        {
            foreach (var (source, scope) in new[] { (elementType, "Type"), (element, "Instance") })
            {
                if (source == null) continue;
                Parameter p = source.LookupParameter(name);
                if (p == null || p.IsReadOnly) continue;
                var (success, status, detail) =
                    TrySetParameter(p, value, doc, materialCache, defaultMaterial);
                return (success, scope, status, detail);
            }
            Parameter fallback = element.LookupParameter(name);
            return fallback == null
                ? (false, string.Empty, "FAILED", "Parameter not found on Type or Instance")
                : (false, string.Empty, "FAILED", "Read-Only on both Type and Instance");
        }

        private static Dictionary<string, Dictionary<string, string>> LoadDataMap(string path)
        {
            var data = new Dictionary<string, Dictionary<string, string>>();
            using (var reader = new System.IO.StreamReader(path, System.Text.Encoding.UTF8, true))
            {
                string headerLine = reader.ReadLine();
                if (headerLine == null) return data;
                if (headerLine.Length > 0 && headerLine[0] == '\uFEFF')
                    headerLine = headerLine.Substring(1);

                List<string> headers = ParseCsvLine(headerLine);
                int itemNumberIdx = headers.IndexOf("Item Number");
                if (itemNumberIdx < 0) return data;

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    List<string> values = ParseCsvLine(line);
                    if (itemNumberIdx >= values.Count) continue;
                    string key = values[itemNumberIdx].Trim();
                    if (string.IsNullOrEmpty(key)) continue;
                    var row = new Dictionary<string, string>();
                    for (int i = 0; i < headers.Count; i++)
                    {
                        if (i == itemNumberIdx || i >= values.Count) continue;
                        if (!string.IsNullOrEmpty(headers[i])) row[headers[i]] = values[i];
                    }
                    data[key] = row;
                }
            }
            return data;
        }

        private static List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var buffer = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { buffer.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else buffer.Append(c);
                }
                else
                {
                    if (c == '"') inQuotes = true;
                    else if (c == ',') { fields.Add(buffer.ToString()); buffer.Clear(); }
                    else buffer.Append(c);
                }
            }
            fields.Add(buffer.ToString());
            return fields;
        }

        private class CsvParamLogEntry
        {
            public string ItemNumber { get; set; }
            public string ParamName { get; set; }
            public string Value { get; set; }
            public string Scope { get; set; }
            public string Status { get; set; }
            public string Detail { get; set; }
        }

        private class CsvAggregatedEntry
        {
            public string ItemNumber { get; set; }
            public List<string> SetParams = new List<string>();
            public List<string> FailedParams = new List<string>();
            public List<string> SkippedParams = new List<string>();
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  UiHelpers
    // ══════════════════════════════════════════════════════════════════════

    internal static class UiHelpers
    {
        internal static TextBlock MakeHint(string text, int row)
        {
            var hint = new TextBlock
            {
                Text = text,
                Margin = new Thickness(10, 8, 10, 2),
                FontStyle = FontStyles.Italic,
                Foreground = Av.Brush(Av.TextFaint),
                FontSize = 11
            };
            WpfGrid.SetRow(hint, row);
            return hint;
        }

        internal static StackPanel MakeButtonRow(
            int row, params (string label, double width, RoutedEventHandler click)[] buttons)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(10, 6, 10, 10)
            };
            bool isFirst = true;
            foreach (var (label, width, click) in buttons)
            {
                var button = isFirst
                    ? Av.GhostButton(label, width)
                    : Av.PrimaryButton(label, width);
                button.Margin = isFirst ? new Thickness(0) : new Thickness(8, 0, 0, 0);
                button.Click += click;
                panel.Children.Add(button);
                isFirst = false;
            }
            WpfGrid.SetRow(panel, row);
            return panel;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  FamilyNameGuard
    // ══════════════════════════════════════════════════════════════════════

    internal static class FamilyNameGuard
    {
        internal static void RestoreIfTruncated(Document doc, ElementId familyId, string originalName)
        {
            if (!originalName.Contains('/')) return;
            if (!(doc.GetElement(familyId) is Family reloadedFamily)) return;
            if (reloadedFamily.Name == originalName) return;

            using (var tx = new Transaction(doc, $"Restore family name: {originalName}"))
            {
                try
                {
                    tx.Start();
                    reloadedFamily.Name = originalName;
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    TaskDialog.Show("Family Name Warning",
                        $"Parameter changes were applied successfully, but the family name\n" +
                        $"could not be restored to \"{originalName}\" after reload.\n\n" +
                        $"Current name: \"{reloadedFamily.Name}\"\n\n" +
                        $"Reason: {ex.Message}\n\n" +
                        $"Please rename the family manually in the Project Browser.");
                }
            }
        }
    }
}