# Snapdate — Design

> Status: **Draft for approval** · Applies to v0.1.0 · Mockup: [`design/mockup/`](design/mockup/)

## 1. Design principles

1. **One screen, three steps.** Folders → Name → Go. No wizards, no hidden menus.
2. **Show, don't tell.** The user always sees a real example of the resulting
   file name and a preview list before anything happens.
3. **Safe by default.** Copy, never overwrite, clear wording. Destructive
   choices (move) are visually secondary and explained.
4. **Friendly, not childish.** Warm gradient brand, generous spacing, soft
   shadows, rounded corners, calm motion.
5. **Bilingual from day one.** Every string in resources; layouts tolerate
   ~30 % longer German text.

## 2. Brand

**Name:** Snapdate — *snap* (photo) + *date*. Works in English and German.

**Logo / icon:** rounded square ("squircle") with the brand gradient; inside a
white photo card (mountain + sun) with a calendar dog-ear in the top-right
corner. Source: [`assets/branding/logo.svg`](assets/branding/logo.svg).
Icon sizes exported to `app.ico`: 16, 20, 24, 32, 40, 48, 64, 256 px. At ≤ 24 px
the mountain is dropped and only card + dog-ear remain.

**Brand gradient:** `#6D5DFC → #B44CF0 → #FF7A59` (135°).
Used only for: logo, primary button, progress bar, selected token chip glow.

## 3. Design tokens

### 3.1 Colour

| Token | Light | Dark | Use |
|---|---|---|---|
| `bg` | `#F6F5FB` | `#121119` | Window background |
| `surface` | `#FFFFFF` | `#1B1A26` | Cards, panels |
| `surface-2` | `#F0EEF9` | `#24233A` | Inputs, list hover, chip background |
| `border` | `#E4E1F0` | `#302E48` | 1 px separators |
| `text` | `#1B1A2E` | `#F2F1FA` | Primary text |
| `text-muted` | `#6B6880` | `#A3A0B8` | Secondary text, labels |
| `primary` | `#5B4BF5` | `#8B7DFF` | Links, focus ring, active states |
| `primary-soft` | `#ECE9FF` | `#2E2A5C` | Selected chip, info badge |
| `success` | `#1FA971` | `#3CCB8F` | "Camera date" badge, done state |
| `warning` | `#D98A0B` | `#F2B13C` | "File date" fallback badge, duplicates |
| `danger` | `#E5484D` | `#FF6B70` | Errors |

All text/background pairs meet **WCAG AA** (≥ 4.5:1 body, ≥ 3:1 large/UI).
Follows Windows light/dark setting; can be overridden in settings.

### 3.2 Typography

**Inter** (bundled via `Avalonia.Fonts.Inter`, OFL licence) — no reliance on
installed fonts. Monospace for file names: **JetBrains Mono** (OFL, bundled).

| Style | Size / line | Weight |
|---|---|---|
| Display (app name) | 18 / 24 | 700 |
| Title (card header) | 15 / 22 | 600 |
| Body | 13 / 20 | 400 |
| Label | 12 / 16 | 500, muted |
| Mono (file names) | 12.5 / 18 | 400 |
| Button | 14 / 20 | 600 |

### 3.3 Spacing, radius, elevation, motion

- Spacing scale (4-pt grid): 4, 8, 12, 16, 20, 24, 32.
- Radius: 6 (chips, badges), 10 (inputs, buttons), 16 (cards), 22 (logo).
- Elevation: cards `0 1 2 rgba(20,16,60,.04), 0 4 16 rgba(20,16,60,.06)`;
  dark mode uses border instead of shadow.
- Motion: 150 ms ease-out for hover/press, 250 ms for panel transitions; list
  items fade/slide in 8 px while scanning. Respects "reduce animations".

## 4. Layout

Window: default **1180 × 760**, minimum **960 × 640**. Custom title bar
(Avalonia `ExtendClientAreaToDecorationsHint`) keeping native min/max/close
and snap layouts.

```
┌──────────────────────────────────────────────────────────────────────────┐
│ [logo] Snapdate                              EN | DE   ☾   ─  □  ✕      │ 48 px title bar
├───────────────────────────────┬──────────────────────────────────────────┤
│ ① Folders                     │ Preview                     128 photos   │
│  Source  [📁 D:\Camera   ▸]   │ ● 121 camera date  ● 7 file date  2 dup  │
│  Target  [📁 E:\Sorted   ▸]   │ ┌──────────────────────────────────────┐ │
│  ☐ include sub-folders        │ │ [thumb] IMG_4711.JPG                 │ │
│                               │ │         → 2024-07-14_15-32-08.jpg  ● │ │
│ ② File name                   │ │ [thumb] DSC00012.ARW                 │ │
│  [{YYYY}-{MM}-{DD}_{hh}… ✎]   │ │         → 2024-07-14_16-01-55.arw  ● │ │
│  Year Month Day Hour Min …    │ │   …  (virtualised list)              │ │
│  Example: 2024-07-14_15-32…   │ │                                      │ │
│                               │ └──────────────────────────────────────┘ │
│ ③ Options                     │                                          │
│  (•) Copy  ( ) Move           │                                          │
│  Same name → add (2)          │                                          │
├───────────────────────────────┴──────────────────────────────────────────┤
│ ✓ Ready · originals stay untouched        [  Copy 128 photos  ➜ ]       │ 72 px action bar
└──────────────────────────────────────────────────────────────────────────┘
   left column 400 px fixed            right column fills
```

Below 1040 px width the left column shrinks to 360 px; text wraps, nothing is
hidden.

## 5. Components

### 5.1 Step card
Numbered circle (gradient when complete, outline when pending), title, content.
Completed steps show a small ✓.

### 5.2 Folder picker
Large row button: folder icon, label, path (middle-ellipsis:
`D:\…\Urlaub 2024`), chevron. Click opens the native folder dialog
(`StorageProvider.OpenFolderPickerAsync`). Whole card accepts drag & drop of a
folder; drop target highlights with dashed primary border.
Validation inline below the row (e.g. "Target is inside the source folder").

### 5.3 Pattern builder
- Text field (mono) showing the pattern; tokens rendered as **pills** inside
  the field (inline elements), literal text editable normally.
- Below: token **chip palette** grouped *Date* · *Time* · *File* · *Separators*.
  Click inserts at caret; drag & drop supported. Chips show friendly labels
  ("Year", "Jahr") with the raw token as tooltip.
- **Presets** dropdown: `2024-07-14_15-32-08`, `20240714_153208`,
  `2024/07/2024-07-14 …` (folders), `14.07.2024 15-32 – IMG_4711`.
- **Live example** line with the first file's real data, plus extension hint:
  "The file extension (.jpg, .png, …) is added automatically."
- Invalid characters typed by the user are shown with a red underline and a
  plain-language explanation.

### 5.4 Preview list
Virtualised list. Row: 40 px thumbnail (rounded 8), original name (muted),
arrow, new name (mono, bold), date-source badge:
- green **Camera** (metadata), amber **File date** (fallback), amber
  **Renamed (2)** (collision), red **Skipped** (error, tooltip with reason).
Filter tabs above the list: All · Camera date · File date · Problems.
Empty state: illustration + "Choose a source folder to see your photos here".

### 5.5 Action bar
Left: status sentence. Right: primary gradient button with count
("Copy 128 photos" / "Move 128 photos"). While running the button turns into a
progress bar with "Cancel"; on completion a success card slides up:
"128 photos copied · Open folder · Show log".

### 5.6 Title bar controls
Language segmented control `EN | DE`, theme toggle, About (version, licences,
"no data leaves this PC").

## 6. Localisation

- Auto: Windows display language `de-*` → German, otherwise English.
- Month/weekday tokens use the selected UI language.
- Key wording (EN / DE): Source / Quelle · Destination / Ziel ·
  File name / Dateiname · Copy / Kopieren · Move / Verschieben ·
  Camera date / Aufnahmedatum · File date / Dateidatum.

## 7. Accessibility

- Full keyboard path (Tab order follows steps 1→2→3→action), visible 2 px focus ring.
- Every control has an automation name (screen readers / UIA).
- Status is never colour-only: badges carry text + icon.
- Minimum hit target 32 px; text scales with Windows text-size setting.
