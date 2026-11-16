# ADR 0004 — Update check and in-app update

- Status: accepted (part 1: the update check, issue #2). Issue #3 will extend this ADR with the
  verified in-app updater; issue #4 adds release signing.
- Date: 2026-11-16

## Context
Snapdate has had **no network access at all** (see [security.md](../security.md)). Users of a
desktop tool rarely check for new versions, so fixes (including security fixes) reach them late.
We want to tell them when a new version exists — without telemetry, without surprises and with an
off switch.

## Decision
**Update check (this ADR, part 1)**
- One endpoint only: `https://api.github.com/repos/jannikjordan/snapdate/releases/latest`.
  The code hard-checks scheme `https`, host `api.github.com` and the default port, also for
  redirects (followed manually, same host only, at most 3). Anything else is rejected.
- The request is a plain `GET` with `Accept` and `User-Agent: Snapdate/<version>`. No cookies,
  credentials, machine or user identifiers, no extra headers. (GitHub necessarily sees the IP address,
  as with any web request.)
- Automatic check on start at most **once per 24 h**; **on by default**, switchable off in the
  settings; a manual "check now" ignores the limit and the opt-out. The time is stored only after an
  answer was received, so an offline start does not suppress the next attempt.
- Hard limits: 10 s timeout, 1 MB response cap (declared and actual), cancellation honoured.
- Every failure (offline, DNS/TLS, timeout, HTTP error incl. 403/429 rate limit, oversized or
  malformed JSON, links that do not point into our own repository) yields "no information": the UI shows
  nothing and never an error.
- The answer is **untrusted**. JSON is parsed with a source-generated `System.Text.Json` context
  (trim/AOT safe). The release page and asset URLs must be `https://github.com/jannikjordan/snapdate/…`;
  asset names are restricted to safe characters.
- Drafts and pre-releases are ignored; the same or an older version is never offered
  (SemVer precedence, `0.2.0-rc.1 < 0.2.0`); a version can be "skipped" (hidden for automatic checks only).
- Two install kinds will exist. **Portable** builds only show "new version available" with a link to
  the release page. **Installed** builds (Inno Setup, issue #1) may later download and run the
  installer (issue #3, to be added here, including signature verification).
- Layering: `AppVersion` in Domain; `IReleaseFeed`, `IClock` ports and `UpdateCheckService` in
  Application; `GitHubReleaseFeed` in Infrastructure (the only place with `HttpClient`). Settings
  (`AutoUpdateCheck`, `LastUpdateCheckUtc`, `SkippedVersion`) extend `AppSettings`.

**In-app update (issue #3, to be added)** — download of the installer asset, verification
(ECDSA P-256 `.sig`, issue #4) and launch. Until then no update code beyond the check exists.
Note for #3: GitHub serves release assets via a redirect from `github.com/.../releases/download/...`
to its download CDN (e.g. `objects.githubusercontent.com`, `release-assets.githubusercontent.com`).
The allow-list above (`api.github.com` for the check, `github.com` links) does not cover that hop, so
#3 must extend it explicitly in this ADR (named CDN hosts, https only, same size cap logic, signature
verification before anything runs) rather than loosen the check's rules.

## Consequences
- The "no network" promise becomes "no network except this opt-out check (and later the verified
  updater)". AGENTS.md, security.md and the privacy statements are updated accordingly.
- GitHub's unauthenticated API allows 60 requests/hour/IP; once a day per user is far below that, and
  rate-limit answers are silent.
- Any further network use (another host, another purpose) needs a new ADR.
- Found while implementing: `AppSettings` used `init` properties, and the source-generated JSON
  deserializer then drops property defaults for keys missing in older settings files (e.g.
  `IncludeSubfolders` became false). The properties are now `set`; a regression test covers it. This
  matters here because the update check must default to on.
