# CapybaraHome

A home task planner, replacing a personal JIRA setup. One ASP.NET Core app serves the
web UI, a REST API and an MCP endpoint over a SQLite file, so the same data is reachable
from a desktop browser, a phone and Claude mobile.

Meal tracking is designed but not built yet; tasks come first.

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
  midnight. **Due in** optionally dates each created task a number of days from the day it is
  created; leave it empty for a task with no due date. The editor previews the next five
  executions, and an automation can skip its run while a task with the same title is still
  open. Times are the server's own clock, so in a container they follow `TZ`. A run missed
  while the app was down happens once when it is back.
- **Homes** (`/homes`) — the home search: the place you live now, then the candidates ranked by
  score. A property (`/homes/{id}`) holds its rent, parking, cat rent, deposit, a utility
  breakdown by kind, what internet is provided, amenities, pros and cons, notes and pictures,
  and the drive time to each destination. **Search settings** (`/homes/settings`) holds the
  scoring weights, the destinations and the priorities.
- **Settings** (`/settings`) — categories, the accounts, connector tokens, and the export,
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

The app serves MCP over streamable HTTP at `/mcp`, with tools for the tasks, the meals and the
home search: `search_tasks`, `get_task`, `create_task`, `update_task`, `list_categories`, the
meal tools, and `get_home_search`, `get_property`, `add_property` and `add_property_note`.

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
  installation updates rather than duplicates.
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

## Docker

```
docker build -t capybarahome .
docker run -p 8080:8080 -v capybarahome-data:/data capybarahome
```

The database, the backups, the property pictures and the cookie keys live in `/data`, so that is
the volume to keep.
Open the app and it takes you to `/setup` for the first account. GitHub Actions builds the image
on every pull request and pushes it to `ghcr.io` on a push to `main`.
