# CapybaraHome

A home task planner, replacing a personal JIRA setup. One ASP.NET Core app serves the
web UI, a REST API and an MCP endpoint over a SQLite file, so the same data is reachable
from a desktop browser, a phone and Claude mobile.

Tasks, meals and the grocery list all live here.

## Layout

| Project | Holds |
| --- | --- |
| `src/CapybaraHome.Domain` | Entities, enums and the rules that go with them |
| `src/CapybaraHome.Application` | Handlers, validation and DTOs |
| `src/CapybaraHome.Infrastructure` | EF Core context, mappings and migrations |
| `src/CapybaraHome.Web` | Blazor UI, the REST API and the MCP endpoint |
| `tests/CapybaraHome.Tests` | xUnit tests |

## Running it

Requires the .NET 10 SDK.

```
dotnet run --project src/CapybaraHome.Web
```

The database is created and migrated on startup, at `capybarahome.db` beside the running
app. `GET /health` answers once it is up.

```
dotnet test
```

For a coverage report locally, the same settings CI uses:

```
dotnet test --settings .github/coverage.runsettings --results-directory ./coverage
```

Every pull request gets a comment with line and branch coverage, broken down by assembly
and by file, against the current tip of the branch it would merge into. The `Build`
workflow measures both sides in the one run: the pull request as GitHub already builds it,
which is the branch merged into that tip, and the tip on its own in a `git worktree`
beside it. The two sides therefore differ by exactly the pull request's own commits, and a
pull request stacked on another branch is compared against that branch, at any depth.

Both figures are measured from source every run, so there is no stored baseline that can
fall behind the branch it claims to describe.

That costs a second build and test run per pull request. It is the price of the stacked
case working: a stored baseline can only be recorded for branches that are pushed to, so
the orphan `coverage-baseline` branch this replaces only ever held `main`, and every
stacked pull request read `n/a`.

## Screens

Every screen is plain server-rendered HTML with ordinary form posts: no page needs an
interactive Blazor circuit, and the whole app works with JavaScript turned off. That is
deliberate — a dead circuit would otherwise leave buttons that do nothing and say nothing.


- **Dashboard** (`/`) — cards you add, rename and remove. A card shows a project's progress
  as a bar, a count of whatever its filter matches, or a short list of those tasks.
- **Board** (`/board`) — four columns, To do / In progress / On hold / Done, with arrows to
  move a task along. Filters by category and by how soon something is due.
- **Tasks** (`/tasks`) — the whole list, filtered by text, category, status, days to the due
  date, or overdue only.
- **Create** — the button in the header goes to `/tasks/new`; the same page edits an existing
  task at `/tasks/{id}`.
- **Automations** (`/automations`) — tasks created on a timer, repeating every *N* days, weeks,
  months or years for any *N*, counted from a start date, at a time of day that defaults to
  midnight. A weekly one can be pinned to a day of the week, which moves it to the first of
  those on or after the start date. **Due in** optionally dates each created task a number of
  days from the day it is created, 0 for the same day; leave it empty for a task with no due
  date. The editor previews the next five
  executions, and an automation can skip its run while a task with the same title is still
  open. Times are the server's own clock, so in a container they follow `TZ`. A run missed
  while the app was down happens once when it is back.
- **Homes** (`/homes`) — the home search: the place you live now, then the candidates ranked by
  score. A property (`/homes/{id}`) holds its rent, parking, cat rent, deposit, a utility
  breakdown by kind, what internet is provided, amenities, pros and cons, notes and pictures,
  and the drive time and distance to each destination, with what a week of driving costs in
  fuel and tolls. **Search settings** (`/homes/settings`) holds the scoring weights, the
  destinations, the priorities, the cars and the toll roads.
- **Morning report** (`/report`) — one page of the day: the forecast, the tasks due within a
  week, what is planned to eat, the next thirty days of your calendars, the holidays, today's
  fixtures and the headlines. **Download PDF** is exactly the sheet that is printed, and
  **Print now** sends it to the printer straight away. **Report settings**
  (`/report/settings`) turns the daily printing on and off, sets the time of day, names the
  printer, the forecast service and the calendars to read, and keeps the task table to the
  categories you tick.
- **Grocery list** (`/grocery`) — the open shopping list. It stays open until you start a
  fresh one yourself, so something thought of mid-week has somewhere to go. **Generate**
  (`/grocery/generate`) asks about each frequent item and each ingredient of the planned
  meals, one tick box apiece, and adds only what is ticked. **Print** (`/grocery/print`)
  is the same list with nothing but the list on it, ready for paper; an earlier list is at
  `/grocery/print/{id}`.
- **Settings** (`/settings`) — categories, frequent grocery items, the accounts, connector
  tokens, and the export,
  import and backup controls. **Import from JIRA** (`/settings/jira`) is the wizard that
  copies a JIRA Cloud project in.
- **Your account** (`/account`) — your own password and your authenticator.

## The home search

Scoring is entirely yours to set, under `/homes/settings`. Every weight starts at zero, so
nothing is scored until you fill them in:

- **Points per dollar a month** and **points per dollar of deposit** — a candidate cheaper
  than the home you live in now earns points, a dearer one loses them. The month is rent plus
  parking plus cat rent plus the utilities.
- **Points per minute of drive**, multiplied by each destination's own weight, against the
  same baseline. Work, driven to every day, is worth setting above one.
- **Points per amenity**, **per pro** and **off per con**.
- Each priority carries its own points, earned by a property you mark as meeting it.

Fees and drive times are differences, so they need a property ticked as *This is where I live
now*; amenities, pros, cons and priorities stand on their own. A drive only scores where both
ends are known, so a destination you have not measured for both places counts for nothing
rather than inventing a saving.

Drive times can always be typed in. To have them measured instead, put an OpenRouteService
address and key under **Routing service**: `https://api.openrouteservice.org` with a key from
their site, or your own instance, which needs no key. The same service places an address the
first time a drive is measured. The key is write-only — the page says whether one is held,
never what it is — and there is a button to forget it.

### Driving cost

Each destination carries how many round trips a week you drive to it — three for a three-day
work week — and any carpool credit a day; each car carries its average mpg and what a gallon
costs now; and each toll road its peak toll. All of it lives under `/homes/settings`. A
property's page then costs its usual week of driving: the fuel in every car, the tolls, and
the carpool credit taken off, a week and over an average month.

The miles come from each drive's distance, which a measured route fills in and which can be
typed in beside the minutes. A measured route also names the tolled roads it uses, which
OpenRouteService marks from OpenStreetMap's toll tags without a price. A toll road under
settings prices every tolled road whose name contains what you type, so `520` covers the
SR 520 bridge, and each crossing is charged its peak toll in both directions. A tolled road
the route used that nothing prices yet is named on the property's page. Tolls come only from
a measured route, so a drive typed in by hand carries none. Driving cost is shown, not scored — it is not part
of the points.

Pictures are kept as files rather than in the database, so an export stays a readable
document. `Photos__Path` says where, `/data/photos` in the container.

## Signing in

CapybaraHome holds its own accounts. There is no identity provider to register and nothing to
configure before the first run.

The first time it starts, every page redirects to `/setup`, where you create the first account:
a username, a name to show and a password of at least ten characters. That account is the one
that can add and remove the others, under Settings. Everyone shares the same tasks.

Passwords are stored as PBKDF2-HMAC-SHA256 hashes, each with its own salt and the iteration
count alongside it. Nothing emails a reset: the first account resets other people's passwords
under Settings, and everyone changes their own under Your account.

An authenticator is optional and per account, set up under Your account. The secret is shown
once as text and as an `otpauth://` link; sign-in asks for a six-digit code from then on. The
setup is not in force until a code confirms it, so a half-finished setup cannot lock anyone out.
Turning it off again needs the account's password.

## Behind a reverse proxy

The app speaks plain HTTP and expects something in front of it to terminate HTTPS. It reads
`X-Forwarded-Proto`, `X-Forwarded-For` and `X-Forwarded-Host`, trusting any hop by default,
which is right for a container only the proxy can reach. To narrow that, list the proxy:

```
ForwardedHeaders__KnownProxies__0=10.0.1.5
ForwardedHeaders__KnownNetworks__0=10.0.0.0/8
```

The keys that encrypt the sign-in cookie are written to `DataProtection__KeyPath`, which the
image sets to `/data/keys`. Keep that on the volume: without it a new container cannot read the
cookies the old one wrote, and everybody is signed out on every image pull.

## The Claude connector

The app serves MCP over streamable HTTP at `/mcp`. It carries the task tools (`search_tasks`,
`get_task`, `create_task`, `update_task`, `list_categories`), the meal tools (`search_meals`,
`get_meal`, `create_meal`, `update_meal`, `create_meal_variant`, `list_meal_tags`), the
home search tools (`get_home_search`, `get_property`, `add_property`, `add_property_note`) and
the grocery tools (`get_grocery_list`, `add_grocery_item`, `remove_grocery_item`,
`start_fresh_grocery_list`, `list_grocery_candidates`).

Mint a token under Settings, then point a custom connector at `https://your-host/mcp` and
send the token as `Authorization: Bearer <token>`. Only a SHA-256 hash of each token is
stored, so the secret is shown once and cannot be recovered. The same token works against
the REST API under `/api`. A token is how the connector signs in; it does not use the sign-in
cookie and is not affected by an authenticator.

## Export, import and backups

- `GET /api/export` downloads the whole database as one JSON document. Settings has the same
  link, and a file picker to load one back in.
- `POST /api/import?mode=Merge|Replace` reads a document back. A merge matches categories by
  name, tasks by JIRA key and automations by title, so re-importing an export of this
  installation updates rather than duplicates. Grocery lists have no such key, so a merge
  loads them only into an installation that holds none; frequent items are matched by name.
- **Import from JIRA** (`/settings/jira`) is a four-step wizard: the address of your JIRA
  Cloud site, the email address of your Atlassian account and an API token; then the project
  to copy, chosen from the ones that account can see; then a confirmation with the issue
  count; then the import, which pulls the issues itself a page at a time. The token is used
  for that one import and is never written to the database, a log or a backup.
- The mapping is [in the docs](docs/jira-import-mapping.md): epics become categories, every
  other issue becomes one task with its JIRA timestamps kept, titles verbatim, and a re-run
  upserts on the JIRA key. Nothing in JIRA is changed. `POST /api/import/jira` takes the same
  issues as JSON for anything scripted.
- Automatic backups write a dated export on a timer when they are turned on:

```
Backups__Enabled=true
Backups__Destination=/data/backups
Backups__Interval=24:00:00
Backups__Keep=14
```

## The morning report

The report is built from this app's own tasks and meals, plus a forecast service, the
calendar feeds you name, a local news page and a national wire. A source that cannot be read
costs the report its own section and nothing else. It is laid out as a Letter-size PDF and
sent to a printer over IPP.

Nothing about it is configured on the host. A new installation has the schedule off and
nothing filled in: `/report/settings` is where you turn the daily printing on and set the
time of day, the printer's host, port, queue and whether it uses both sides of the paper, the
forecast address, the calendars, and the categories the task table is kept to. Ticking no
category carries every category, including any added later.

A calendar is read as one of three kinds: **Upcoming events** are listed with their dates and
places for the next 30 days, **Holidays** by name and date over the same window, and
**Today only** entries under a heading of the calendar's name. A calendar can be switched off
without being deleted.

The time of day is the server's own clock, so in a container it follows `TZ`, and a morning
the app was down for is missed rather than printed late. The page and the download are there
whether or not the schedule is on; with no printer host nothing is printed at all. The
forecast service is the one that answers with `period1Name` through `period4WindDirection`;
with no address set the report leaves the weather out.

## Docker

```
docker build -t capybarahome .
docker run -p 8080:8080 -v capybarahome-data:/data capybarahome
```

The database, the backups, the property pictures and the cookie keys live in `/data`, so that is
the volume to keep.
Open the app and it takes you to `/setup` for the first account. GitHub Actions builds the image
on every pull request and pushes it to `ghcr.io` on a push to `main`.
