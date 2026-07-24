# TODO

## Pending Investigation — 2026-07-24

Investigation only, nothing fixed yet. Findings for the 5 items flagged at end-of-session.

### 1. `AvControls.TitleBarWithSubtitle` — confirmed zero callers

Re-grepped the whole solution. Only hit is the method's own definition:

```
AVES/Core/AvControls.cs:109:        public static Border TitleBarWithSubtitle(string title, string? subtitle = null)
```

Safe to delete whenever the header-styling task's Step 3 is picked back up.

### 2. Pattern B breadcrumb occurrences (`"Title — extra"` same-line style)

- **`Cmds_Pulldown4.cs`** — none remaining. Line 406 still calls `AvControls.TitleBar("Batch Tag", "")`, but the breadcrumb argument is an empty string (already neutralized by the earlier drift-fix commit `6e3788f`), so nothing renders. Structurally still a 2-arg call, but no live text.
- **`Cmds_Button7.cs:574-581`** — live, dynamic breadcrumb:
  ```csharp
  Text = $"  — {processed} of {assembliesFound} assemblies processed",
  FontSize = 12,
  Foreground = AvTokens.B(AvTokens.TextFaint),
  ```
- **`Cmds_Button8.cs:1040-1047`** — live, dynamic breadcrumb:
  ```csharp
  Text = $"  — {processed} of {assembliesFound} assemblies processed · Total weight: {totalWeightSum:F3} kg",
  FontSize = 12,
  Foreground = AvTokens.B(AvTokens.TextFaint),
  ```

Both remaining occurrences show live progress/weight data, not static filler — same category as the two Pattern B instances already deliberately left alone during the header-styling task.

### 3. Phase 3 StatusPill/StatTile/button-technique reconciliation backlog

Source: the 7 "Migrate `Cmds_*.cs` to AVES.Core design tokens/controls" commit messages (full detail lives there, not in a separate doc). `Cmds_Button7.cs` (`8c7ba25`) routed cleanly with no flagged discrepancies — the backlog is actually 6 files:

- **`Cmds_PullDown.cs`** (`e8b95f7`) — `StatusPill` stays local: "Cache mismatch" wording + differing dot size/padding/corner-radius/font-size vs `AvControls.StatusPill`.
- **`Cmds_PullDown2.cs`** (`ee43189`) — `StatusPill` stays local: "Completed with errors" wording (Core says "Warning") + outer `Border` never sets `VerticalAlignment` (Core's does).
- **`Cmds_Pulldown3.cs`** (`c1c29af`) — `StatTile`: label `FontSize` 10.5 vs Core's 11, value margin-bottom 3 vs 4, no `"warn"` case in the accent-color switch (dead code, unreachable). `StatusPill`: "fail" label reads "Error" vs Core's "Failed" (geometry otherwise matches). Button trio: `GhostButton` padding hardcoded to `(16,9,16,9)` vs Core's `(14,9,14,9)`.
- **`Cmds_Pulldown4.cs`** (`d674c2f`) — `StatusPill`: "warn" label reads "Warn" (a third distinct wording variant) — confirmed **live**, not dead code — + missing `VerticalAlignment` on the outer `Border`.
- **`Cmds_Button6.cs`** (`c1e0691`) — `StatusPill`: different input domain entirely (`"Renamed"/"Skipped"/"Error"/"Already Correct"` vs Core's `ok/skip/fail/warn`), dot margin 5 vs 6, padding `(8,4,10,4)` vs `(8,5,10,5)`, extra outer `Margin(12,0,12,0)`. `StatTile`: value margin-bottom 3 vs 4, no `TextTrimming`/`ToolTip`, extra outer `Margin(0,0,10,0)`. `BrandTile`: decode sets `DecodePixelHeight`/`SnapsToDevicePixels`/`UseLayoutRounding`, none of which Core's plain `BrandTile` does. Buttons: a 4th distinct technique (`SetBinding` to `TemplatedParent.Background` instead of baking color into the template), padding `(20,8,20,8)`/`(16,8,16,8)` vs Core's uniform `(16,9,16,9)`, extra `MinWidth=80`.
- **`Cmds_Button9.cs`** (`5b8fcd3`) — `StatusPillConverter`: closest match to Core found (only "Exported" vs Core's "Done" differs), but architecturally an `IValueConverter`, not a static factory method. `BuildHeaderBar`: `Background` is `Surface` not `Surface2`, plus an extra timestamp column Core's shape doesn't have. `BuildButton`: a 5th distinct technique — every kind (including Primary/Ghost, which have no border in any Core family) gets an explicit border, uniform padding `(16,9,16,9)` for all kinds including Ghost (Core's Ghost uses `(14,9,14,9)`). `BuildStatTile`: no `"warn"` case at all (unreachable), label `FontSize` 10.5 vs 11, value margin-bottom 3 vs 4, no `TextTrimming`/`ToolTip`. `BuildLogoImage`: sets `DecodePixelHeight`, unique try/catch fallback pattern.

Net: 3 wording variants of the "warn" pill label alone ("Cache mismatch", "Completed with errors", "Warn"), plus 5 distinct button-construction techniques across the whole plugin (Core's 3 families + Button6's SetBinding technique + Button9's uniform-border technique). No reconciliation decision has been made on any of these yet.

### 4. `Cmds_Pulldown3.cs` uncommitted diff — now identified

Not the breadcrumb-clearing pattern. It's a title-text rename, isolated to 4 lines, all in the Vendor auto-dimension flow:

```diff
-                    "Vendor Dimension — Overall H×W×D", "Vendor");
+                    "Auto-Dimension Vendor H×W×D", "Vendor");
```
(appears 4 times: `ShowAssemblyPicker` call, `ShowDimensionTypePicker` call, and twice in `ShowLogWindow` title args)

Renames "Vendor Dimension — Overall H×W×D" to "Auto-Dimension Vendor H×W×D" — looks like a deliberate naming-consistency pass (matches the "Auto-Dimension MT Views" naming already used by the sibling MT flow), not accidental drift. Still uncommitted. Not part of today's work; nobody has acted on it.

### 5. Full `git status` snapshot (untracked/uncommitted, outside today's work)

```
 M AVES.slnx
 M AVES/AVES_Installer.iss
 D AVES/Commands/General/Cmds_Button.cs
 M AVES/Commands/General/Cmds_Pulldown3.cs
 D AVES/Resources/Icons16/Button_Button16.png
 M AVES/Resources/Icons16/PullDown16.png
 D AVES/Resources/Icons16/PullDown_1Button16.png
 D AVES/Resources/Icons16/PullDown_2Button16.png
 D AVES/Resources/Icons16/PullDown_3Button16.png
 D AVES/Resources/Icons32/Button_Button32.png
 M AVES/Resources/Icons32/PullDown32.png
 D AVES/Resources/Icons32/PullDown_1Button32.png
 D AVES/Resources/Icons32/PullDown_2Button32.png
 D AVES/Resources/Icons32/PullDown_3Button32.png
 D AVES/installer_output/AVES_Setup_v1.0.0.exe
?? "AVES/Resources/Hilti Logo/"
?? AVES/Resources/Icons16/Button10_Button16.png
?? AVES/Resources/Icons16/Button11_Button16.png
?? AVES/Resources/Icons16/Button6_Button16.png
?? AVES/Resources/Icons16/Button7_Button16.png
?? AVES/Resources/Icons16/Button8_Button16.png
?? AVES/Resources/Icons16/Button9_Button16.png
?? AVES/Resources/Icons16/PullDownAutoDimension16.png
?? AVES/Resources/Icons16/PullDownAutoDimension_MTQDrawings16.png
?? AVES/Resources/Icons16/PullDownAutoDimension_VendorDrawings16.png
?? AVES/Resources/Icons16/PullDownCreateSheets16.png
?? AVES/Resources/Icons16/PullDownCreateSheets_AssemblyDrawings16.png
?? AVES/Resources/Icons16/PullDownCreateSheets_MTQDrawings16.png
?? AVES/Resources/Icons16/PullDownCreateSheets_VendorDrawings16.png
?? AVES/Resources/Icons16/PullDown_BatchRemoveParams16.png
?? AVES/Resources/Icons16/PullDown_BatchSharedParams16.png
?? AVES/Resources/Icons16/PullDown_CsvImporter16.png
?? AVES/Resources/Icons32/Button10_Button32.png
?? AVES/Resources/Icons32/Button11_Button32.png
?? AVES/Resources/Icons32/Button6_Button32.png
?? AVES/Resources/Icons32/Button7_Button32.png
?? AVES/Resources/Icons32/Button8_Button32.png
?? AVES/Resources/Icons32/Button9_Button32.png
?? AVES/Resources/Icons32/PullDownAutoDimension32.png
?? AVES/Resources/Icons32/PullDownAutoDimension_MTDimensions32.png
?? AVES/Resources/Icons32/PullDownAutoDimension_VendorDimensions32.png
?? AVES/Resources/Icons32/PullDownAutoTag32.png
?? AVES/Resources/Icons32/PullDownAutoTag_MTTag32.png
?? AVES/Resources/Icons32/PullDownAutoTag_VendorTag32.png
?? AVES/Resources/Icons32/PullDownCreateSheets32.png
?? AVES/Resources/Icons32/PullDownCreateSheets_AssemblyDrawings32.png
?? AVES/Resources/Icons32/PullDownCreateSheets_MTDrawings32.png
?? AVES/Resources/Icons32/PullDownCreateSheets_VendorDrawings32.png
?? AVES/Resources/Icons32/PullDown_BatchRemoveParams32.png
?? AVES/Resources/Icons32/PullDown_BatchSharedParams32.png
?? AVES/Resources/Icons32/PullDown_CsvImporter32.png
?? AVES/installer_output/AVES_Setup_v1.0.1.exe
```

Note: some untracked icon filenames still say `MTQ` (e.g. `PullDownAutoDimension_MTQDrawings16.png`) while others already say `MT` (e.g. `PullDownAutoDimension_MTDimensions32.png`) — inconsistent, not addressed by today's MTQ->MT rename since icon assets were out of scope for that task.
