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
