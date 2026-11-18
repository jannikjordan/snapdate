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
| `primary` | `#5B4BF5` | `#8B7DFF` | Links, focus ring, active states, "Date from name" filter dot |
| `primary-soft` | `#ECE9FF` | `#2E2A5C` | Selected chip, "Date from name" (info) badge background |
| `info` | `#5B4BF5` | `#A99EFF` | "Date from name" badge text (4.69:1 light, 5.63:1 dark on `primary-soft`, which is also the selected-row background) |
| `info-border` | `#7B6EF6` | `#7A6EE6` | 1 px border of the "Date from name" badge, so the pill stays visible on a selected row (3.27:1 light, 3.25:1 dark on `primary-soft`) |
| `success` | `#1FA971` | `#3CCB8F` | "Camera date" badge, done state |
| `warning` | `#D98A0B` | `#F2B13C` | "File date" fallback badge, duplicates, "Dates differ" hint |
| `danger` | `#E5484D` | `#FF6B70` | Errors |

All text/background pairs meet **WCAG AA** (≥ 4.5:1 body, ≥ 3:1 large/UI).
Follows Windows light/dark setting; can be overridden in settings.

### 3.2 Typography

**Inter** (bundled via `Avalonia.Fonts.Inter`, OFL licence) — no reliance on
installed fonts. Monospace for file names: **Cascadia Mono** (ships with Windows 11), falling back to Consolas — nothing extra to bundle.

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

Window: default **1180 × 780**, minimum **980 × 720** (fits 1366 × 768 laptops). Custom title bar
(Avalonia `ExtendClientAreaToDecorationsHint`) keeping native min/max/close
and snap layouts. The bar is marked `WindowDecorationProperties.ElementRole="TitleBar"` (Windows
reports it as caption: drag, double-click, snap layouts); the language, theme, help and About controls
are marked `ElementRole="User"` so they stay clickable. Avalonia draws the caption buttons itself but they
report native hit-test codes, so snap layouts work. Avalonia also draws the window title over the bar;
`App.axaml` hides it (`Panel#PART_TitleTextPanel`, opacity 0) while `Title` stays for Alt-Tab, taskbar and screen readers.
There is **no fullscreen mode**: Avalonia's decorations add a fullscreen button left of minimize (it would cover the About
button, and fullscreen hides the caption buttons). `App.axaml` sets `IsVisible=False` on `PART_FullScreenButton` and
`PART_PopoverFullScreenButton`, and `MainWindow` reverts any `WindowState.FullScreen`. The three caption buttons are
46 dip wide each (138 dip), inside the 150 dip column reserved at the right end of the bar.

**Rule: the left column must fit without scrolling at the default and the minimum size**
(guarded by UI tests in both languages). Long names are shortened with “…”
and shown in full as a tooltip. Step 3 lives in the action bar, next to the button it
affects; secondary helpers (hint text, quick picks, extension note) are
tooltips or flyouts.

```
┌──────────────────────────────────────────────────────────────────────────┐
│ [logo] Snapdate v0.1.0                 EN | DE   ☾   ?   ⓘ   ─  □  ✕     │ 48 px title bar
├────────────────────────────────┬─────────────────────────────────────────┤
│ ① Choose folders               │ Preview                      12 photos  │
│  [📁 Photos from   D:\…   ▸]   │ All 12 · ● Camera 8 · ● Name 2 · ● File 2 │
│  [📁 Save to       E:\…   ▸]   │ ┌─────────────────────────────────────┐ │
│  ☑ Include sub-folders         │ │ [thumb] IMG_4711.JPG                │ │
│                                │ │         → 2024-07-14_09-12-33.jpg ● │ │
│ ② Build the name ⓘ [07] Quick picks ▾ Reset                             │
│  [{YYYY}-{MM}-{DD}_{HH}…   ]   │ │ [thumb] DSC00013.JPG                │ │
│  (Year 2024✕)(-✕)(Month 07✕)…  │ │         → …_16-01-55 (2).jpg  Numb. │ │
│  Date  Year Month Day Name Wd  │ │   …  (virtualised list)             │ │
│  Time  Hour Minute Second      │ │                                     │ │
│  File  Original Camera Counter │ │                                     │ │
│  Sep.  - _ . Space NewFolder   │ │                                     │ │
│  Example  2024-07-14_09-12….jpg│ └─────────────────────────────────────┘ │
├────────────────────────────────┴─────────────────────────────────────────┤
│ ✓ Ready: 12 photos               ③ [Copy|Move]   [ Copy 12 photos ➜ ]   │ 68 px action bar
└──────────────────────────────────────────────────────────────────────────┘
   left column 470 px fixed             right column fills
```


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
  the field (inline elements). **Free text can be typed anywhere** between
  pills — spaces, dashes, words — and is kept literally
  (`{YYYY}-{MM}-{DD} {HH}{mm}` → `2024-07-14 1532`).
- Below: token **chip palette** grouped *Date* · *Time* · *File* · *Separators*.
  Click inserts at caret; drag & drop supported. Chips show friendly labels
  ("Year", "Jahr") with the raw token as tooltip.
- A **"Leading zero (07 instead of 7)"** switch converts all date/time blocks
  (`{MM}`/`{M}`, `{DD}`/`{D}`, `{HH}`/`{H}`, `{mm}`/`{m}`, `{ss}`/`{s}`).
- **Clicking a pill switches its form**: `07 ⇄ 7`, `2024 ⇄ 24`, `Jul ⇄ July`,
  `Sun ⇄ Sunday`; the ✕ removes it. This keeps the chip palette short.
- Implementation note: the text box holds the pattern text; pills are a
  live, clickable rendering of the parsed pattern below it.
- **Presets**: `2024-07-14_15-32-08`, `20240714_153208`,
  `2024-07-14 1532`, `2024/07/2024-07-14 …` (folders), `14.07.2024 – IMG_4711`.
- **Live example** line with the first file's real data, plus extension hint:
  "The file extension (.jpg, .png, …) is added automatically."
- Invalid characters typed by the user are shown with a red underline and a
  plain-language explanation.

### 5.4 Preview list
Virtualised list. Row: 40 px thumbnail (rounded 8), original name (muted),
arrow, new name (mono, bold), date-source badge:
- green **Camera date** (metadata), primary/info **Date from name** (read from the file name by a date rule;
  `Border.badge.info`, `primary-soft` background, `info` text, 1 px `info-border`; tooltip names the rule), amber **File date**
  (fallback), amber **Numbered** (collision), red **Skipped** (error, tooltip with reason).
- **Dates differ**: when metadata and file name give different days, a small hint sits under the badge: warning icon +
  "Dates differ" / "Daten weichen ab" in `warning` (never colour alone). Its tooltip names both dates, which one was
  used and why ("The name says …, the photo's metadata says … (written by an app, not a camera). The name was used." /
  "The camera date (…) was used; the name says …"). The row's automation name includes badge and hint.
Filter tabs above the list (wrap onto a second line when narrow): All · ● Camera date · ● Date from name (primary dot)
· ● File date · ● Numbered. Every photo is in exactly one of the three date-source tabs, so their counts add up to All.
Empty state: illustration + "Choose a source folder to see your photos here".

### 5.5 Action bar
Left: status sentence. Right: primary gradient button with count
("Copy 128 photos" / "Move 128 photos"). While running the button turns into a
progress bar with "Cancel"; on completion a success card slides up:
"128 photos copied · Open folder · Show log".

### 5.6 Title bar controls
Language segmented control `EN | DE`, theme toggle, About (version, licences,
"no data leaves this PC").

### 5.7 Update banner and notice
Slim strip (`Border.banner`: `SdPrimarySoft` background, 1 px `SdBorder` bottom line, padding 20/8)
directly under the title bar; it pushes the content down and never overlays it. The update banner
shows a refresh icon, "Snapdate X is available" with "You are using version Y." below, and buttons
(34 px high): installed copies "Update now" (primary) + "What's new" (secondary), portable copies
"Download" (primary); "Later" and "Skip this version" are link buttons. The one-time notice uses the
same strip with an info icon, one line of text and a "Got it" link button. Both are live regions with
automation names. About gets a divider, the "Check for updates automatically" checkbox and a
"Check now" secondary button with a short inline result next to it.

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
