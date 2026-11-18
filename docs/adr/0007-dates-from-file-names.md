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
  (`AmbiguousNeighbours`, see below). Regex mode adds `InvalidSyntax`,
  `UnsupportedConstruct`, `RepetitionTooLarge`, `UnknownGroup` and the
  fallback `InvalidExpression` (see below). A rule with one
  invalid pattern still uses its valid ones.
- **Ambiguity rule (runs).** A run is a maximal sequence of placeholders, `?`
  and literal digits 0–9 (it ends at any other literal character, at `*`, and
  at the start or end of the pattern); a run may contain at most one
  variable-width placeholder (`{M}` `{D}` `{H}` `{m}` `{s}`), and a run that
  contains one must not contain `?` and must not touch `*`. Violations are
  reported as `AmbiguousNeighbours`.
  - Valid: `WhatsApp * {YYYY}-{MM}-{DD} * {HH}.{mm}.{ss}*`,
    `{YYYY}{M}{DD}-*`, `{YYYY}{MM}{DD}?{HH}{mm}` (no variable-width
    placeholder, so `?` is allowed).
  - Invalid: `{YYYY}{M}{D}` (two variable-width placeholders in one run),
    `{YYYY}-{M}1{D}` (the digit `1` joins `{M}` and `{D}` into one run),
    `*{D}.{MM}.{YYYY}` (the run with `{D}` touches `*`),
    `{YYYY}{M}{DD}?*` (the run with `{M}` contains `?` and touches `*`),
    `{YYYY}{M}{DD}?-*` and `{YYYY}-{M}?-{DD}` (the run with `{M}` contains
    `?`).
- When a name contains several places where a pattern with `*` on both sides
  could match, the first date from the left is used. The leftmost block that
  fits the pattern's shape is chosen before the date is validated; if that
  block is not a valid date, the pattern does not match and the next pattern
  or rule is tried (later blocks in the same name are not).
- Each pattern is compiled once to a `Regex` with
  `NonBacktracking | IgnoreCase | CultureInvariant | ExplicitCapture`, a
  **50 ms match timeout** (a timeout counts as no match) and no
  `RegexOptions.Compiled`. Digits are `[0-9]` (never `\d`, which matches
  non-ASCII digits). Names longer than 260 characters are not matched.
- **What protects matching.** `NonBacktracking` has no catastrophic
  backtracking: its time grows linearly with the name, but the factor depends
  on the expression, and it checks the timeout only now and then (not at all
  while it fills in the groups after a match is found). The timeout is
  therefore a last resort, not a guarantee. The real protection is that the
  expressions are small: token patterns produce only fixed-width groups,
  `[0-9]{1,2}` and `.*?` (measured worst case on hostile 260-character names
  with 200-character patterns: under 10 ms), and regular expressions get the
  structural limits below.
- Values are parsed with `NumberStyles.None` and the invariant culture. A
  match only counts if it forms a real calendar date and time (month 1–12, day
  within the month, hour 0–23, minute and second 0–59) and passes the
  **plausibility check shared with the metadata reader** (`DatePlausibility`:
  not before 1900-01-01, not more than 2 days in the future). Otherwise the
  next pattern or rule is tried.
- **Two-digit year pivot:** `{YY}` means 20YY, or 19YY when 20YY is after the
  current year (whole years are compared, not full dates).

**Advanced mode: regular expression.** A rule can set `"syntax": "regex"`
instead of the default `"pattern"` (in the editor: the "Advanced: regular
expressions" check box, off by default; switching keeps the text and checks it
again). The editor keeps regex lines as typed (only the `\r` of a line break is
dropped), as spaces can matter. Each line is one expression (`DateExpression`).

*Why an allowlist.* `NonBacktracking` has no catastrophic backtracking, but its
time per character grows with how many places of the expression can be active
at once, and it checks the timeout only now and then (not while it fills in the
groups). Two review rounds found accepted expressions that took 0.5–18 s and up
to 660 MB for one 260-character name and never timed out: repeated groups that
repeat something (`(?:.*a.{0,99}){0,10}`), alternatives of different length
acting as hidden repetitions (`(?:(?:|.|..)a){20}`), and character classes the
checker read differently from the engine (`[\-[x]…]`). Guessing how the engine
reads the text is therefore replaced by Snapdate's own parser
(`ExpressionGrammar`): only what it understands is accepted, and the engine
must agree.

Checks, in order:
1. Not empty, at most **300 characters**.
2. **Allowed subset** (recursive-descent parser; anything else →
   `UnsupportedConstruct` naming the construct and its position, or
   `InvalidSyntax` with the position):
   - atoms: literal characters (not `\ . [ ] ( ) { } * + ? | ^ $`, no control
     characters), escaped characters that are not letters, digits or `_`
     (`\.`, `\(`, `\ `), `\d \s \w`, `.`, anchors `^ $`;
   - classes `[…]`/`[^…]` of literal characters, ranges (`a-z`), escaped
     characters and `\d \s \w` — no `[` inside (so no nesting or subtraction),
     no `\p{…}`, a `]` only as `\]`, not empty;
   - groups `(…)` (not capturing, `ExplicitCapture`), `(?:…)` and
     `(?<name>…)` with `name` one of `year`, `yy`, `month`, `day`, `hour`,
     `minute`, `second` (case-sensitive; else `UnknownGroup`) — no other `(?`
     form: no inline options (so no `(?x)` comments), lookarounds, atomic
     groups, conditionals, comments or balancing groups; no backreferences,
     `\b`, `\G` or other letter escapes;
   - alternation `|`;
   - quantifiers `* + ? {n} {n,} {n,m}` and their lazy forms, n ≤ m ≤ **20**
     (`RepetitionTooLarge`; dates need at most 4). A `{` that is no
     quantifier must be written `\{`.
3. **Width rule.** Every node knows its shortest and longest width (unbounded
   for `* + {n,}`); an alternation whose branches differ in length is variable
   too. A quantifier on anything whose width varies, or that contains such a
   part, → `NestedQuantifier` (`(?:.*a)+`, `(?:x?){3}`, `(?:|.){3}`,
   `(?:(?:.|..)a){20}`). Fixed-width parts may be repeated
   (`(?:_\d{2}\d{2})?`, `(?:ab|cd){3}`). Repeating something of width 0
   (`^*`, `()?`) → `UnsupportedConstruct`.
4. **Budget** → `TooComplex`: at most **30 quantifiers**, at most **8
   unbounded** ones, and unbounded quantifiers × positions ≤ **240**, where
   positions = single-character steps once counts are written out (`\d{4}` = 4,
   `.{0,20}` = 20, `.*` = 1, a fixed-count group counts its content each time,
   an alternation counts all its branches). Without an unbounded quantifier
   there is no cost: bounded parts alone measured cheap. Realistic expressions
   stay far below: `.*(?<year>\d{4})[-_.]?(?<month>\d{2})[-_.]?(?<day>\d{2})[-_ T]?(?<hour>\d{2})[-_.:]?(?<minute>\d{2})[-_.:]?(?<second>\d{2}).*`
   costs 2 × 21 = 42.
5. **The engine must agree:** the text is compiled on its own (so it cannot
   close the `^(?:…)$` wrapper from inside, e.g. `a)|(?:b`) and then wrapped,
   with `NonBacktracking | IgnoreCase | CultureInvariant | ExplicitCapture` and
   the 50 ms timeout. Any exception → `InvalidExpression`; never a crash.
6. **Required groups:** `year` or `yy`, `month`, `day`; the same time
   dependencies as patterns (hour ⇔ minute, second ⇒ minute). A name may
   appear in several alternatives (`(?<year>…)-…|…(?<year>…)`).

*Measured* (tests in `MatchTimeTests`, `DateRuleSetTests`; hostile names of 260
characters such as runs of one letter with one odd letter, repeating pairs,
digits only, random text): every expression from both review rounds is now
rejected or takes under 30 ms; the slowest accepted expressions found by
fuzzing (thousands of combinations including empty and different-length
alternatives, classes with escapes and `[`, fixed-count groups) and at the
budget's edges take up to about 40 ms per match on a developer machine. The
tests require under 200 ms per match.

*Per-name budget.* `DateRuleSet.Match` stops trying regex rules for a name once
they have used **200 ms** together (`DefaultRegexBudget`); token-pattern rules
are cheap and still run. So 100 rules × 10 expressions cannot add up to
seconds per file; one name costs at most the budget plus the one expression
that was running. The rule editor's tester runs off the UI thread, 150 ms after
the last change, and shows only the latest result.

Matching is shared with patterns: names longer than 260 characters are not
matched, a timeout (when the engine notices it) counts as no match, and group
values must be 1–4 **ASCII** digits (`\d` may match other scripts' digits;
those never give a date), then pass the calendar and plausibility checks.
Implemented in #21 (v0.3.0).

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
- File-name input is untrusted: the regexes run with `NonBacktracking` (no
  catastrophic backtracking) on names of at most 260 characters, token patterns
  and regular expressions are kept small enough (structural limits above) that
  a hostile name costs milliseconds, the timeout is a last resort the engine
  checks only approximately, and every date passes the calendar and
  plausibility checks, so a hostile name cannot inject an absurd date.
- The uninstaller's "delete settings" option also deletes the user's custom
  rules (`date-rules.json` is in the settings folder); the prompt says so.
- More seeded rules (Signal, Telegram, `IMG_YYYYMMDD_HHMMSS`, `PXL_…` in UTC)
  are a follow-up, not part of v0.3.0.
