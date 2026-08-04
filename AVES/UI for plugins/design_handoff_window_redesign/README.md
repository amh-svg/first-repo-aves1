# Handoff: AVES WPF Window Redesign — "Refined Native"

## Overview
A visual refresh of the AVES add-in's WPF windows — the shared dialogs behind
**Batch Shared Parameters**, **Batch Remove Parameters**, and **CSV Importer**.
The *UX and flow stay exactly as they are today.* This is a **look-and-feel pass**:
consistent tokens, cleaner chrome, real status badges instead of emoji, and a
polished report. The approved direction is **Refined Native** with the **AVES Red**
(`#E64A2E`) accent and **inline settings controls**.

## About the design files
The files in this bundle (`AVES Refined Native.html` + its `.css`/`.jsx`) are
**design references built in HTML** — they show the intended look and spacing, not
production code to copy. Your job is to **recreate them in the existing C#/WPF
add-in**, reusing its patterns. The current windows are built **programmatically in
C#** (no XAML views) inside `AVES/Commands/General/Cmds_Button.cs` — keep that
approach. Two starter files are included to make it fast:

- **`AvesTheme.xaml`** — a `ResourceDictionary` of colour/brush/font tokens plus
  ready styles for buttons and the DataGrid. Merge once at startup, apply by key.
- **`AvesUi.cs`** — C# factory methods (matching the existing `UiHelpers` class) for
  the composite widgets that are easier to build in code: status pill, stat tile,
  chip, brand tile, number badge.

## Fidelity
**High-fidelity.** Colours, type sizes, spacing, and radii below are final — match
them. The only deliberately-open item is the **brand mark**: the design uses a
neutral accent tile as a *placeholder*, because the imported bird icon is not the
company's real logo. Drop in the real logo when available.

---

## Design tokens

### Colour (Refined Native · AVES Red)
| Token | Hex | Use |
|---|---|---|
| Accent | `#E64A2E` | primary buttons, progress fill, brand tile, focus, selection |
| Accent deep | `#C2402A` | primary-button hover |
| Accent tint | `#FBE9E4` | selection bg, chips, number badge, focus ring |
| Backdrop | `#F4F4F6` | area behind a window's content (if custom chrome) |
| Surface | `#FFFFFF` | window body, cards, rows |
| Surface-2 | `#FAFAFB` | footers, header strips, inset panels, table header |
| Zebra | `#F7F8FA` | DataGrid alternating row |
| Border | `#E5E6EA` | hairlines, card/table borders |
| Border strong | `#D4D6DC` | input borders, table header underline |
| Text | `#1F2329` | primary text |
| Text dim | `#5A6069` | labels, secondary text |
| Text faint | `#8B9099` | section labels, hints, captions, em-dashes |

### Status colours — **semantic, never recoloured by the accent**
| Kind | Label | Bg | Fg | Replaces emoji |
|---|---|---|---|---|
| ok | Done | `#E4F3EA` | `#2C7A52` | ✅ |
| skip | Skipped | `#EDEFF2` | `#5B6573` | ⏭ |
| fail | Failed | `#FBE3DE` | `#C0381F` | ❌ |
| warn | Cache mismatch | `#FAF0DA` | `#9A6B12` | ⚠ |
| info | Info | `#E7EFFB` | `#2E66A8` | — |

### Typography
- **UI font:** Segoe UI (native). **Mono (numerics/IDs):** Consolas native fallback
  (design used IBM Plex Mono — bundle it only if you want the exact glyphs).
- Title-bar title **13 / SemiBold**; window sub-label 12 faint.
- Section label **11 / Bold**, UPPERCASE, faint. (WPF has no letter-spacing — just
  upper-case the string; skip tracking.)
- Field label **12.5 / SemiBold**, dim. Control text **13**. Button **13 / SemiBold**.
- Pill **11.5 / SemiBold**. Stat value **22 / Bold mono**; stat caption **11 / SemiBold** faint.
- Table header **10.5 / Bold** faint; cell **12.5**; numeric cell **11.5 mono** dim; note **11.5** faint.

### Radii / spacing / elevation
- Corner radius: **8** windows/cards · **5** buttons, inputs, tiles, pills(6) · **9** number badge (circle).
- Window body padding **20**. Footer padding **14 / 20**. Field vertical gap **16**. Button gap **10**.
- DataGrid row height **42**, cell padding **12 / 11**.
- Window shadow (only if custom chrome): `0 1px 2px rgba(20,22,28,.06), 0 8px 24px rgba(20,22,28,.10)`.

---

## Window chrome (two options)
Every window gets a 44px-high title bar in the mock: white bg, 1px bottom border
(`#E5E6EA`), a 20px **brand tile** + title (13/SemiBold) on the left.

- **Option A — low risk (recommended first):** keep the native OS window frame
  (`WindowStyle=SingleBorderWindow`), and apply all *interior* styling (body bg
  `#FFFFFF`, the token-styled controls, the footer strip). You skip the custom
  title bar; everything else still lands.
- **Option B — high fidelity:** `WindowStyle=None` + `WindowChrome` to draw the
  custom 44px title bar with the brand tile. More work; do it once in a shared base
  `Window` subclass and reuse across all dialogs.

Either way: set every window `Background="{StaticResource AvesSurface}"`, `FontFamily`
to Segoe UI, and `UseLayoutRounding="True"`.

---

## Screens / Views

### 1. Settings — inline controls  *(`SharedParamSettingsWindow`, `RemoveSettingsWindow`)*
**Replace the numbered double-click `ListBox`** with real, always-visible controls
laid out top-to-bottom in the 20px-padded body. Each row: a **number badge**
(`AvesUi.NumberBadge`) + a 12.5/SemiBold dim label, then the control beneath.

For **Batch Shared Parameters** (5 fields):
1. **Shared parameter group** → `ComboBox` (styled, full width).
2. **Parameters to inject** → a wrap-panel of **chips** (`AvesUi.Chip`, mono text)
   plus a dashed `+ Add` chip that opens the existing `MultiPickerWindow`.
3. **Target Revit group** → `ComboBox` *(side-by-side with #4 in a 2-col grid)*.
4. **Mode / category** → `ComboBox`.
5. **Parameter scope** → **segmented toggle** (Instance / Type): a 1px-bordered
   `Surface-2` container, 3px padding; the selected segment is a white `Border`
   (radius 3) with a soft shadow; unselected is dim text. Two `RadioButton`s
   re-templated, or two `ToggleButton`s with a shared group flag.

Footer (`Surface-2`, top border): right-aligned **Cancel** (`AvesGhostButton`) +
**▶ Run script** (`AvesPrimaryButton`).

> ComboBox styling: give it the same look as the mock's `.control` — white bg, 1px
> `BorderStrong`, radius 5, padding 10/12, a faint chevron. Re-template `ComboBox`
> (ToggleButton part) once in `AvesTheme.xaml` if you have time; otherwise a close
> approximation with `Padding`/`BorderBrush` is fine for Option A.

The **Remove** settings window is the same pattern with 2 fields (Parameters →
chips/picker, Mode → ComboBox) and a **▶ Run removal** primary button.

### 2. Multi-select picker  *(`MultiPickerWindow`)*
- Title bar "Select parameters to inject".
- Body: a **search/filter box** at top (`Surface-2`, 1px border, radius 5, magnifier
  glyph + placeholder), then a bordered list.
- Each item is a **checkbox row** (40px tall): a 17px rounded-5 box (unchecked = 1.5px
  `BorderStrong`; checked = accent fill + white check) and the param name in **mono 12**.
- Footer: left = "`N selected`" (12/SemiBold dim); right = **Cancel** (ghost) + **Add
  parameters** (primary). Keep Ctrl/Shift multi-select behaviour.

### 3. Progress  *(`BatchProgressWindow`)*
- Title bar "Processing families".
- Body (centered, ~14px gaps): a status row — left `Processing: <family>` (family in
  mono 12), right `12 / 20` (mono 12 dim); then the **progress bar**; then a faint
  caption (e.g. `Adding 4 parameters · 8 families remaining`) on the left and a small
  **Cancel** ghost button on the right.
- Progress bar: height **10**, fully rounded (radius 999), track `#E5E6EA`, fill a
  left-to-right gradient from `#E64A2E` to a lighter coral (`~#FF9E7A`). Re-template
  `ProgressBar` (the `PART_Indicator` border) for rounded + gradient.

### 4. Unified Report  *(`UnifiedReportWindow`)* — the hero
Body has three zones, then a footer.

**A · Run summary (top).** Two columns:
- Left (fills): a **3-column grid of stat tiles** (`AvesUi.StatTile`). Map the existing
  summary numbers: Processed (`ok`), Skipped (default), Failed (`fail`), Params added
  (`accent`), Already existed (default), Cache mismatch (`warn`). The current
  `ReportSummaryRow` Setting/Value DataGrid is **replaced** by these tiles.
- Right (fixed ~250px): a **Configuration panel** — `Surface-2` bg, 1px border, radius 5;
  key/value rows with a dashed bottom hairline, key dim, value **mono**. Keep it short
  (Parameter group, Mode, Scope, Families found).

**B · Results per family.** A section label row ("Results per family" + a faint
"showing N of M" on the right), then the **styled `DataGrid`** (apply `AvesDataGrid`,
`AvesColumnHeader`, `AvesRow`, `AvesCell`). Columns: Family name (SemiBold) · **Status**
· Added · Existed · Added parameters · Notes.
- **Status column** = a `DataGridTemplateColumn` rendering `AvesUi.StatusPill(kind)`
  instead of an emoji string. Add a `StatusKind` ("ok"/"skip"/"fail"/"warn") to the row
  models, or convert at render time via `AvesUi.PillKindFromGlyph(row.Status)`.
- Numeric columns (Added/Existed): mono 11.5, dim, centered; render `—` in
  `BorderStrong` colour when empty.
- Added parameters: mono 11, dim. Notes: 11.5 faint; **amber** (`#9A6B12`) when it's a
  cache-mismatch note.

**C · Footer.** Left: faint source caption (e.g. `Source · MEKANOS.txt`). Right:
**⧉ Copy to clipboard** (`AvesSecondaryButton`, keep the existing TSV clipboard logic
and the "Copied!" confirmation) + **Close** (`AvesPrimaryButton`).

> The same report shell serves all three commands — only the columns differ
> (`AddParamReportRow`, `RemoveParamReportRow`, `CsvItemReportRow`). Style the shell
> once; pass column defs as today. The CSV importer's **Warnings** section becomes a
> second small DataGrid with the same row styling.

---

## Interactions & behaviour
No behaviour changes. Preserve: validation loop (re-show settings if no params
chosen), Cancel→`DialogResult=false`, progress cancel flag, double-click confirm on
pickers, clipboard export + 2s "Copied!" revert, DataGrid sorting. Hover states are
the only additions — see the button templates in `AvesTheme.xaml`.

## State management
None new. The settings POCOs (`SharedParamSettings`, `RemoveSettings`) and report
row models are unchanged except for an optional `StatusKind` string on report rows to
drive the pill column.

## Assets
- **Brand tile** — placeholder only (`AvesUi.BrandTile`, an accent rounded-square +
  white dot). Replace with the company's real logo (ideally a vector/`DrawingImage`,
  or 16/20/32px PNGs) when supplied.
- No other raster assets; all glyphs (chevron, check, magnifier, dot) are drawn with
  `Path`/`Ellipse` or simple text.

## Files in this bundle
- `AVES Refined Native.html` — the approved design reference (open in a browser;
  toggling palettes is just exploration — ship **AVES Red**).
- `aves-themes.css` — source of every token/measurement (see the `.v-native` block
  and the `:root` status palette).
- `aves-windows.jsx`, `aves-report.jsx` — component markup for each window.
- `tweaks-panel.jsx` — only needed to run the HTML locally; not part of the WPF work.
- `AvesTheme.xaml`, `AvesUi.cs` — **starter implementation files** for the add-in.

## Suggested order
1. Add `AvesTheme.xaml` + `AvesUi.cs`; merge the dictionary at startup.
2. Apply button + DataGrid styles in `UnifiedReportWindow`; swap summary to stat
   tiles + config panel; add the status-pill column. (Biggest visual win.)
3. Restyle `MultiPickerWindow` and `BatchProgressWindow`.
4. Rebuild the two settings windows as inline controls.
5. (Optional) Option-B custom title bar via a shared base `Window`.
