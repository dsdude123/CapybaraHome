"""Covers the CapybaraHome integration's setup, its action and its completion trigger."""

from datetime import timedelta
from typing import Any
from unittest.mock import patch

import pytest
from homeassistant.core import HomeAssistant
from homeassistant.exceptions import ServiceValidationError
from homeassistant.setup import async_setup_component
from homeassistant.util import dt as dt_util
from pytest_homeassistant_custom_component.common import (
    MockConfigEntry,
    async_fire_time_changed,
)

from custom_components.capybarahome.const import (
    CONF_TOKEN,
    CONF_URL,
    DOMAIN,
    EVENT_TASK_COMPLETED,
)

ENTRY_DATA = {CONF_URL: "http://capybara.test:8101", CONF_TOKEN: "a-token"}


class FakeClient:
    """Stands in for the REST client, recording what the integration asked of it."""

    def __init__(self) -> None:
        """Start with nothing completed and no category."""
        self.completed: list[list[dict[str, Any]]] = []
        self.categories: list[dict[str, Any]] = []
        self.created: list[dict[str, Any]] = []
        self.since: list[Any] = []

    async def async_verify(self) -> dict[str, Any]:
        """Accept the token."""
        return {"username": "first"}

    async def async_list_categories(self) -> list[dict[str, Any]]:
        """Return the categories the test set up."""
        return self.categories

    async def async_completed_since(self, since) -> list[dict[str, Any]]:
        """Return the next batch of completions the test queued."""
        self.since.append(since)
        return self.completed.pop(0) if self.completed else []

    async def async_create_task(self, **kwargs: Any) -> dict[str, Any]:
        """Record the task and answer as the server would."""
        self.created.append(kwargs)
        return {"id": 7, "title": kwargs["title"]}


@pytest.fixture
def client() -> FakeClient:
    """The fake the integration is given in place of the real client."""
    return FakeClient()


async def setup_entry(hass: HomeAssistant, client: FakeClient) -> MockConfigEntry:
    """Set up one config entry backed by the fake client."""
    entry = MockConfigEntry(
        domain=DOMAIN, data=ENTRY_DATA, unique_id=ENTRY_DATA[CONF_URL]
    )
    entry.add_to_hass(hass)

    with (
        patch("custom_components.capybarahome.CapybaraHomeClient", return_value=client),
        patch(
            "custom_components.capybarahome.watcher.CapybaraHomeClient",
            return_value=client,
        ),
    ):
        assert await hass.config_entries.async_setup(entry.entry_id)
        await hass.async_block_till_done()

    return entry


async def test_the_entry_sets_up_and_registers_the_action(
    hass: HomeAssistant, client: FakeClient
) -> None:
    """A good token leaves the entry loaded and the action callable."""
    assert await async_setup_component(hass, DOMAIN, {})
    await setup_entry(hass, client)

    assert hass.services.has_service(DOMAIN, "create_task")


async def test_creating_a_task_resolves_the_category_by_name(
    hass: HomeAssistant, client: FakeClient
) -> None:
    """A category name is turned into the id the API wants."""
    client.categories = [{"id": 3, "name": "House"}]
    assert await async_setup_component(hass, DOMAIN, {})
    await setup_entry(hass, client)

    response = await hass.services.async_call(
        DOMAIN,
        "create_task",
        {"title": "Bleed the radiators", "category": "house"},
        blocking=True,
        return_response=True,
    )

    assert client.created == [
        {
            "title": "Bleed the radiators",
            "notes": None,
            "status": None,
            "category_id": 3,
            "due_date": None,
        }
    ]
    assert response == {"task": {"id": 7, "title": "Bleed the radiators"}}


async def test_an_unknown_category_is_refused(
    hass: HomeAssistant, client: FakeClient
) -> None:
    """A name no category carries stops the call rather than filing it loose."""
    assert await async_setup_component(hass, DOMAIN, {})
    await setup_entry(hass, client)

    with pytest.raises(ServiceValidationError):
        await hass.services.async_call(
            DOMAIN, "create_task", {"title": "Mow", "category": "Garden"}, blocking=True
        )

    assert client.created == []


async def test_a_completed_task_reaches_the_event_bus(
    hass: HomeAssistant, client: FakeClient
) -> None:
    """The poll fires one event per completion and moves its cutoff forward."""
    assert await async_setup_component(hass, DOMAIN, {})
    await setup_entry(hass, client)

    events = []
    hass.bus.async_listen(EVENT_TASK_COMPLETED, events.append)

    completed_at = dt_util.utcnow() + timedelta(seconds=10)
    client.completed = [
        [
            {
                "id": 4,
                "title": "Defrost the freezer",
                "notes": None,
                "categoryId": 3,
                "categoryName": "House",
                "dueDate": "2026-11-01",
                "completedAt": completed_at.isoformat(),
            }
        ]
    ]

    async_fire_time_changed(hass, dt_util.utcnow() + timedelta(seconds=61))
    await hass.async_block_till_done()

    assert len(events) == 1
    assert events[0].data == {
        "task_id": 4,
        "title": "Defrost the freezer",
        "notes": None,
        "category": "House",
        "category_id": 3,
        "due_date": "2026-11-01",
        "completed_at": completed_at.isoformat(),
    }

    async_fire_time_changed(hass, dt_util.utcnow() + timedelta(seconds=121))
    await hass.async_block_till_done()

    assert len(events) == 1
    assert client.since[-1] == completed_at


async def test_the_trigger_fires_and_honours_its_categories(
    hass: HomeAssistant, client: FakeClient
) -> None:
    """An automation on the trigger runs for a matching category and not for another."""
    assert await async_setup_component(hass, DOMAIN, {})
    await setup_entry(hass, client)

    assert await async_setup_component(
        hass,
        "automation",
        {
            "automation": {
                "triggers": {
                    "trigger": "capybarahome.task_completed",
                    "options": {"category": ["House"]},
                },
                "actions": {
                    "event": "ran",
                    "event_data": {"title": "{{ trigger.title }}"},
                },
            }
        },
    )
    await hass.async_block_till_done()

    ran = []
    hass.bus.async_listen("ran", ran.append)

    hass.bus.async_fire(
        EVENT_TASK_COMPLETED, {"title": "Sweep the chimney", "category": "Garden"}
    )
    await hass.async_block_till_done()
    assert ran == []

    hass.bus.async_fire(
        EVENT_TASK_COMPLETED, {"title": "Bleed the radiators", "category": "House"}
    )
    await hass.async_block_till_done()

    assert len(ran) == 1
    assert ran[0].data["title"] == "Bleed the radiators"
