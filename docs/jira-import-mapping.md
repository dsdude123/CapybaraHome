# JIRA → CapybaraHome import mapping

Plan for migrating the existing JIRA data into CapybaraHome. The live data was profiled
read-only on **2026-10-17**; nothing in JIRA was changed.

**Privacy.** This document carries no information about the source data itself — no site
or account identifiers, no category, task, chore or meal names, no counts and no issue
keys. It describes only the shape of the mapping: which JIRA field becomes which
CapybaraHome field. Project ids (CAPY for tasks, FOOD for meals) are used as labels for
the two sides.

**Scope.** Food is out of scope for the initial implementation, so **CAPY is phase 1**
and the meal side is phase 2. The FOOD mapping stays in this document so the model is not
designed in a way that locks meals out, but nothing in the FOOD sections blocks phase 1.

**The import is a straight copy.** Every CAPY task comes across as it stands, one JIRA
issue to one CapybaraHome task. No deduplication, no grouping of repeated chores into
rules, no judgement about what an automation created.

Repeating chores are **out of scope for this plan**. How CapybaraHome handles them is a
later decision, and nothing in this document plans for it.

## 1. Target model

The design doc owns the model; this is only the set of places the JIRA data needs to
land, so the mapping tables below have something to point at. The one shape question the
import actually cares about is where JIRA subtasks go: a parent link, or ordinary tasks
with the parent's title in the notes.

Field names are C#/EF-friendly; table names are suggestions, not decided.

**Tasks side**

- `Category` — one row per CAPY epic, all of them treated the same. One category named
  "Uncategorized" absorbs the tasks that sit under no epic.
- `Task` — Title, Notes (markdown), CategoryId (nullable), ParentTaskId (nullable, if
  subtasks stay), Status, DueDate (nullable), CompletedAt (nullable), CreatedAt,
  UpdatedAt, JiraKey (nullable, unique).
- `TaskStatus` enum — `ToDo`, `InProgress`, `OnHold`, `Done`.

**Meals side**

- `Meal` — Name, Directions (markdown, nullable), Status, IsOneTime, JiraKey, CreatedAt,
  UpdatedAt, `NeedsIngredientSplit` (bool), `ImportedText` (nullable). **No recipe blob:**
  directions and ingredients are separate, and ingredients are their own rows (below) so
  that future planning can work with quantities. `ImportedText` holds the original
  imported description untouched; it is what "Clear all" on the split screen falls back
  to, and it can be dropped once a meal's split is confirmed.
- `MealIngredient` — MealId, Quantity (string, e.g. "2 cups"), IngredientName, SortOrder.
  One row per ingredient, quantity and name in separate columns on purpose.
- `MealTag` + `MealTags` join — one row per JIRA label, each with a `Facet` column
  (Cuisine, Protein, Effort, Trait) so the UI can filter by facet instead of a flat tag
  soup.
- `MealStatus` enum — `Pooled`, `Available`, `Queued`, `Planned`, `PlannedShared`,
  `Closed`.
- No meal date fields and no rotation engine. `UpdatedAt` is the exception that is not a
  user-set date: it is a column the app keeps, **shown in the meal list in the UI and
  sortable**, because sorting on it is how the oldest meals get found.

`JiraKey` on both sides is what makes the import re-runnable (§5) and keeps a trail back
to the original issue.

## 2. CAPY field mapping

| JIRA field | CapybaraHome | Notes |
|---|---|---|
| `key` | `Task.JiraKey` | Keep for traceability and idempotency. Keys are sparse; do not use as the primary key |
| `summary` | `Task.Title` | Trim whitespace |
| `description` (ADF) | `Task.Notes` | Convert ADF → markdown. Struck-through bullets are completed checklist lines; keep them as `~~text~~` (§4, item 3) |
| `issuetype` = Epic | `Category` row | Not a task |
| `issuetype` = Task | `Task` | — |
| `issuetype` = Subtask | `Task` with `ParentTaskId` | — |
| `parent` (epic) | `Task.CategoryId` | Blank → "Uncategorized" |
| `parent` (task) | `Task.ParentTaskId` | — |
| `status` | `Task.Status` | See §3 |
| `duedate` | `Task.DueDate` | Date only, no time. Nullable |
| `resolutiondate` | `Task.CompletedAt` | Convert to a date in the site's timezone |
| `created` / `updated` | `Task.CreatedAt` / `UpdatedAt` | Preserve, do not stamp import time |
| repeated `summary` values | nothing special | Each issue imports as its own task |
| `labels` | drop | Effectively unused on this side |
| `priority` | drop | Effectively unused; all but a handful are the default |
| `assignee` / `reporter` | drop | Single-user app |
| `customfield_10015` Start date | drop | Barely used; no model need |
| `attachment` | manual | Re-attach by hand or skip |
| Rank, Team, Flagged, Development, worklog, votes, watches | drop | JIRA plumbing with no home-app meaning |

## 3. Status mapping

**CAPY**

| JIRA status | CapybaraHome | Notes |
|---|---|---|
| To Do | `ToDo` | — |
| In Progress | `InProgress` | — |
| On Hold | `OnHold` | Easy to miss. Needs its own state, not a fold into ToDo |
| Done | `Done` | `CompletedAt` from `resolutiondate` |

**FOOD** (phase 2) — six statuses, each mapping one-to-one onto a `MealStatus` value.
They are statuses and nothing more; the import carries them across unchanged and reads
nothing further into them.

| JIRA status | CapybaraHome |
|---|---|
| Pooled | `Pooled` |
| Available | `Available` — the ingredients for it are in stock |
| Queued | `Queued` |
| Planned | `Planned` |
| Planned Shared | `PlannedShared` |
| Closed | `Closed` |

## 4. FOOD field mapping (phase 2)

| JIRA field | CapybaraHome | Notes |
|---|---|---|
| `key` | `Meal.JiraKey` | — |
| `summary` | `Meal.Name` | Strip a leading `[One-Time]` marker and set `IsOneTime` |
| `description` (ADF) | `Meal.ImportedText` and `Meal.Directions`, plus `NeedsIngredientSplit = true` | ADF → markdown, stored in both: `ImportedText` as the untouched original, `Directions` as the working copy. **The importer does not parse ingredients out of it** (§5 item 2). An empty rich-text field can come back as `{"type":"doc","version":1,"content":[]}` instead of null — import that as NULL and leave the flag false |
| `status` | `Meal.Status` | See §3 |
| `labels` | `MealTags` | Assign each tag a facet (Cuisine / Protein / Effort / Trait) |
| `customfield_10135` Frequency | drop | Legacy, from a retired automation |
| `customfield_10015` Start date | drop | Legacy; food needs no dates |
| `resolutiondate` | drop | Food needs no dates |
| `duedate` | drop | Stale where set, not a plan |
| `created` / `updated` | `Meal.CreatedAt` / `UpdatedAt` | `UpdatedAt` is shown and sortable in the UI (§1) |
| `attachment` | manual | Re-upload by hand |
| `comment` | drop | Copy the text into the directions if it matters |

## 5. Gaps, decisions and mechanics

1. ~~Recurrence history depth~~ **Not a question.** Every issue comes across as a task.
   The importer derives nothing and groups nothing.
2. **Ingredient splitting is a review screen, not an importer job** *(phase 2)*. Meals
   with a description arrive with `NeedsIngredientSplit` set, that description in both
   `ImportedText` and `Directions`. The screen works one meal at a time and **offers a
   suggested split for Conner to correct**: ingredient lines guessed into Quantity and
   IngredientName rows, the rest left as directions. It needs a **Clear all** button that
   throws the whole suggestion away and restores `ImportedText` verbatim, for when the
   guess is bad enough that correcting it is slower than starting over. The guess runs on
   that screen, never at import time, so the imported data is never silently rewritten
   and re-running the suggestion is always possible.
3. **Struck-through lines in descriptions.** Some CAPY descriptions are bullet lists with
   lines struck through. They import as markdown verbatim, `~~like this~~`, so nothing is
   lost; noted only so the conversion is not mistaken for stray formatting.
4. ~~Title normalization~~ **Settled.** Titles copy verbatim, typos and duplicate
   spellings included; tidy them in the app afterwards if they annoy.
5. **Attachments and comments** have no automated path worth building; handle the few of
   them by hand after the import.
6. **Export.** Read-only, from JIRA REST v3 (`/rest/api/3/search/jql`) or the Rovo MCP
   tools, paging 100 at a time. JQL per project: `project = CAPY ORDER BY created ASC`,
   `project = FOOD ORDER BY created ASC`. Request `fields=*all` for FOOD (the custom
   fields matter) and the explicit field list from §2 for CAPY. Page by `nextPageToken`,
   or by `created` windows if a page token is awkward — windows must be checked for
   overflow past 100 results.
7. **Snapshot to disk as JSON**, one file per project, and import from the files. The
   import is then reproducible and JIRA is touched exactly once. The snapshot holds the
   real data, so it stays out of the repository.
8. **Convert ADF to markdown** in one place. The MCP tools can return markdown directly
   (`responseContentFormat: "markdown"`), which is the cheaper path than writing an ADF
   walker.
9. **Load order.** Phase 1: categories → tasks → subtasks (if subtasks stay). Phase 2
   adds meal tags → meals. Tasks reference categories; subtasks reference tasks. Phase 1
   needs no FOOD export, so the meal queries can be left until then.
10. **Idempotency.** Upsert on `JiraKey`, so a re-run after a fix updates rather than
    duplicates. Run against a scratch database first.
11. **Verification.** Compare the loaded rows against the snapshot, not against figures
    written down here: one task per issue, one category per epic, the uncategorized count
    equal to the epic-less issues, completed and due-date counts equal to the source, and
    the status split identical on both sides. Nothing is collapsed, so every count should
    match exactly. Then spot-check by hand the largest epic, a task with subtasks, a
    struck-through checklist, and on the meal side the longest description and a one-time
    meal.
12. **Keep JIRA read-only** until CapybaraHome has been used for a week or two, then
    archive the projects rather than deleting them.
