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
  score. A property (`/homes/{id}`) holds its size, rent, parking, cat rent, deposit, a utility
  breakdown by kind, what internet is provided, amenities, pros and cons, notes and pictures,
  and the drive time and distance to each destination, with what a week of driving costs in
  fuel and tolls. **Search settings** (`/homes/settings`) holds the scenarios, the scoring
  weights, the destinations, the priorities, the cars and the toll roads.
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
- **Close operations** — on a task's own page, what happens when it is moved to done: call a
  webhook or create another task. A task can carry as many as you like, and they run in the
  order you add them, with what each one last did shown beside it. The created task's due date
  can be an exact date or one worked out from the day you finished the task: in so many days,
  weeks, months or years, the start of the next quarter, or the end of the quarter it was
  finished in. Unless you turn it off, the created task carries the same operations, so closing
  it sets the next one again.
- **Settings** (`/settings`) — categories, frequent grocery items, the accounts, connector
  tokens, and the export,
  import and backup controls. **Import from JIRA** (`/settings/jira`) is the wizard that
  copies a JIRA Cloud project in.
- **Your account** (`/account`) — your own password and your authenticator.

## The home search

### Scenarios

The search can be kept under more than one scenario, and `/homes/settings` switches between
them; with more than one, the board at `/homes` carries a dropdown of its own. A scenario holds
the scoring weights, the destinations with their weight, round trips, parking, credit, car and
multiplier, the cars and the toll roads, so the scores and the driving costs follow whichever one is showing.
Everything else is shared: the properties and their fees, the priorities and how each property
is marked against them, and the routing service, which is one account.

Copying a scenario brings its weights, destinations, cars and toll roads, the car each
destination drives, the toll roads and the fees marked by hand on each drive, and the drive times
already measured, so
only the difference has to be changed; the copy is the one left showing. Deleting one takes its
destinations, cars, tolls and their drive times with it, and the last scenario stays, since the
search has to be scored under something. A search that has never named one is scored under
*Default*, which is everything already set up.

### Scoring

Scoring is entirely yours to set, under `/homes/settings`. Every weight belongs to the scenario
showing, and starts at zero, so nothing is scored until you fill them in:

- **Points per dollar a month** and **points per dollar of deposit** — a candidate cheaper
  than the home you live in now earns points, a dearer one loses them. The month is rent plus
  parking plus cat rent plus the utilities. What a month's driving costs is scored at the same
  points a dollar, on its own line: the fuel of every drive plus the tolls and the parking, less
  the commute credit. A place with a distance still unknown has no driving difference, since the unknown
  drive would make it look cheap.
- **Points per minute of drive**, multiplied by each destination's own weight, against the
  same baseline. Work, driven to every day, is worth setting above one.
- **Points per square foot** larger than the home you live in now; a smaller place loses them.
  Each property also shows its rent a square foot, which is not scored.
- **Points per amenity**, **per pro** and **off per con**.
- Each priority carries its own points, earned by a property you mark as meeting it.

A property can carry the link to the listing it was found on, shown on its page and opened in a
new tab. Only an http or https address is kept, and a plain host is read as https.

A destination can also carry a drive time multiplier, for a commute the routing service reckons
shorter than the traffic you meet. It stretches every drive to that destination, across every
property and whether the time was measured or typed in, and the stretched minutes are what the
page shows and the score weighs.

Fees, size and drive times are differences, so they need a property ticked as *This is where I live
now*; amenities, pros, cons and priorities stand on their own. A drive only scores where both
ends are known, so a destination you have not measured for both places counts for nothing
rather than inventing a saving.

Drive times can always be typed in. To have them measured instead, put an OpenRouteService
address and key under **Routing service**: `https://api.heigit.org` with a key from
their site, or your own instance, which needs no key. HeiGIT retired
`api.openrouteservice.org`, and the hosted API serves routing under `/openrouteservice` and
geocoding under `/pelias/v1`, which the app works out from the address it is given; an address
still saved under the old host is called at the new one. The same service places an address the
first time a drive is measured. The key is write-only — the page says whether one is held,
never what it is — and there is a button to forget it.

### Driving cost

Each destination carries how many round trips a week you drive to it — three for a three-day
work week, and fractions are allowed, so somewhere visited every third week is `0.33` — the car
that drive is made in, what parking there costs a day, and any commute credit a day, which is
what a day there pays back; each car carries its average mpg and what a gallon costs now; and
each toll road its peak toll. All of it lives under `/homes/settings`. A property's page then
costs its usual week of driving, one line per drive: the fuel, the tolls, the parking, and the
commute credit taken off, a week and over an average month.

One property can sit where the commute to a destination works differently — transit is there to
take, so nothing is parked and taking it is paid for. Its drive carries a parking and a commute
credit of its own, beside the minutes on the property's page. Left empty the drive follows the
destination; filled in, that drive alone charges its own, and the destination and every other
property are untouched. Measuring the drive again leaves them standing, since a measurement says
nothing about what parking costs there.

A destination left with no car of its own is shared out: its fuel is what the drive averages over
every car that has both a mileage and a fuel price, which is the one to leave for a commute you
take turns driving. Naming a car charges that drive to it alone, and a named car with no mileage
or no fuel price leaves that drive's fuel uncounted, which the page says rather than passing over.

The miles come from each drive's distance, which a measured route fills in and which can be
typed in beside the minutes. A measured route also names the tolled roads it uses, which
OpenRouteService marks from OpenStreetMap's toll tags without a price. A toll road under
settings prices every tolled road whose name contains what you type, so `520` covers the
SR 520 bridge, and each crossing is charged its peak toll in both directions. A tolled road
the route used that nothing prices yet is named on the property's page.

Each drive on a property's page carries a tick box per toll road. Left alone they follow the
measured route; tick one the route did not use — the tolled way you would take in practice,
for much the same time — and the ticks decide instead, for that drive, measurement or no
measurement. Match them back to the route and it takes the drive over again. What the month's
driving comes to is scored, at the points a dollar a month already carries.

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

### Signing in with OAuth

A Claude custom connector offers OAuth or no authentication; it has no field for a fixed
header, so a hand-minted token cannot be used with one. The app is therefore its own OAuth 2.1
authorization server as well as the protected resource, with no external identity provider
involved. Add a custom connector pointed at `https://your-host/mcp`, leave the client id and
secret empty, and the rest is discovery:

- `GET /.well-known/oauth-protected-resource` (and `/.well-known/oauth-protected-resource/mcp`)
  names `/mcp` as the resource and this host as its authorization server. An unauthenticated
  call to `/mcp` answers `401` with a `WWW-Authenticate` header pointing at that document.
- `GET /.well-known/oauth-authorization-server` names the authorize, token, registration and
  revocation endpoints. Only the authorization code grant with PKCE `S256` is offered, plus
  refresh tokens.
- `POST /oauth/register` registers the connector and hands back a client id (RFC 7591). A
  client that asks for `client_secret_post` or `client_secret_basic` is given a secret too; one
  that asks for `none` relies on PKCE. **Registration needs no sign-in**, so the callback a
  client may name is held to an allowlist: `OAuth__AllowedRedirectUris__0` and so on, defaulting
  to Claude's own connector callbacks. What is compared is the scheme, host and port, not the
  path. Without it, anyone who can reach the app could register a client calling itself Claude
  with a callback of their own and send you a consent link. Registrations are also capped, and
  the oldest one nobody ever approved is dropped to make room.
- `GET /oauth/authorize` is the consent screen. It needs a signed-in browser, so it goes through
  this installation's own username, password and authenticator code first, and then names the
  app and the account it would act as.
- `POST /oauth/token` trades the code for an access token that lasts an hour and a refresh token
  that lasts sixty days. A refresh rotates both; offering a spent code or refresh token back is
  treated as a leak and drops the whole grant. An access token reaches **`/mcp` only** — the
  grant is for the home task tools, and `/api` carries the whole export and an import that
  replaces the data, which those tools cannot do.
- `POST /oauth/revoke` retires a token (RFC 7009), and **Connected apps** under Settings lists
  everything that has been authorized and revokes it. Changing or resetting a password, or
  turning an authenticator off, revokes that account's grants as well, so a sixty-day refresh
  token cannot outlive the credentials it was granted under.

If a proxy serves the app under a different host or scheme than the app sees, set
`OAuth__Issuer` to the address clients reach it at; otherwise the documents follow the request.
Behind a proxy, also set `ForwardedHeaders__KnownProxies__0` to its address and stop publishing
the container's port directly: with no known proxy the app trusts `X-Forwarded-*` from anyone,
which lets a caller reaching it straight on the LAN fake its address past the per-caller rate
limit and change the host the metadata names.

Only the client's servers call the discovery, registration, token and revocation endpoints, so
those are what has to face the internet. The consent page is visited only by the person
approving the client, in their own browser, so it can stay on an address that does not: set
`OAuth__AuthorizationEndpoint` to it (for example
`https://capybarahome.internal.example/oauth/authorize`) and publish only `/mcp`,
`/oauth/register`, `/oauth/token`, `/oauth/revoke` and `/.well-known/oauth-*`. The sign-in page
then never faces the internet, and a client can only be approved from somewhere that address is
reachable. Set `OAuth__Issuer` alongside it: the consent page checks the resource a client asks
for against the issuer, and on the internal address the request alone would name the wrong one.

### Signing in with a token

A client that sends a bearer header of its own — a script, Home Assistant, curl — mints a token
under Settings and sends it as `Authorization: Bearer <token>`. Only a SHA-256 hash of each
token is stored, so the secret is shown once and cannot be recovered. The same token works
against the REST API under `/api`. Neither kind of token uses the sign-in cookie, and neither is
affected by an authenticator once issued.

## Home Assistant

A HACS-installable custom integration lives in
[`custom_components/capybarahome`](custom_components/capybarahome/README.md). It adds a
`capybarahome.create_task` action and a `capybarahome.task_completed` trigger, and signs in
with the same API tokens as the connector. Add this repository to HACS as a custom
repository of type Integration to install it.

The trigger is a poll: the integration asks once a minute for the tasks completed since its
last question, which is what `GET /api/tasks?completedSince=` answers.

## Webhooks

A webhook operation sends the completed task to a URL you type. A `POST` carries the title,
notes, category, due date and completion time as JSON; a `GET` just fetches the URL. The call
goes out within a few seconds of the task being closed rather than while the page is saving,
and it is retried, further apart each time, until the target accepts it or five attempts have
failed. What happened is on the task's own page.

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
  installation updates rather than duplicates. Grocery lists have no such key, so a merge
  loads them only into an installation that holds none; frequent items are matched by name.
  A task's close operations come across with it; the runs they have already had do not, being
  history rather than configuration.
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
