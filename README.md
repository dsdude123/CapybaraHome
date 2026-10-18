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

## Screens

- **Dashboard** (`/`) — cards you add, rename and remove. A card shows a project's progress
  as a bar, a count of whatever its filter matches, or a short list of those tasks.
- **Board** (`/board`) — four columns, To do / In progress / On hold / Done, with arrows to
  move a task along. Filters by category and by how soon something is due.
- **Tasks** (`/tasks`) — the whole list, filtered by text, category, status, days to the due
  date, or overdue only.
- **Create** — the button in the header goes to `/tasks/new`; the same page edits an existing
  task at `/tasks/{id}`.
- **Settings** (`/settings`) — categories, people and invites, connector tokens, and the
  export, import and backup controls.

## Signing in

Sign-in is OpenID Connect against whichever provider you register the app with. Nothing
about it lives in the repository; it is read from configuration, so in practice from the
environment:

```
Authentication__Oidc__Authority=https://your-provider.example
Authentication__Oidc__ClientId=...
Authentication__Oidc__ClientSecret=...
```

The redirect URI to register is `https://your-host/signin-oidc`.

Everyone shares the same tasks. The first identity to sign in claims the installation; after
that, an email address has to hold an open invite, which an existing user issues under
Settings. Anyone else is turned away at `/access-denied`.

## The Claude connector

The app serves MCP over streamable HTTP at `/mcp`, with five tools: `search_tasks`,
`get_task`, `create_task`, `update_task` and `list_categories`.

Mint a token under Settings, then point a custom connector at `https://your-host/mcp` and
send the token as `Authorization: Bearer <token>`. Only a SHA-256 hash of each token is
stored, so the secret is shown once and cannot be recovered. The same token works against
the REST API under `/api`.

## Export, import and backups

- `GET /api/export` downloads the whole database as one JSON document; Settings has the same
  link and a box to paste one back in.
- `POST /api/import?mode=Merge|Replace` reads a document back. A merge matches categories by
  name and tasks by JIRA key, so re-importing an export of this installation updates rather
  than duplicates.
- `POST /api/import/jira` loads a read-only snapshot of the JIRA project, as
  [the mapping](docs/jira-import-mapping.md) describes: epics become categories, every issue
  becomes one task with its JIRA timestamps kept, and a re-run upserts on the JIRA key.
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
docker run -p 8080:8080 -v capybarahome-data:/data \
  -e Authentication__Oidc__Authority=... \
  -e Authentication__Oidc__ClientId=... \
  -e Authentication__Oidc__ClientSecret=... \
  capybarahome
```

The database and the backups live in `/data`, so that is the volume to keep. GitHub Actions
builds the image on every pull request and pushes it to `ghcr.io` on a push to `main`.
