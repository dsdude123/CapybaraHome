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

Every pull request gets a comment with line and branch coverage on the branch against the
latest `main` build, broken down by assembly and by file. It comes from the `Build`
workflow: a push to `main` records its cobertura report on the orphan `coverage-baseline`
branch, one `<branch>.tar.gz` force-pushed over the last, and a pull request reads the
archive for its base branch from there. The branch is used rather than a build artifact
because artifacts on a public repository expire after at most 90 days, which would lose the
baseline over a long quiet spell.

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
- **Automations** (`/automations`) — tasks created on a timer: every day, week, two weeks,
  month, three months or year, counted from a start date, at a time of day that defaults to
  midnight. The editor previews the next five executions, and an automation can skip its run
  while a task with the same title is still open. Times are the server's own clock, so in a
  container they follow `TZ`. A run missed while the app was down happens once when it is back.
- **Close operations** — on a task's own page, what happens when it is moved to done: call a
  webhook, create another task, or produce a document. A task can carry as many as you like,
  and they run in the order you add them. The created task's due date can be an exact date or
  one worked out from the day you finished the task: in so many days, weeks, months or years,
  the start of the next quarter, or the end of the quarter it was finished in. Unless you turn
  it off, the created task carries the same operations, so closing it sets the next one again.
- **Documents** (`/documents`) — the documents those operations produced, each on a page laid
  out to print, with the recent operations and anything that failed listed underneath.
- **Settings** (`/settings`) — categories, the accounts, connector tokens, and the export,
  import and backup controls. **Import from JIRA** (`/settings/jira`) is the wizard that
  copies a JIRA Cloud project in.
- **Your account** (`/account`) — your own password and your authenticator.

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

The app serves MCP over streamable HTTP at `/mcp`, with five tools: `search_tasks`,
`get_task`, `create_task`, `update_task` and `list_categories`.

Mint a token under Settings, then point a custom connector at `https://your-host/mcp` and
send the token as `Authorization: Bearer <token>`. Only a SHA-256 hash of each token is
stored, so the secret is shown once and cannot be recovered. The same token works against
the REST API under `/api`. A token is how the connector signs in; it does not use the sign-in
cookie and is not affected by an authenticator.

## Webhooks

A webhook operation sends the completed task to a URL you type. A `POST` carries the title,
notes, category, due date and completion time as JSON; a `GET` just fetches the URL. The call
goes out within a few seconds of the task being closed rather than while the page is saving,
and it is retried, further apart each time, until the target accepts it or five attempts have
failed. What happened is on the task's own page and under Documents.

Redirects are not followed, and a call is refused if the address is the server itself, a
link-local address such as a cloud metadata service, or multicast; the address is checked again
when the connection is made, so a host name cannot be re-pointed at one of those after it is
saved. Addresses on your own network are allowed, which is the ordinary case here. There is no
place to put a header or a token on a webhook, so put any secret the target needs in the URL.

## Export, import and backups

- `GET /api/export` downloads the whole database as one JSON document. Settings has the same
  link, and a file picker to load one back in.
- `POST /api/import?mode=Merge|Replace` reads a document back. A merge matches categories by
  name, tasks by JIRA key and automations by title, so re-importing an export of this
  installation updates rather than duplicates. A task's close operations come across with it;
  the runs they have already had do not, being history rather than configuration.
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

The database, the backups and the cookie keys live in `/data`, so that is the volume to keep.
Open the app and it takes you to `/setup` for the first account. GitHub Actions builds the image
on every pull request and pushes it to `ghcr.io` on a push to `main`.
