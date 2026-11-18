# ADR 0007 — Dates from file names: rule format, conflict rule, storage

- Status: accepted
- Date: 2026-11-18
- Related: [ADR 0003](0003-single-file-instead-of-native-aot.md) (single-file exe), [ADR 0006](0006-inno-setup-installer.md) (installer; the settings folder is never touched by install or upgrade). Epic #13, issues #14–#23 (regex mode: #21).

## Context
WhatsApp removes EXIF from pictures it sends. Snapdate then falls back to the
file timestamp, which is the copy or download time, so the photos are renamed
with the wrong date. The date is often still in the file name:

- **WhatsApp on Android** saves received media as `IMG-YYYYMMDD-WA####.jpg` /
  `VID-YYYYMMDD-WA####.mp4`. In a sample of 7 such files, 3 had no metadata at
  all. 4 had an EXIF `DateTimeOriginal` written later by some app (no camera
  make/model; IFD0 `DateTime` = export day): 3 of them are on the day in the
  file name, 1 contradicts it (`IMG-20160706-WA0002.jpg` →
  `DateTimeOriginal` 2026-06-23 08:34:32). Only the file names of the sample
  are used in tests and docs; no photos are committed.
- **WhatsApp Desktop / Web** saves names like
  `WhatsApp Image 2024-07-14 at 15.32.08.jpeg`, possibly localised
  (`WhatsApp Bild … um …`) and with suffixes (`_1a2b3c4d`, ` (1)`).
- **iPhone** saves `IMG_4200.jpg` with no date anywhere. This is out of scope;
  the user guides suggest sending photos "as document" instead.

Other apps (Signal, Telegram, `IMG_YYYYMMDD_HHMMSS`, Pixel `PXL_…` in UTC) use
similar schemes, so the solution must be a **generic, rule-based parser**, not
WhatsApp-specific code. Rules ship with the app (*system rules*, maintained by
us) and users can add their own (*custom rules*, never overwritten by
updates). The audience is non-technical.

## Decision

### Rule format: token pattern
A rule is "the name builder in reverse": a pattern made of the same kind of
placeholders the user already knows from the file-name builder, for example
`IMG-{YYYY}{MM}{DD}-WA*`.

| Element | Meaning | Compiled to |
|---|---|---|
| `{YYYY}` | 4-digit year | `[0-9]{4}` |
| `{YY}` | 2-digit year (see pivot below) | `[0-9]{2}` |
| `{MM}` `{DD}` `{HH}` `{mm}` `{ss}` | 2-digit month, day, hour, minute, second | `[0-9]{2}` |
| `{M}` `{D}` `{H}` `{m}` `{s}` | 1–2 digits | `[0-9]{1,2}` |
| `*` | any text (also empty) | `.*?` |
| `?` | exactly one character | `.` |
| `{{` / `}}` | literal `{` / `}` | escaped |
| anything else | literal, case-insensitive | `Regex.Escape` |

- `*` and `?` cannot occur in Windows file names, so the wildcards never clash
  with literal text.
- The pattern must match the **whole** file name without extension (`^…$`).
- A rule has **1–10 patterns** (one per line in the editor); the first pattern
  that yields a valid date wins.
- Validation (errors are values with a character position, never exceptions):
  empty; longer than 200 characters; unclosed brace; a lone `}`
  (`UnexpectedClosingBrace`); unknown token; unsupported token (e.g. `{MMM}`,
  `{name}`); a token used twice; year, month or day missing; hour and minute
  must appear together (neither `{HH}` without `{mm}` nor `{mm}` without
  `{HH}`; error kinds `HourWithoutMinute`, `MinuteWithoutHour`); seconds
  require minutes (`SecondWithoutMinute`); ambiguous neighbours
  (`AmbiguousNeighbours`, see below); an expression that fails to build
  (`InvalidExpression`, used by regex mode, see below). A rule with one
  invalid pattern still uses its valid ones.
- **Ambiguity rule (runs).** A *run* is a sequence of tokens that may also
  include `?` and literal digit characters (`0`–`9`), because both behave like
  fixed-width digits. A run ends only at a literal non-digit, at `*`, or at the
  start or end of the pattern. Each run may contain **at most one
  variable-width token** (`{M}`, `{D}`, `{H}`, `{m}`, `{s}`), and a run that
  contains one must not touch `*`. In addition, a `?` must not sit directly
  next to a variable-width token (stricter than the run rule, but harmless).
  Fixed-width tokens may touch `*`. Violations are reported as
  `AmbiguousNeighbours`.
  - Valid: `WhatsApp * {YYYY}-{MM}-{DD} * {HH}.{mm}.{ss}*`,
    `{YYYY}{M}{DD}-*`.
  - Invalid: `{YYYY}{M}{D}` (two variable-width tokens in one run),
    `{YYYY}-{M}1{D}` (the digit `1` joins `{M}` and `{D}` into one run),
    `*{D}.{MM}.{YYYY}` and `{YYYY}{M}{DD}?*` (a run with a variable-width
    token touches `*`), `{YYYY}-{M}?-{DD}` (a `?` directly next to `{M}`).
- Each pattern is compiled once to a `Regex` with
  `NonBacktracking | IgnoreCase | CultureInvariant | ExplicitCapture`, a
  **50 ms match timeout** (a timeout counts as no match) and no
  `RegexOptions.Compiled`. Digits are `[0-9]` (never `\d`, which matches
  non-ASCII digits). Names longer than 260 characters are not matched.
- Values are parsed with `NumberStyles.None` and the invariant culture. A
  match only counts if it forms a real calendar date and time (month 1–12, day
  within the month, hour 0–23, minute and second 0–59) and passes the
  **plausibility check shared with the metadata reader** (`DatePlausibility`:
  not before 1900-01-01, not more than 2 days in the future). Otherwise the
  next pattern or rule is tried.
- **Two-digit year pivot:** `{YY}` means 20YY, or 19YY when 20YY is after the
  current year (whole years are compared, not full dates).

**Advanced mode: regular expression.** A rule can set `"syntax": "regex"`
instead of the default `"pattern"`. The user's expression is wrapped as
`^(?:…)$`, limited to 300 characters, compiled with the same options, and
must use the named groups `year`, `month`, `day` (required) and `hour`,
`minute`, `second` (optional); no other group names. Counted repetition is
capped (`{n}` / `{n,m}` with n, m ≤ 100). **Any failure while building the
regex** — constructs that `NonBacktracking` does not support
(backreferences, lookarounds, atomic groups; `NotSupportedException`), syntax
errors (`ArgumentException`), a too-large repetition or anything else — becomes
a plain-language validation error (`InvalidExpression`), never a crash. The 50 ms timeout applies to
matching, as for patterns. Group values go through the
same ASCII-digit, calendar and plausibility checks. Implemented in #21 in the
same release (v0.3.0); until then such rules are kept but inactive.

### Date priority and conflicts
1. **Camera EXIF** (metadata from a file with a camera model) always wins, as
   before. If a file-name date is on a different day, the photo is flagged
   **"Dates differ"** and the ignored file-name date is shown.
2. Otherwise a **file-name date** wins over **app-written metadata** and over
   the file timestamp. App-written metadata is any metadata date without a
   camera model — e.g. written by a gallery or backup app, and also QuickTime,
   XMP or PNG dates without a camera model, which matters for `VID-…-WA`
   videos.
3. **Same day** (file-name date and app-written metadata): the metadata date is
   kept unchanged; the metadata time wins.
4. **Different days:** the file-name date is used with the new source
   `DateSource.FileName`, the photo is flagged **"Dates differ"** and the
   ignored metadata date is shown.

The "Dates differ" hint (tooltip) always shows the date that was **not** used:
the ignored metadata date when the file name won, or the ignored file-name
date when camera EXIF won.
5. **Date-only file name** (no time in the name, no same-day metadata): the
   time is taken from the **file timestamp if it is on the same day**,
   otherwise it is **00:00:00**. The file timestamp is only read when needed.
6. No rule matches: unchanged behaviour (metadata → file timestamp).

Rejected: **"the earlier date wins"**. A wrong but plausible date — written by
an app or by a camera whose clock was never set — would beat a correct file
name. In the sample the contradicting EXIF date is *later*, but the next one
may be earlier; a fixed priority with a visible "Dates differ" flag is
predictable and lets the user see what was ignored.

### Storage
- **System rules** are a JSON file **embedded in `Snapdate.Infrastructure`**
  (`EmbeddedResource`, read with `GetManifestResourceStream`). Portable and
  installed builds behave the same, and there is nothing for the installer to
  lay down, nothing that can drift and nothing a user can tamper with.
- **Custom rules** live in `%APPDATA%\Snapdate\date-rules.json`, a separate
  file next to `settings.json` and written the same way: 64 KB size cap,
  source-generated `System.Text.Json`, temp file + atomic move. Install, upgrade
  and in-app update never write it (ADR 0006).
- **Schema v1.** System file:

  ```json
  {
    "schemaVersion": 1,
    "rules": [
      { "id": "whatsapp-android", "name": "WhatsApp (Android)",
        "patterns": [ "IMG-{YYYY}{MM}{DD}-WA*", "VID-{YYYY}{MM}{DD}-WA*" ] },
      { "id": "whatsapp-desktop", "name": "WhatsApp Desktop / Web",
        "patterns": [ "WhatsApp * {YYYY}-{MM}-{DD} * {HH}.{mm}.{ss}*" ] }
    ]
  }
  ```

  User file:

  ```json
  {
    "schemaVersion": 1,
    "disabledSystemRules": [ "whatsapp-desktop" ],
    "customRules": [
      { "id": "custom-3f9a1c2e", "name": "Scanner", "enabled": true,
        "syntax": "pattern", "patterns": [ "Scan_{YYYY}-{MM}-{DD}*" ] }
    ]
  }
  ```

- **Precedence:** enabled custom rules in list order, then enabled system
  rules in shipped order; the first match wins. No manual reordering in v1.
- **Stable ids.** System rule ids (`whatsapp-android`, `whatsapp-desktop`, …)
  are permanent and never renamed, like the installer `AppId`: the user file
  refers to them in `disabledSystemRules`. Ids match `^[a-z0-9-]{1,64}$`;
  custom ids are `custom-<8 hex>`.
- **Users never edit system rules.** They can **disable** one (its id goes to
  `disabledSystemRules`; unknown ids are kept, as a removed rule may come
  back) or **duplicate** it into a custom rule (the original stays enabled;
  the copy wins anyway because custom rules come first). There is no global
  on/off switch.
- **Robustness of the user file.** Missing → no custom rules. Larger than
  64 KB, invalid JSON or wrong types → *unreadable*: the file is ignored, the
  system rules stay active, and before the next save the file is moved aside to
  `date-rules.unreadable-YYYYMMDD-HHMMSS.json` (never overwritten or deleted).
  Over 100 custom rules, over 10 patterns, names over 80 characters or invalid
  ids → only the offending entries are dropped. An unknown `syntax` is kept as
  an inactive rule and written back unchanged. A newer `schemaVersion` → the
  rules are used best effort, but the file is **read-only**: Snapdate does not
  save it, so a newer version's data is never lost.

### Alternatives considered
- **Regular expressions only:** too technical for the audience; the token
  pattern covers the known cases and the regex mode remains for power users.
- **Hard-coded parsers per app:** not extensible by users and every new app
  needs a release.
- **Rules inside `settings.json`:** that file is rewritten on every run, and one
  corrupt file would lose both settings and rules.
- **System rules as a file laid down by the installer:** the portable exe has
  no installer, and a file in the install folder is user-writable for per-user
  installs, so it could drift from the exe or be tampered with.

## Consequences
- A new `DateSource.FileName` ("Date from name" / "Datum aus Name") appears as a
  badge and filter in the UI and as `FileName` in the `date_source` column of
  the CSV log.
- Photos can carry a "Dates differ" flag; the date that was not used (metadata
  or file-name date) stays visible in its hint.
- Rule ids are a permanent contract; renaming or reusing a system id would
  re-enable rules users had switched off.
- File-name input is untrusted: the generated regexes run with
  `NonBacktracking` and a timeout, and every date passes the calendar and
  plausibility checks, so a hostile name cannot hang the scan or inject an
  absurd date.
- The uninstaller's "delete settings" option also deletes the user's custom
  rules (`date-rules.json` is in the settings folder); the prompt says so.
- More seeded rules (Signal, Telegram, `IMG_YYYYMMDD_HHMMSS`, `PXL_…` in UTC)
  are a follow-up, not part of v0.3.0.
