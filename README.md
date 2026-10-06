# RemoteJobs

A small, self-contained .NET + AI job aggregator. A console app fetches postings from eleven
free job APIs (eight no-key, three needing a free key) across multiple countries, filters
them down to .NET/C#/Blazor and AI/ML roles, classifies each by work mode (remote/hybrid/
onsite/unknown), and writes `jobs.json` + `stats.json`. Static `index.html`/`stats.html`
pages read those files client-side - there is no backend, no database, and nothing to host
beyond static files.

## Screenshots

The search page - work-mode filter, country/comp filters, newest-first by default:

![RemoteJobs search page: dark theme, filter bar with work-mode toggle set to 100% remote, grid of job cards](blog-images/search-page.png)

The Stats page - raw postings per source, how many clear the relevance bar, and the work-mode
breakdown:

![RemoteJobs stats page: stat tiles for totals, a raw-postings-per-source bar chart, and a work-mode breakdown bar chart](blog-images/stats-page.png)

## How it works

- `src/RemoteJobs.Fetcher` - a .NET 9 console app. One adapter class per job source
  (`Adapters/`), normalizing every posting into a shared `JobPosting` shape. Filtering
  (`.NET` relevance, work-mode classification, country parsing, salary parsing, dedupe)
  lives in `Filtering/` and `Program.cs`.
- `src/RemoteJobs.Fetcher/wwwroot/index.html` - the search page, with a work-mode filter
  (100% remote / hybrid / onsite / unknown) alongside the existing search/country/comp
  filters. It's copied to the build output folder automatically; the fetcher writes
  `jobs.json` into that same folder, so the output directory is a complete,
  self-contained static site.
- `src/RemoteJobs.Fetcher/wwwroot/stats.html` - a Stats page (linked from the top of
  `index.html`) showing pipeline volumes per source: raw postings fetched, how many are
  .NET-relevant, and their work-mode breakdown, reading a `stats.json` the fetcher writes
  alongside `jobs.json`.
- `src/RemoteJobs.Web` - optional, for local development only. A ~10-line ASP.NET Core
  app whose only job is to serve the fetcher's Debug output folder over HTTP, so you get
  real F5-to-browse convenience in Visual Studio instead of running a separate static
  server command. See [Running from Visual Studio](#running-from-visual-studio) below.

Sources used - the first eight are free, public, no API key required:

| Source | Endpoint |
|---|---|
| RemoteOK | `https://remoteok.com/api` |
| Remotive | `https://remotive.com/api/remote-jobs` |
| Arbeitnow | `https://arbeitnow.com/api/job-board-api` |
| Himalayas | `https://himalayas.app/jobs/api` |
| Jobicy | `https://jobicy.com/api/v2/remote-jobs` |
| Working Nomads | `https://www.workingnomads.com/api/exposed_jobs/` |
| WeWorkRemotely | `https://weworkremotely.com/categories/remote-programming-jobs.rss` (RSS) |
| The Muse | `https://www.themuse.com/api/public/jobs` - no key needed at all |
| Adzuna (optional, UK/US/CA/AU/DE/NL/FR) | `https://api.adzuna.com/v1/api/jobs/{country}/search/{page}` - needs a free key |
| Reed (optional, UK) | `https://www.reed.co.uk/api/1.0/search` - needs a free key |
| Careerjet (optional, UK/US/CA/AU/DE/NL/FR) | `https://search.api.careerjet.net/v4/query` - needs a free key + IP whitelist |

The Muse is a general job board, queried with `category=Software Engineering` and
`location=Flexible / Remote` - that location value is a genuine structured "this role is
remote-friendly" signal from the source itself (not a text guess), so it uses the same
*assumed remote unless flagged* trust tier as the other remote-focused boards rather than
Adzuna/Reed's stricter *requires explicit remote match*. It's fetched up to 10 pages (200
jobs) per run - a self-imposed cap for politeness, not an API-enforced limit, since no key
is required at all.

A few sources return far more than their single default page, and are paginated up to a
self-imposed cap (none of these are API-enforced limits) to widen the raw pool the filters
draw from:

| Source | Per-page size | Self-imposed cap | Why |
|---|---|---|---|
| Himalayas | 20 (the API silently ignores a higher `limit`) | 15 pages / 300 jobs | It's a general remote board covering every job category, not just dev roles, so a bigger raw sample is needed to catch the few .NET-titled ones. |
| Arbeitnow | 250 | 5 pages / up to 1,250 raw, filtered to remote-only in the adapter | Most of Arbeitnow's postings aren't remote (or aren't in English), so one page rarely surfaces many remote roles. |
| Adzuna | 50 | 5 pages / 250 jobs | Its query is already scoped to remote + `.net`/`c#`/`asp.net`/`blazor`/`dotnet`, so more pages means more legitimately-scoped postings, not more noise - this is the single highest-yield source as a result. |

RemoteOK, Remotive, Jobicy, Working Nomads and WeWorkRemotely already return their entire
current listing (or the source's own hard cap, e.g. Jobicy's 100) in one request, so there's
nothing further to paginate there.

One source considered and rejected: **NoFluffJobs** has excellent .NET/remote data (a
genuine structured `fullyRemote` flag), but its `robots.txt` disallows `/api/` for
automated tools and it isn't a published developer API - using it would go against the
site's stated wishes, which conflicts with this project's "not scraping" principle even
though the endpoint is technically unauthenticated.

A couple of the first eight ask (in their terms/friendly notices, not enforced technically)
that you don't poll too often: Remotive suggests max ~4 times/day, Jobicy asks for max
once/hour. Running the fetcher every few hours on a schedule is well within that.

Adzuna, Reed and Careerjet were added because they're general job boards, not remote-only
ones, and Reed/Adzuna in particular helped correct how under-represented the UK was in the
free sources above (most of those just say "Europe"/"Worldwide" rather than naming the UK).
Being general boards, a posting from any of the three needs to **positively mention "remote"**
(not just avoid saying "hybrid"/"onsite") to be classified as work mode `Remote` rather than
`Unknown` - see [Filtering notes](#filtering-notes--known-heuristics) below. That "remote"
check is English-word-specific, so DE/NL/FR postings from Adzuna/Careerjet that use local
terms like "Home Office" or "télétravail" instead will mostly land in `Unknown`, not `Remote` -
a known gap, not a bug.

### Enabling Adzuna, Reed and Careerjet (optional)

All three require a free API key that you register for yourself - this project can't create
accounts on your behalf. If you skip any of them, everything else still works exactly as
before; a skipped adapter just prints `skipped` and the fetcher continues.

1. **Adzuna**: sign up at <https://developer.adzuna.com/> for a free `app_id` + `app_key`.
   Queries seven country indices under the same key: UK, US, Canada, Australia, Germany,
   Netherlands and France.
2. **Reed**: sign up at <https://www.reed.co.uk/developers/jobseeker> for a free API key.
   UK-only.
3. **Careerjet**: sign up at <https://www.careerjet.com/partners/api/> for a free API key,
   **then whitelist your machine's outbound public IP address in the Careerjet publisher
   dashboard** - every request 403s without this, and there's no API to do it for you. If
   you're running this from a home connection with a dynamic IP, expect to have to update
   that whitelist entry occasionally when your IP changes. Also queries the same seven
   countries as Adzuna.
4. Set them as environment variables before running the fetcher:

   ```
   setx ADZUNA_APP_ID "your-app-id"
   setx ADZUNA_APP_KEY "your-app-key"
   setx REED_API_KEY "your-reed-key"
   setx CAREERJET_API_KEY "your-careerjet-key"
   setx CAREERJET_USER_IP "your-whitelisted-ip"
   ```

   (`setx` persists them for future terminal sessions and for Visual Studio once restarted;
   open a new terminal/restart VS afterwards for them to take effect. Use `set` instead of
   `setx` for a one-off, current-session-only value.)

Reed is UK-only by nature, so every job from it is tagged `Countries: ["United Kingdom"]`.
Adzuna and Careerjet tag each job with the country of the index it came from (e.g.
`Countries: ["Germany"]`), and report compensation in that country's currency (GBP/USD/
CAD/AUD/EUR) rather than converting anything.

All three free tiers have a rate/quota limit - check the numbers on your own account
dashboard after signing up, since they're not fixed publicly and can change. A run every few
hours is very unlikely to be an issue either way, though Adzuna/Careerjet now make several
calls per run (one per page per country) rather than one - lower `MaxPagesPerCountry`/
`MaxPagesPerLocale` in the adapter source if you hit a quota error.

**Note:** Adzuna has been live-tested end-to-end (on its original UK index) and confirmed
working; the other six Adzuna country indices reuse the identical request shape, just with a
different country code, so they should behave the same way but weren't individually
live-tested. Reed and Careerjet were both written against their published API docs but not
live-tested (no test API key was available for either, and Careerjet also needs a whitelisted
IP this project doesn't have). The JSON parsing and request shape should be correct, but if
you hit an error after adding a key, check the console output for the exact HTTP error and
open an issue in this repo (or just tell me) with that message.

## Running the fetcher

```
dotnet run --project src/RemoteJobs.Fetcher
```

This builds the app (if needed), calls all eleven sources, filters/dedupes the results, and
writes:

```
src/RemoteJobs.Fetcher/bin/Debug/net9.0/wwwroot/jobs.json
src/RemoteJobs.Fetcher/bin/Debug/net9.0/wwwroot/stats.json
src/RemoteJobs.Fetcher/bin/Debug/net9.0/wwwroot/index.html   (copied automatically)
src/RemoteJobs.Fetcher/bin/Debug/net9.0/wwwroot/stats.html   (copied automatically)
```

Everything the site needs lives in that one `wwwroot` folder. Copy it anywhere, or point a
static host at it directly.

### Scheduling it (Windows Task Scheduler)

First publish a self-contained build so you're not depending on `dotnet run`/the SDK being
present at run time:

```
dotnet publish src/RemoteJobs.Fetcher -c Release -o publish
```

This produces `publish/RemoteJobs.Fetcher.exe` alongside a `publish/wwwroot/` folder
(with `index.html`); running the exe regenerates `publish/wwwroot/jobs.json` in place.

Then create a scheduled task that runs it every few hours, e.g. via `schtasks`:

```
schtasks /create /tn "RemoteJobsFetcher" /tr "C:\path\to\publish\RemoteJobs.Fetcher.exe" /sc hourly /mo 4
```

(Adjust the path and interval as you like - every 4-6 hours is more than enough given the
source rate-limit guidance above.)

## Opening the search page

`index.html` calls `fetch('jobs.json')`, which browsers block for pages opened directly
via `file://` (a CORS restriction, not something this project can work around while still
using a real `jobs.json` file). Serve the `wwwroot` folder with any static file server:

```
# Node (no install needed beyond npx)
npx http-server publish/wwwroot -p 8080

# Python
python -m http.server 8080 --directory publish/wwwroot
```

Then open `http://localhost:8080`. Or push the `wwwroot` folder to any static host
(GitHub Pages, Netlify, Azure Static Web Apps, etc.) - there's no server-side code to
deploy, just files.

## Running from Visual Studio

Open `RemoteJobs.sln`. There are two runnable projects:

- **RemoteJobs.Fetcher** - the actual aggregator. Right-click it → Set as Startup Project,
  then Ctrl+F5 (recommended, so you can read the per-source fetch summary) or F5. This is
  the one you run whenever you want fresh data.
- **RemoteJobs.Web** - a tiny static-file server that exists purely so you can press F5 and
  get a browser tab instead of typing a static-server command. It always serves
  `src/RemoteJobs.Fetcher/bin/Debug/net9.0/wwwroot` - i.e. whatever the Fetcher last wrote
  in a Debug build - at `http://localhost:5252`, and VS's launch profile opens that URL in
  your browser automatically. It has no other logic: `UseDefaultFiles()` +
  `UseStaticFiles()` pointed at that folder.

Typical flow: run RemoteJobs.Fetcher once (Ctrl+F5) to generate `jobs.json`, then run
RemoteJobs.Web (F5) to browse the result. You can leave RemoteJobs.Web running and just
re-run RemoteJobs.Fetcher whenever you want to refresh the data - no restart needed, just
refresh the browser tab, since RemoteJobs.Web reads the files from disk on every request.

If you'd rather launch both with one F5, right-click the solution → **Set Startup
Projects...** → **Multiple startup projects** → set both to "Start". They'll launch
simultaneously, so the very first page load may 404 on `jobs.json` until the Fetcher
finishes and writes it - just refresh once its console shows `Wrote ...json`.

RemoteJobs.Web is a local dev convenience only, not something intended to be published or
exposed beyond `localhost` - deployment still means copying `wwwroot` to a static host, as
described above.

## Filtering notes / known heuristics

This is intentionally a heuristic system, not a precise one - the raw location string and
raw salary text are always kept alongside the parsed fields so you can eyeball edge cases
in the UI rather than trust the parser blindly.

- **Relevance** matches `.net`, `c#`, `asp.net`, `dotnet`, `blazor` **or** a set of AI/ML
  terms (`ai`, `llm`, `genai`, `nlp`, `machine learning`, `generative ai`, `artificial
  intelligence`, `agentic ai`, `ml engineer`) with word-boundary regexes (so `.net` doesn't
  fire on "internet", and bare `ai` doesn't fire inside "Aiden"). .NET and AI are two
  independent categories, not an intersection - a pure AI/ML role with no .NET in sight is
  in scope, same as a pure .NET role with no AI in sight. It matches against **title and
  tags only, never the free-text description** - some staffing-marketplace listings
  (Lemon.io postings via Remotive/Working Nomads in particular) pad every job's description
  with a boilerplate sentence listing every stack they recruit for ("...React & Golang, PHP
  & Vue, React & .NET..."), which would otherwise false-positive on completely unrelated
  roles. For the same reason, an abnormally long tag list (more than 15 tags - a strong
  signal of the same "we do everything" pattern, since those postings also carry 40+ generic
  tags) is excluded from the tag check entirely. Net effect: matches are precise but
  conservative - a genuinely relevant role that only mentions it deep in a normal-length
  description won't be caught, and a generic "AI" category tag on an otherwise-unrelated
  role (data labeling, transcription) can occasionally over-include. Given how noisy the
  alternative was, that trade-off is intentional.
- **Work mode** (`WorkMode`: Remote / Hybrid / Onsite / Unknown) is *classified*, not used
  to hard-reject a .NET-relevant posting - every match is kept in `jobs.json` and tagged, so
  the UI's work-mode filter (and the Stats page) can show hybrid/onsite volume instead of
  it being silently dropped. Classification has four tiers, per source:
  - *Trusted remote-only* (RemoteOK, Himalayas): these boards only carry remote roles, so
    every match is tagged `Remote` without a text check.
  - *Assumed remote unless flagged* (Remotive, Jobicy, Working Nomads, WeWorkRemotely):
    tagged `Hybrid` if the text contains `hybrid` or a day/frequency count in either digit or
    word form ("3 days a week", "two days a week", "twice a week"); tagged `Onsite` if it
    contains `onsite`/"in office", or phrasing like "based at their site"/"office-based";
    otherwise assumed `Remote`.
  - *Requires explicit remote match* (Reed): a general job board that also carries ordinary
    office jobs, so absence of hybrid/onsite text isn't enough to call it Remote - it's
    tagged `Remote` only if it also **positively** mentions `remote`, "work from home", "WFH",
    or "remote-first"; otherwise `Unknown` (not Onsite - the source simply gave no signal
    either way). A bare "remote" doesn't count if it's immediately followed by a word like
    "monitoring"/"sensing"/"control"/"access"/"support"/"management"/"device(s)"/"system(s)" -
    that's almost always the product being described ("remote monitoring systems"), not the
    job's work arrangement, and counting it caused a real false positive on an onsite role
    that happened to build remote-monitoring software.
  - *Truncated description* (Adzuna, Careerjet): same hybrid/onsite detection as above - a
    hybrid or onsite signal found within the available text is still trusted - but this tier
    **never resolves to `Remote`**, even on an explicit "remote"/"WFH" mention; the best it
    can do on a clean match is `Unknown`. This is a deliberate downgrade, not a bug: Adzuna's
    search API confirmed-truncates `description` to ~500 characters, so a posting whose
    opening paragraph says "WFH" but whose required-office-days detail only appears later -
    e.g. "...joining colleagues in the office twice a week" - would otherwise read as `Remote`
    even though it's genuinely hybrid, because the disqualifying text is outside what the API
    hands us at all (confirmed repeatedly on one real listing). Given this tool's entire
    purpose is telling the truth about "remote," a false `Remote` is worse than a false
    `Unknown` - an `Unknown` posting is still visible and checkable, a false `Remote` just lies
    quietly. Careerjet shares the tier as a precaution: its docs don't mention a length cap,
    but it hasn't been live-tested to rule one out either, and Adzuna's cap wasn't documented
    anywhere until it was found by testing.

  **Exception:** Arbeitnow provides its own structured `remote: true/false` flag, which is
  trusted over any text heuristic - non-remote Arbeitnow postings are filtered out by the
  adapter itself, before this classification ever runs. So unlike the other nine sources,
  Arbeitnow's hybrid/onsite volume isn't visible in the UI filter or the Stats page.
- **Country parsing** is a simple heuristic: comma/slash-split, normalize a handful of
  common variants (UK/US/USA etc.), and treat empty/"Worldwide"/"Anywhere" as `Worldwide`.
  Continent/region names (Europe, LATAM, APAC, EMEA, ...) are kept as their own entries
  rather than expanded into real countries. The country filter matches a posting if its
  countries list contains the selected country **or** contains `Worldwide`.
- **Salary parsing**: structured fields are used directly where a source provides them
  (RemoteOK, Himalayas, Jobicy). For free-text salary fields (Remotive), numbers are
  extracted along with a currency symbol/code and a day-rate/hourly-rate/annual
  classification from surrounding text (e.g. "per day", "/hr"). If nothing meaningful
  parses, `CompType` stays `Unknown` and the UI shows "Not listed" rather than a guess.
- **Cross-currency sorting**: the UI sorts compensation only within the same `CompType`
  (day rate vs. salary are never mixed in one sort), and doesn't attempt cross-currency
  conversion - a $70k and a £70k role both show as their own figures, sorted by raw number
  within their own comp-type bucket. Values aren't converted to a common currency for
  ranking; treat the sort as "biggest-looking number of that type" rather than a precise
  ranking across currencies.
- **Deduplication** is on `(title, company)`, case-insensitive, across all sources - first
  occurrence wins.
