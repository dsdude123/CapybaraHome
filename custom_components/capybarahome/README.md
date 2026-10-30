# CapybaraHome for Home Assistant

A custom integration that lets Home Assistant create CapybaraHome tasks and react to
tasks being completed. It talks to the same REST API and API tokens the connector uses,
so nothing new has to be exposed.

## Install through HACS

1. In HACS, open the three-dot menu and choose **Custom repositories**.
2. Add `https://github.com/dsdude123/CapybaraHome` with the type **Integration**.
3. Download **CapybaraHome** and restart Home Assistant.
4. Go to **Settings → Devices & services → Add integration** and pick **CapybaraHome**.

The setup form asks for the address Home Assistant can reach CapybaraHome on
(for example `http://tokyo-3-wsl.nerv.jpn.com:8101`) and an API token, which is minted
under Settings in CapybaraHome and shown only once.

Home Assistant 2025.12 or newer is required.

## Action: create a task

`capybarahome.create_task` creates one task.

| Field | Required | Meaning |
| --- | --- | --- |
| `title` | yes | Short description of the work. |
| `notes` | no | Free-form detail, held as markdown. |
| `category` | no | Name of an existing category; the call fails if no category has that name. |
| `due_date` | no | Day the task is due. |
| `status` | no | `ToDo`, `InProgress`, `OnHold` or `Done`. Defaults to `ToDo`. |
| `config_entry_id` | no | Which server to use, when more than one is set up. |

```yaml
actions:
  - action: capybarahome.create_task
    data:
      title: Change the furnace filter
      category: House
      due_date: "2026-11-15"
    response_variable: created
```

The action returns the task as the server stored it under `task`, so `created.task.id`
is available to the rest of the automation.

## Trigger: a task was completed

The integration polls CapybaraHome once a minute for tasks completed since the last
poll, so a completion reaches Home Assistant within a minute of being saved. Only tasks
completed after the integration started are reported; completions during a Home
Assistant restart are not replayed.

In the automation editor the trigger appears as **CapybaraHome → Task completed**, with
an optional list of categories to narrow it to.

```yaml
triggers:
  - trigger: capybarahome.task_completed
    options:
      category:
        - House
```

Every completion is also put on the event bus as `capybarahome_task_completed`, which
works on any Home Assistant version:

```yaml
triggers:
  - trigger: event
    event_type: capybarahome_task_completed
```

Either way the trigger data carries `task_id`, `title`, `notes`, `category`,
`category_id`, `due_date` and `completed_at`.
