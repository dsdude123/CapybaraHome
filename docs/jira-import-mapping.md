# JIRA → CapybaraHome import mapping

Plan for migrating the existing JIRA data into CapybaraHome. Read-only profile of the
live JIRA data taken **2026-10-17**; nothing in JIRA was changed.

**Privacy.** This document deliberately carries no category, task, chore or meal names.
Issue keys (`CAPY-1`, `FOOD-25`) and JIRA project ids are fine and are used instead, so
anything here can be read against the live data without the names leaving JIRA.

**Scope.** Food is out of scope for the initial implementation, so **CAPY is phase 1**
and the meal side is phase 2. The FOOD profile and mapping stay in this document because
the data was already read and the model should not be designed in a way that locks meals
out, but nothing in the FOOD sections blocks phase 1.

**The import is a straight copy.** Every CAPY task comes across as it stands, one JIRA
issue to one CapybaraHome task. No deduplication, no grouping of repeated chores into
rules, no judgement about what an automation created.

Repeating chores are **out of scope for this plan**. How CapybaraHome handles them is a
later decision, and nothing in this document plans for it.

Source site: `csmediaseattle.atlassian.net` (cloud id `cd5e9659-73df-41cc-9244-b7641efe918e`),
projects **CAPY** (tasks) and **FOOD** (meals). Both are team-managed ("next-gen")
software projects, single user, timezone `America/Los_Angeles`.

## 1. What is actually in JIRA today

Counts below are the full data set, not a sample (949 CAPY issues, 121 FOOD issues).

### CAPY — 949 issues

| Dimension | Values |
|---|---|
| Issue types | Task 899, Subtask 42, Epic 8 |
| Statuses | Done 754, To Do 182, In Progress 10, **On Hold 3** |
| Hierarchy | Epic → Task → Subtask (3 levels). 683 tasks sit under an epic, **216 tasks have no epic**, 42 subtasks sit under 7 parent tasks |
| Due date | set on 744 (205 blank); range 2020-05-10 → 2027-04-10; only 20 are in the future |
| Resolution date | set on all 754 Done issues |
| Description | 144 non-empty, median 90 chars, max 1,948 — links, lists, bullet checklists (some lines struck through to mean "done") |
| Labels | effectively unused: 1 issue carries one |
| Priority | effectively unused: 944 of 949 are the default Medium |
| Start date (custom) | 3 issues |
| Attachments | 2 issues |
| Issue links | none |
| Created | 2024-04-30 → 2026-10-17 (44 issues predate the current epic set) |

The 8 epics are two different things mixed together — five standing categories and three
finite projects:

| Epic | Children | Nature |
|---|---|---|
| CAPY-1 | 501 | standing category |
| CAPY-10 | 76 | standing category |
| CAPY-104 | 33 | finite project |
| CAPY-11 | 30 | standing category |
| CAPY-782 | 21 | finite project |
| CAPY-316 | 10 | finite project, all Done |
| CAPY-253 | 7 | standing category |
| CAPY-63 | 5 | standing category |

### FOOD — 121 issues (phase 2)

| Dimension | Values |
|---|---|
| Issue types | Task only (no epics or subtasks used) |
| Statuses | Pooled 98, Available 10, Queued 5, Planned 3, Planned Shared 2, Closed 3 |
| Labels | 18 distinct tags on 114 meals (7 untagged), 1–6 tags each, median 3. They fall into four facets: cuisine, protein, effort and trait |
| Frequency (`customfield_10135`, number) | set on 74 meals, in days. Legacy, see below |
| Start date (`customfield_10015`, date) | set on 39 meals. Legacy, see below |
| Due date | 1 meal (FOOD-105, a stale 2022 value) |
| Description | 48 non-empty — 22 hold a full recipe (max 9,013 chars), 21 are short notes |
| Resolution date | 3 (the Closed ones) |
| Attachments | 3 meals (FOOD-96, FOOD-107, FOOD-108) |
| Comments | 3 in the whole project |
| Priority / issue links | unused |

**Frequency and Start date are legacy.** They are left over from a retired automation,
which set Start date to the next date a meal became eligible again — last eaten plus
Frequency days (clearest case: FOOD-25, resolved 2025-11-11, Frequency 365, Start date
2026-11-12). That automation is gone, meals are now found by sorting on last updated, so
neither field is imported and the values still sitting on 74 and 39 meals are stale.

### Repeated task titles

Worth knowing when reading the counts: JIRA has no recurring issues, so a chore that
repeats exists as one issue per occurrence, created on the day it is due. 389 of the 949
CAPY issues share a title with at least one other, the largest such group being 114
issues, and a few titles differ only by spelling or a typo. The import copies all of it
verbatim: one issue, one task, no grouping and no cleanup.

## 2. Target model

The design doc owns the model; this is only the set of places the JIRA data needs to
land, so the mapping tables below have something to point at. The one shape question the
import actually cares about is where the 42 JIRA subtasks go: a parent link, or ordinary
tasks with the parent's title in the notes.

Field names are C#/EF-friendly; table names are suggestions, not decided.

**Tasks side**

- `Category` — one row per standing CAPY epic, plus an `IsProject` flag and optional date
  window for the finite ones. One category named "Uncategorized" absorbs the 216
  epic-less tasks.
- `Task` — Title, Notes (markdown), CategoryId (nullable), ParentTaskId (nullable, if
  subtasks stay), Status, DueDate (nullable), CompletedAt (nullable), CreatedAt,
  UpdatedAt, JiraKey (nullable, unique).
- `TaskStatus` enum — `ToDo`, `InProgress`, `OnHold`, `Done`.

**Meals side**

- `Meal` — Name, Directions (markdown, nullable), Status, IsOneTime, JiraKey, CreatedAt,
  UpdatedAt, `NeedsIngredientSplit` (bool). **No recipe blob:** directions and
  ingredients are separate, and ingredients are their own rows (below) so that future
  planning can work with quantities.
- `MealIngredient` — MealId, Quantity (string, e.g. "2 cups"), IngredientName, SortOrder.
  One row per ingredient, quantity and name in separate columns on purpose.
- `MealTag` + `MealTags` join — the 18 tags, each with a `Facet` column (Cuisine,
  Protein, Effort, Trait) so the UI can filter by facet instead of a flat tag soup.
- `MealStatus` enum — `Pooled`, `Available`, `Queued`, `Planned`, `PlannedShared`,
  `Closed`.
- No meal date fields and no rotation engine. `UpdatedAt` is the exception that is not a
  user-set date: it is a column the app keeps, **shown in the meal list in the UI and
  sortable**, because sorting on it is how the oldest meals get found.

`JiraKey` on both sides is what makes the import re-runnable (§7) and keeps a trail
back to the original issue.

## 3. CAPY field mapping

| JIRA field | CapybaraHome | Notes |
|---|---|---|
| `key` (e.g. CAPY-317) | `Task.JiraKey` | Keep for traceability and idempotency. Keys are sparse; do not use as the primary key |
| `summary` | `Task.Title` | Trim whitespace |
| `description` (ADF) | `Task.Notes` | Convert ADF → markdown. Struck-through bullets are completed checklist lines; keep them as `~~text~~` unless checklists get a real model (§6, item 5) |
| `issuetype` = Epic | `Category` row | Not a task |
| `issuetype` = Task | `Task` | — |
| `issuetype` = Subtask | `Task` with `ParentTaskId` | 42 rows, under 7 parents |
| `parent` (epic) | `Task.CategoryId` | Blank → "Uncategorized" (216 tasks) |
| `parent` (task) | `Task.ParentTaskId` | — |
| `status` | `Task.Status` | See §4 |
| `duedate` | `Task.DueDate` | Date only, no time. Nullable (205 blanks) |
| `resolutiondate` | `Task.CompletedAt` | Present for all 754 Done; convert to date in `America/Los_Angeles` |
| `created` / `updated` | `Task.CreatedAt` / `UpdatedAt` | Preserve, do not stamp import time |
| repeated `summary` groups | nothing special | Each issue imports as its own task (§1) |
| `labels` | drop | 1 issue; carry it as a note if wanted |
| `priority` | drop | 944/949 default Medium |
| `assignee` / `reporter` | drop | Single-user app |
| `customfield_10015` Start date | drop | 3 issues; no model need |
| `attachment` | manual | 2 files; re-attach by hand or skip |
| Rank, Team, Flagged, Development, worklog, votes, watches | drop | JIRA plumbing with no home-app meaning |

## 4. Status mapping

**CAPY**

| JIRA status (count) | CapybaraHome | Notes |
|---|---|---|
| To Do (182) | `ToDo` | — |
| In Progress (10) | `InProgress` | — |
| On Hold (3) | `OnHold` | Easy to miss — all 3 sit under one epic. Needs its own state, not a fold into ToDo |
| Done (754) | `Done` | `CompletedAt` from `resolutiondate` |

**FOOD** (phase 2) — the six statuses are a real workflow, not a to-do/done pair, so they
map one-to-one:

| JIRA status (count) | CapybaraHome | Semantics |
|---|---|---|
| Pooled (98) | `Pooled` | The full catalogue; not currently in the rotation |
| Available (10) | `Available` | **The ingredients for it are in stock** |
| Queued (5) | `Queued` | Short-listed for an upcoming week |
| Planned (3) | `Planned` | Picked for the next cook |
| Planned Shared (2) | `PlannedShared` | Planned, with someone else involved — confirm (§6 item 2) |
| Closed (3) | `Closed` | Retired, or a one-off that has been eaten |

## 5. FOOD field mapping (phase 2)

| JIRA field | CapybaraHome | Notes |
|---|---|---|
| `key` (FOOD-n) | `Meal.JiraKey` | — |
| `summary` | `Meal.Name` | Strip a leading `[One-Time]` marker and set `IsOneTime` (1 meal today: FOOD-75) |
| `description` (ADF) | `Meal.Directions`, plus `NeedsIngredientSplit = true` | ADF → markdown into Directions as-is. **The importer does not parse ingredients out of it.** The 48 meals with a description come in flagged, and the app prompts Conner to split each one into directions and ingredient rows by hand. An empty rich-text field can come back as `{"type":"doc","version":1,"content":[]}` instead of null (seen on FOOD-12) — import that as NULL and leave the flag false |
| `status` | `Meal.Status` | See §4 |
| `labels` | `MealTags` | 18 tags; assign each a facet (Cuisine / Protein / Effort / Trait) |
| `customfield_10135` Frequency | drop | Legacy, from the retired automation (§1) |
| `customfield_10015` Start date | drop | Legacy (§1); food needs no dates |
| `resolutiondate` | drop | Only 3 meals carry one, and food needs no dates |
| `duedate` | drop | Set on exactly one meal, a stale 2022 value rather than a plan |
| `created` / `updated` | `Meal.CreatedAt` / `UpdatedAt` | `UpdatedAt` is shown and sortable in the UI (§2) |
| `attachment` | manual | 3 meals; re-upload by hand |
| `comment` | drop | 3 comments total; copy their text into the directions if they matter |

## 6. Gaps and decisions needed

1. ~~Recurrence history depth~~ **Not a question.** All 949 issues come across as tasks.
   The importer derives nothing and groups nothing.
2. **"Planned Shared" semantics** *(phase 2, not blocking)*. Planned with another person,
   versus a dish taken somewhere to share, changes whether meals want a `SharedWith`
   field. The other five statuses are settled (§4).
3. **Ingredient splitting is manual** *(phase 2)*. 48 meals arrive with their description
   in `Directions` and `NeedsIngredientSplit` set; the app needs a screen that walks
   through them one at a time. Open: whether the prompt offers a suggested split for
   Conner to correct, or a blank pair of fields.
4. **Finite epics vs standing categories.** Three of the eight epics are finite projects
   (one of them fully Done), not standing categories. Plan is one `Category` table with
   an `IsProject` flag, so they can be archived out of the everyday view.
5. **Checklists inside descriptions.** Several CAPY descriptions are bullet lists with
   struck-through lines used as checkboxes (CAPY-114 is the clearest). Markdown keeps
   them readable; a real `ChecklistItem` model would make them usable. Not in scope of
   this plan — flagged so the data model does not foreclose it.
6. ~~Title normalization~~ **Settled.** Titles copy verbatim, typos and duplicate
   spellings included; tidy them in the app afterwards if they annoy.
7. **Software and homelab epics.** Three epics, 113 tasks between them, are software or
   homelab work rather than home chores. They fit the same model, but if CapybaraHome is
   meant to be home-only they could stay in JIRA instead. Default: import them.
8. **Attachments (5 files) and comments (3)** have no automated path worth building;
   handle by hand after the import.
9. **Pre-epic backlog.** 44 CAPY issues predate the current epic set (2024-04 onward)
   and mostly land in "Uncategorized". They import like everything else; worth a skim in
   the app afterwards for things that are simply dead.

## 7. Import mechanics

1. **Export.** Read-only, from JIRA REST v3 (`/rest/api/3/search/jql`) or the Rovo MCP
   tools, paging 100 at a time. JQL per project: `project = CAPY ORDER BY created ASC`,
   `project = FOOD ORDER BY created ASC`. Request `fields=*all` for FOOD (the custom
   fields matter) and the explicit field list from §3 for CAPY. Page by `nextPageToken`,
   or by `created` windows if a page token is awkward — windows must be checked for
   overflow past 100 results.
2. **Snapshot to disk as JSON**, one file per project, and import from the files. The
   import is then reproducible and JIRA is touched exactly once. The snapshot holds the
   real names, so it stays out of the repository.
3. **Convert ADF to markdown** in one place. The MCP tools can return markdown
   directly (`responseContentFormat: "markdown"`), which is the cheaper path than
   writing an ADF walker.
4. **Load order.** Phase 1: categories → tasks → subtasks (if subtasks stay). Phase 2
   adds meal tags → meals. Tasks reference categories; subtasks reference tasks. Phase 1
   needs no FOOD export, so the meal queries can be left until then.
5. **Idempotency.** Upsert on `JiraKey`, so a re-run after a fix updates rather than
   duplicates. Run against a scratch database first.
6. **Verification** — the import is done when these match:
   - CAPY: 8 categories, 899 + 42 = 941 tasks, 216 in Uncategorized, 754 with
     `CompletedAt`, 744 with a due date. Nothing collapsed, so the task count is the
     issue count.
   - FOOD (phase 2): 121 meals, 18 tags, 114 meals with at least one tag, 48 with
     directions and `NeedsIngredientSplit` set, status split 98/10/5/3/2/3.
   - Spot-check by hand, phase 1: CAPY-1 (epic with 501 children), CAPY-105 (task with 10
     subtasks), CAPY-114 (struck-through checklist). In phase 2: FOOD-69 (longest recipe,
     ~9 KB) and FOOD-75 (`[One-Time]`).
7. **Keep JIRA read-only** until CapybaraHome has been used for a week or two, then
   archive the projects rather than deleting them.
