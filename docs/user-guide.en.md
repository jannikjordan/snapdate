<p align="center"><img src="../assets/branding/logo-256.png" width="96" alt="Snapdate logo"></p>

# 📖 Snapdate — User guide

🇬🇧 English · [🇩🇪 Deutsch](user-guide.de.md)

Snapdate gives your photos **clear names based on the day they were taken**.
Instead of `IMG_4711.JPG`, `DSC00012.JPG` and `WhatsApp Image….jpeg` you get
`2024-07-14_09-12-33.jpg` — and all your pictures line up in the right order,
in any folder, on any computer.

> 💡 **You can't break anything.** Snapdate *copies* your photos by default and
> **never overwrites** a file. Your originals stay exactly where they are.

---

## Contents

1. [Getting started](#-1-getting-started)
2. [The three steps at a glance](#-2-the-three-steps-at-a-glance)
3. [Step 1 — Choose folders](#-3-step-1--choose-folders)
4. [Step 2 — Build the file name](#-4-step-2--build-the-file-name)
5. [Step 3 — Copy or move](#-5-step-3--copy-or-move)
6. [Check the preview](#-6-check-the-preview)
7. [Go!](#-7-go)
8. [Where does the date come from?](#-8-where-does-the-date-come-from)
9. [Dates from file names](#-9-dates-from-file-names)
10. [All blocks (placeholders)](#-10-all-blocks-placeholders)
11. [Install](#-11-install)
12. [Portable version](#-12-portable-version)
13. [Updates](#-13-updates)
14. [Uninstall](#-14-uninstall)
15. [Questions & answers](#-15-questions--answers)

---

## 🚀 1. Getting started

1. Download the installer `Snapdate-Setup-vX.Y.Z-win-x64.exe` from the
   [Releases page](https://github.com/jannikjordan/snapdate/releases/latest).
   (On ARM devices such as Surface Pro X or Copilot+ PCs, pick `win-arm64`.)
2. Double-click it and follow the steps — details in
   [Install](#-11-install). Then start **Snapdate** from the Start menu.
3. Windows may show *“Windows protected your PC”* the first time, because the
   app is new. Click **More info → Run anyway**.

> 💡 You'd rather not install anything? Use the **portable** file instead
> (`Snapdate-vX.Y.Z-win-x64-portable.exe`): just double-click it. See
> [Portable version](#-12-portable-version).

Snapdate speaks **English and German**. It picks the language of your Windows
automatically — you can switch any time with **EN | DE** at the top right.
The 🌙 button switches between light and dark, **?** opens this guide.

![Snapdate when you open it](images/en/01-start-light.png)

---

## 🧭 2. The three steps at a glance

```mermaid
flowchart LR
    A["📁 1. Choose folders<br/>where are the photos,<br/>where should they go?"] --> B["🧩 2. Build the name<br/>click blocks like<br/>Year · Month · Day"]
    B --> C["📋 3. Copy or move<br/>(bottom bar)"]
    C --> D["👀 Check the preview"]
    D --> E["▶️ Press the big button"]
    E --> F["✅ Done — open the folder"]
```

Nothing happens to your files until you press the big button at the bottom
right. Until then, everything is just a preview.

---

## 📁 3. Step 1 — Choose folders

| Box | What to choose |
|---|---|
| **Photos from** | The folder that holds your pictures, e.g. your phone backup or SD card. |
| **Save renamed photos to** | Where the renamed copies should go, e.g. a new folder *“Photos sorted”*. |

Click a box to pick a folder — or simply **drag a folder from Explorer onto
the box**.

**Include sub-folders** — when ticked, Snapdate also looks inside every folder
within your photo folder. Handy for phone backups like `DCIM\100APPLE`, `DCIM\101APPLE` …

> ⚠️ The target folder must not be *inside* the photo folder while
> “Include sub-folders” is on — otherwise Snapdate would find its own copies
> next time. Snapdate tells you if that happens.

![Folders chosen, preview filled](images/en/02-preview-light.png)

---

## 🧩 4. Step 2 — Build the file name

Here you decide what the new names look like. You don't need to type anything
special — just **click the blocks**:

| Group | Blocks |
|---|---|
| **Date** | Year · Month · Day · Month name · Weekday |
| **Time** | Hour · Minute · Second |
| **File** | Original name · Camera · Counter |
| **Separator** | `-` · `_` · `.` · Space · New folder (`/`) |

Every click adds the block where your cursor is in the box above. The row of
coloured blocks under the box shows what you built, with an example value:

- **Click a block** to switch its form: `07 ⇄ 7`, `2024 ⇄ 24`, `Jul ⇄ July`, `Sun ⇄ Sunday`.
- **Click the ✕** next to a block to remove it.
- The small **07** button next to the title switches all date and time blocks between `07` and `7` at once.

### ✍️ Your own text

You can type **any text** into the box, anywhere between the blocks — for
example `Holiday ` in front of the date:

```
Holiday {D}.{M}.{YYYY} {HH}{mm}   →   Holiday 14.7.2024 0912.jpg
```

![Own text in the name](images/en/03-custom-pattern-light.png)

### 🗂️ Sort into folders

The **New folder** block (`/`) puts photos into sub-folders. For example one
folder per year and month:

```
{YYYY}/{MM}/{YYYY}-{MM}-{DD} {name}   →   2024\07\2024-07-14 IMG_4711.jpg
```

![Year and month folders](images/en/04-folders-pattern-light.png)

### ⚡ Quick picks

Don't want to build anything? Open **Quick picks ▾** next to the title — the entries show
exactly how the result will look. **Reset** brings back the standard name.

> 💡 The file type (`.jpg`, `.png`, `.heic` …) is **always kept automatically**.
> You never need to add it.

If something in the box can't work (for example a character Windows doesn't
allow in file names, like `:` or `?`), the box turns red and tells you in plain
words what to fix:

![A friendly error message](images/en/05-pattern-error-light.png)

While the file name can't be used, the preview on the right is dimmed, the new
names are struck through and a red note says the names can't be built right now.
The list stays so you don't lose your place. Fix the file name and the preview
updates by itself. The **Copy** button stays off until then.

---

## 📋 5. Step 3 — Copy or move

You find this choice in the **bar at the bottom**, right next to the big
button: **Copy** or **Move**.

![Move selected in the bottom bar](images/en/06-move-light.png)

| Option | What happens | When to use |
|---|---|---|
| **Copy** *(recommended)* | Your originals stay untouched; renamed copies are created. | Almost always. Safest choice. |
| **Move** | The photos are moved to the target folder with their new names. | When you want to tidy up and have a backup. |

When moving, Snapdate first makes a **verified copy**, and only then removes
the original. On the same drive it simply renames — instantly and safely.
Before moving, Snapdate asks you once more:

![Confirmation before moving](images/en/07-confirm-move-light.png)

---

## 👀 6. Check the preview

On the right you see every photo: a small picture, the **old name** in grey and
the **new name** in bold. A coloured label tells you where the date came from:

| Label | Meaning |
|---|---|
| 🟢 **Camera date** | The date was read from the photo itself — the moment the picture was taken. |
| 🟣 **Date from name** | The photo has no camera date, but its file name has one (e.g. `IMG-20240714-WA0001.jpg` from WhatsApp). See [Dates from file names](#-9-dates-from-file-names). |
| 🟠 **File date** | The file has no date inside and none in its name (common for screenshots and chat pictures), so the date of the file was used. |
| 🟠 **Numbered** | Two photos would get the same name (e.g. taken in the same second), so `(2)`, `(3)` … was added. |

Use the tabs **All · Camera date · Date from name · File date · Numbered** to
look at just one group.

A small **⚠ Dates differ** under the label means the photo had two dates on
different days — for example one in its name and another one inside the file.
Point at it to see which date was used and which one was not (more in
[Where does the date come from?](#-8-where-does-the-date-come-from)).

---

## ▶️ 7. Go!

When everything looks right, press **Copy 12 photos** (or **Move …**) at the
bottom right. A progress bar shows how far Snapdate is; you can **Cancel** at
any time — files not yet processed stay untouched.

When it's done you can **open the folder** straight away. Snapdate also saves a
small **log file** (`snapdate-log-….csv`) in the target folder, listing every
old and new name — you can open it with Excel.

![Done](images/en/08-done-light.png)

---

## 🔍 8. Where does the date come from?

Snapdate looks for the date in this order and takes the first one it finds:

```mermaid
flowchart TD
    S([Photo or video]) --> A{"Date taken<br/>stored by a camera?<br/>(EXIF with camera model)"}
    A -- yes --> OK["🟢 Camera date"]
    A -- no --> N{"Date in the<br/>file name?<br/>(a rule fits)"}
    N -- yes --> C{"An app wrote a date<br/>into the file<br/>on the same day?"}
    C -- yes --> OK
    C -- "no (another day: ⚠ Dates differ)" --> NM["🟣 Date from name"]
    N -- no --> B{"Other date inside<br/>the file?<br/>(written by an app,<br/>XMP, video, PNG)"}
    B -- yes --> OK
    B -- no --> F["🟠 File date<br/>(the oldest date Windows<br/>knows for the file)"]
```

1. **A date from a camera always wins.** If the file name says another day,
   the photo is marked **⚠ Dates differ** and the hint shows the date from the
   name that was not used.
2. **Otherwise the date in the file name wins** over a date that an app (a
   gallery, backup or chat app — not a camera) wrote into the file, and over
   the file date. If both are on the **same day**, nothing is lost: Snapdate
   keeps the app's date, which usually has the more exact time.
3. If they are on **different days**, the name wins and the photo is marked
   **⚠ Dates differ**. Example: `IMG-20160706-WA0002.jpg` arrived via WhatsApp
   on 6 July 2016, but years later an app wrote 23 June 2026 into the file.
   Snapdate uses 6 July 2016 from the name; the hint shows the 2026 date it
   ignored, so you can check.
4. **Names with a date but no time** (like `IMG-20240714-WA0001.jpg`): if the
   file date is on the same day, Snapdate takes its time; otherwise the time
   is 00:00:00.
5. **No date in the name:** as before — a date inside the file, else the file date.

Dates that cannot be right — far in the future or before 1900 — are ignored,
also when they come from a file name.

---

## 📛 9. Dates from file names

WhatsApp removes the date from pictures it sends, but it puts the date into the
**file name**: `IMG-20240714-WA0001.jpg` on Android, or
`WhatsApp Image 2024-07-14 at 15.32.08.jpeg` from WhatsApp Desktop / Web.
Snapdate reads these names with **rules**. Such photos get the
**Date from name** label.

> 💡 The date in a WhatsApp name is the day the picture **arrived** on the
> phone or computer — not always the day it was taken.

Click **Dates from file names** above the preview list to see the rules:

![The rules for dates from file names](images/en/17-date-rules-light.png)

- **Built-in rules** come with Snapdate: *WhatsApp (Android)* and
  *WhatsApp Desktop / Web*. You can't change them, but you can
  **switch them off** with the switch on the left, or **Duplicate** one to get
  your own copy that you can change.
- **Your own rules** are marked *Yours*. They are always tried **before** the
  built-in ones, and updates never change them. Use **Edit** and **Delete** to
  change or remove them.
- The first rule that fits a name is used. Every change is saved at once; the
  preview is updated when you close the window.
- **Try a file name:** type a name, or pick one from your photo folder. Snapdate
  shows which rule reads which date — or *No rule matches*. In that case
  **Create a rule from this name** starts a new rule for names like it.

### ✏️ Your own rules

Click **Add rule** (or **Edit**). Give the rule a name and describe how your
file names look — one pattern per line, up to 10. Click the blocks under the
box to insert them at the cursor:

![Editing a rule](images/en/18-date-rule-editor-light.png)

| Block | Code | Fits |
|---|---|---|
| Year | `{YYYY}` · `{YY}` | `2024` · `24` |
| Month | `{MM}` · `{M}` | `07` · `7` or `07` |
| Day | `{DD}` · `{D}` | `04` · `4` or `04` |
| Hour | `{HH}` · `{H}` | `09` · `9` or `09` |
| Minute | `{mm}` · `{m}` | `05` · `5` or `05` |
| Second | `{ss}` · `{s}` | `08` · `8` or `08` |
| Any text | `*` | anything, also nothing |
| One character | `?` | exactly one character |

- A pattern needs a **year, month and day**. Hour and minute go together;
  seconds need minutes. Each part may appear only once.
- The pattern must fit the **whole name without the extension** — so
  `IMG_{YYYY}{MM}{DD}_*` fits `IMG_20240714_153208.jpg`. Upper and lower case
  don't matter. Everything else must be in the name exactly as typed.
- `*` and `?` can't appear in Windows file names, so they never get mixed up
  with your text. To write a curly brace itself, type it twice: `{{` or `}}`.
- The short blocks `{M}`, `{D}`, `{H}`, `{m}`, `{s}` need something clear next
  to them: `{M}{D}` would not tell `1 12` from `11 2`. Put a separator between
  them or use `{MM}{DD}`.
- Below the box Snapdate explains each line that can't be used, in plain words.
  The tester shows which line fits a name and which date it reads — or that a
  line fits, but `2024-13-45` is not a date.

### 🧪 Advanced: regular expressions

If you know **regular expressions**, tick **Advanced: regular expressions** in
the editor. Your text is kept when you switch. Mark the date parts with named
groups: `year` (or `yy` for two digits), `month` and `day`, and optionally
`hour`, `minute` and `second`. For example:

```
^IMG-(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})-WA\d+$
```

![A rule as a regular expression](images/en/19-date-rule-regex-light.png)

To keep matching fast and safe, Snapdate accepts only a simple part of the
regular-expression language:

- **Allowed:** normal characters, `\d \s \w .`, simple classes such as
  `[a-z_-]` or `[^0-9]`, groups `( )`, `(?: )` and the named groups above,
  alternatives with `|`, `^` and `$`, and repetitions (`* + ? {n} {n,m}`) with
  counts up to 20.
- The expression must fit the **whole name** without the extension; upper and
  lower case don't matter. At most 300 characters per line.
- A part **whose length can vary** — such as `.*`, `x?` or alternatives of
  different length like `(?:a|bc)` — **can't be repeated** again. Write the
  parts out one after another instead.
- Only a few open-ended repetitions (`*`, `+`, `{n,}`) are allowed per
  expression; very long or complex expressions are refused.
- **Not available:** look-arounds, back-references, options such as `(?i)`,
  `\b`, `\p{…}` and other letter escapes.

Anything that can't be used is explained in plain words; the tester tries a
name shortly after you stop typing.

---

## 🧩 10. All blocks (placeholders)

In the box, each block appears as a short code in curly braces. You can also
type these codes yourself. **Upper and lower case matter**: `MM` is the month,
`mm` the minute.

| Block | Code | Example |
|---|---|---|
| Year | `{YYYY}` · `{YY}` | `2024` · `24` |
| Month | `{MM}` · `{M}` | `07` · `7` |
| Month name | `{MMM}` · `{MMMM}` | `Jul` · `July` |
| Day | `{DD}` · `{D}` | `04` · `4` |
| Weekday | `{ddd}` · `{dddd}` | `Thu` · `Thursday` |
| Hour (24 h) | `{HH}` · `{H}` | `09` · `9` |
| Minute | `{mm}` · `{m}` | `05` · `5` |
| Second | `{ss}` · `{s}` | `08` · `8` |
| Original name | `{name}` | `IMG_4711` |
| Camera | `{camera}` | `iPhone 15 Pro` |
| Counter | `{counter}` | `001`, `002`, … (in date order) |

Everything outside `{…}` is kept exactly as typed. To write a curly brace
itself, type it twice: `{{` or `}}`.

**Supported files:** JPG, PNG, HEIC/HEIF, AVIF, WebP, TIFF, GIF, BMP, camera
RAW (DNG, CR2, CR3, NEF, ARW, ORF, RW2, RAF, PEF, …) and videos (MP4, MOV, M4V, 3GP).

---

## 💿 11. Install

The installer is the recommended way to get Snapdate. It puts Snapdate in the
Start menu like any other program and lets it tell you about new versions.

1. Download `Snapdate-Setup-vX.Y.Z-win-x64.exe` (ARM devices:
   `…-win-arm64.exe`) and double-click it.
2. Choose **who** should get Snapdate:

   ![Choose the install mode](images/en/14-installer-scope.png)

   | Choice | What it means |
   |---|---|
   | **Install for me only** *(recommended)* | Only your Windows account. No administrator rights needed. |
   | **Install for all users** | Every account on this PC. Windows asks for administrator approval. |

3. If the installer first asks for a **language**, pick English or German. Then
   click **Next** through the steps. You can keep the suggested folder. The
   **desktop shortcut** is ticked by default (untick it if you don't want it);
   the **Start menu** entry is always created.
4. Click **Install**, then **Finish**. Snapdate starts if you leave
   the “Launch Snapdate” box ticked.

> ⚠️ **Windows may warn you** (“Windows protected your PC”, *SmartScreen*)
> when you open the installer or Snapdate for the first time. Snapdate is new
> and not yet signed with a paid certificate, so Windows doesn't know it yet.
> Click **More info**, then **Run anyway**. If you'd like to double-check the
> file first, see the checksum in the release notes
> (`SHA256SUMS.txt`; in PowerShell: `Get-FileHash <file> -Algorithm SHA256`).

**Installing a newer version** works the same way: run the new installer. It
updates Snapdate in place and **keeps your settings**.

---

## 🧳 12. Portable version

The **portable** file needs no installation at all:

1. Download `Snapdate-vX.Y.Z-win-x64-portable.exe` (ARM devices:
   `…-win-arm64-portable.exe`).
2. Save it anywhere — your Documents folder or a USB stick — and double-click it.

It does not add anything to the Start menu or to *Settings → Apps*. To remove
it, simply delete the file. Your settings stay in `%APPDATA%\Snapdate` (see the
questions at the end); delete that folder too if you want everything gone.

The portable version **does not update itself**. When a new version is out, it
shows a banner with a **Download** button that opens the download page, so you
can download the new file again (see [Updates](#-13-updates)).

---

## 🔔 13. Updates

Snapdate checks **at most once a day**, when it starts, whether a newer version
exists. For this it only asks GitHub (`api.github.com`) for the newest version
number. It sends **no photos, no file names and no personal data** — GitHub
sees your IP address, as with any website you visit.

**First start:** Snapdate only shows a short notice that explains this. The first
check happens at the next start. Press **Got it** to close the notice. If you
switch the automatic check off in the meantime, the notice disappears too.

![Notice on the first start](images/en/13-first-start-notice-light.png)

**When a new version is out**, a slim banner appears at the top:

![Update banner, installed version](images/en/10-update-installed-light.png)

| Button | What it does |
|---|---|
| **Update now** | Installed version only. The banner shows the download progress (with **Cancel**), then Snapdate checks the signature of the download and starts the installer. For an “all users” installation, Windows asks for administrator approval first (this can take as long as you need). Snapdate then closes by itself and starts again in the new version. If you decline the approval, nothing changes. |
| **What's new** | Opens the release notes in your browser. |
| **Later** | Hides the banner until the next start; it returns at the next automatic check. |
| **Skip this version** | Stops the automatic reminders for this version. A newer one is announced again. **Check now** (see below) still shows a skipped version. |

If the download, the check of the signature or the installation fails (or the
approval is declined), **nothing is installed**;
the banner says so briefly and offers **Try again** (where that can help) and **Open download page**.

![Update banner while downloading](images/en/15-update-downloading-light.png)

![Update banner after a failed update](images/en/16-update-failed-light.png)

**Portable version:** the banner has a **Download** button instead of
*Update now*. It opens the latest download page, where you download the new
portable file again.

![Update banner, portable version](images/en/11-update-portable-light.png)

**Turning it off, checking by hand:** click the **ⓘ** button in the top right
(*About Snapdate*). There you can untick **Check for updates automatically**,
or press **Check now** at any time.

![About window with update settings](images/en/12-about-update-light.png)

If you are offline, nothing bad happens: the automatic check simply stays
silent and Snapdate keeps working normally. Only when you press **Check now**
does it tell you *“Couldn't check right now — are you offline?”*.

---

## 🗑️ 14. Uninstall

1. Open Windows **Settings → Apps → Installed apps** (Windows 10: *Apps & features*).
2. Find **Snapdate**, choose **Uninstall** and follow the steps.

If you installed **for me only**, Snapdate asks whether to **also delete your
settings** (remembered folders, file name pattern, language) **and your own
date rules**. Choose **No** to keep them — handy if you want to install
Snapdate again later.
With an **all users** install your settings are always kept.

Your photos and renamed files are never touched. The portable version has no
uninstaller: delete the file.

---

## ❓ 15. Questions & answers

**Will my photos be changed?**
No. With **Copy**, originals are never touched. Even the photos themselves are
not modified — only the file names of the copies differ.

**Can Snapdate overwrite something?**
No. If a name already exists in the target folder, Snapdate adds `(2)`, `(3)` …

**Does Snapdate send anything to the internet?**
Snapdate works offline — except the optional update check. Once a day at
most it asks GitHub for the newest version number; your photos, file names and
personal data are never sent (GitHub sees your IP address, as any website does).
You can turn the check off under *About* → **Check for updates automatically**.
There is no account and no tracking. The **?** and *Project website* buttons open
a web page only when you click them.

**Why do some photos say “File date”?**
Screenshots, pictures saved from chats or social media often have no
date inside. If the name has no date either, the date of the file is the best
Snapdate can do. For names with a date, see
[Dates from file names](#-9-dates-from-file-names).

**My WhatsApp photos from an iPhone have no date.**
An iPhone saves pictures from WhatsApp with names like `IMG_4200.jpg` — there
is no date in the name and none inside the file. Ask the sender to send the
photos **“as Document”** (WhatsApp then keeps the original file with its date),
or copy the pictures soon after you receive them, so the file date is close to
the right day.

**Why does a photo say “Dates differ”?**
It had two dates on different days, e.g. one in the name and one written into
the file by an app. Point at the hint to see which date was used and which one
was ignored. See [Where does the date come from?](#-8-where-does-the-date-come-from).

**Where are my own date rules stored?**
In `%APPDATA%\Snapdate\date-rules.json`, next to the settings. Updates never
change this file. If it ever can't be read, the built-in rules still work and
before Snapdate saves a new one, it moves the old file aside as
`date-rules.unreadable-….json` (it is never deleted).

**What is the log file for?**
It lists what was copied or moved, from where to where. You can delete it at
any time.

**My phone photos are in many sub-folders.**
Tick **Include sub-folders** and choose the main folder (e.g. `DCIM`).

**Where does Snapdate keep its settings?**
In `%APPDATA%\Snapdate\settings.json` (last folders, your name pattern,
language). Delete the file to start fresh.

---

<sub>Found a problem or have an idea? Open an issue on
[GitHub](https://github.com/jannikjordan/snapdate/issues).</sub>
