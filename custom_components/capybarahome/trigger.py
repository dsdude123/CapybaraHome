"""The triggers CapybaraHome offers to the automation editor."""

from __future__ import annotations

from typing import Any

import voluptuous as vol
from homeassistant.core import CALLBACK_TYPE, Event, HomeAssistant, callback
from homeassistant.helpers import config_validation as cv
from homeassistant.helpers.trigger import Trigger, TriggerActionRunner, TriggerConfig
from homeassistant.helpers.typing import ConfigType

from .const import EVENT_TASK_COMPLETED

_CONFIG_SCHEMA = vol.Schema(
    {
        vol.Optional("options"): vol.Schema(
            {vol.Optional("category"): vol.All(cv.ensure_list, [cv.string])}
        )
    }
)


class TaskCompletedTrigger(Trigger):
    """Fires when a task in CapybaraHome is marked done."""

    @classmethod
    async def async_validate_config(
        cls, hass: HomeAssistant, config: ConfigType
    ) -> ConfigType:
        """Validate the trigger config."""
        return _CONFIG_SCHEMA(config)

    def __init__(self, hass: HomeAssistant, config: TriggerConfig) -> None:
        """Keep the categories the trigger was narrowed to, if any."""
        super().__init__(hass, config)
        options = config.options or {}
        self._categories: list[str] = [
            category.casefold() for category in options.get("category", [])
        ]

    async def async_attach_runner(
        self,
        run_action: TriggerActionRunner,
        did_not_trigger: Any = None,
    ) -> CALLBACK_TYPE:
        """Listen for the completion event until the returned callback is called."""

        @callback
        def async_task_completed(event: Event) -> None:
            """Run the automation for a completion the trigger accepts."""
            if self._categories:
                category = (event.data.get("category") or "").casefold()
                if category not in self._categories:
                    return
            run_action(dict(event.data), f"task {event.data.get('title')!r} completed")

        return self._hass.bus.async_listen(EVENT_TASK_COMPLETED, async_task_completed)


TRIGGERS: dict[str, type[Trigger]] = {"task_completed": TaskCompletedTrigger}


async def async_get_triggers(hass: HomeAssistant) -> dict[str, type[Trigger]]:
    """Return the triggers of the CapybaraHome integration."""
    return TRIGGERS
