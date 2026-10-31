"""Polls CapybaraHome for finished tasks and puts each one on the event bus."""

from __future__ import annotations

import logging
from datetime import datetime
from typing import Any

from homeassistant.core import CALLBACK_TYPE, HomeAssistant, callback
from homeassistant.helpers.event import async_track_time_interval
from homeassistant.util import dt as dt_util

from .api import CapybaraHomeClient, CapybaraHomeError
from .const import EVENT_TASK_COMPLETED, SCAN_INTERVAL

_LOGGER = logging.getLogger(__name__)


class CapybaraHomeWatcher:
    """Asks on a timer for the tasks completed since the last poll and fires one event each."""

    def __init__(self, hass: HomeAssistant, client: CapybaraHomeClient) -> None:
        """Start the cutoff at the moment the entry was set up, so nothing old is replayed."""
        self.hass = hass
        self.client = client
        self._since: datetime = dt_util.utcnow()
        self._failing = False

    @callback
    def async_start(self) -> CALLBACK_TYPE:
        """Begin polling and return the callback that stops it."""
        return async_track_time_interval(
            self.hass, self._async_poll, SCAN_INTERVAL, name="CapybaraHome tasks"
        )

    async def _async_poll(self, _now: datetime | None = None) -> None:
        """Read the completions since the cutoff and announce them."""
        try:
            completed = await self.client.async_completed_since(self._since)
        except CapybaraHomeError as err:
            if not self._failing:
                _LOGGER.warning("Could not read completed tasks: %s", err)
                self._failing = True
            return

        self._failing = False

        for task in completed:
            stamp = dt_util.parse_datetime(task.get("completedAt") or "")
            if stamp is None:
                continue
            self._since = max(self._since, stamp)
            self.hass.bus.async_fire(EVENT_TASK_COMPLETED, _payload(task))


def _payload(task: dict[str, Any]) -> dict[str, Any]:
    """Return the event data a completed task carries."""
    return {
        "task_id": task.get("id"),
        "title": task.get("title"),
        "notes": task.get("notes"),
        "category": task.get("categoryName"),
        "category_id": task.get("categoryId"),
        "due_date": task.get("dueDate"),
        "completed_at": task.get("completedAt"),
    }
