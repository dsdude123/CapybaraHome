"""The CapybaraHome integration: an action that creates tasks and a trigger for finished ones."""

from __future__ import annotations

from typing import Any

import voluptuous as vol
from homeassistant.config_entries import ConfigEntry, ConfigEntryState
from homeassistant.core import (
    HomeAssistant,
    ServiceCall,
    ServiceResponse,
    SupportsResponse,
)
from homeassistant.exceptions import (
    ConfigEntryAuthFailed,
    ConfigEntryNotReady,
    HomeAssistantError,
    ServiceValidationError,
)
from homeassistant.helpers import config_validation as cv
from homeassistant.helpers.aiohttp_client import async_get_clientsession
from homeassistant.helpers.typing import ConfigType

from .api import (
    CapybaraHomeAuthError,
    CapybaraHomeClient,
    CapybaraHomeError,
)
from .const import (
    ATTR_CATEGORY,
    ATTR_CONFIG_ENTRY_ID,
    ATTR_DUE_DATE,
    ATTR_NOTES,
    ATTR_STATUS,
    ATTR_TITLE,
    CONF_TOKEN,
    CONF_URL,
    DOMAIN,
    SERVICE_CREATE_TASK,
    TASK_STATUSES,
)
from .watcher import CapybaraHomeWatcher

CONFIG_SCHEMA = cv.config_entry_only_config_schema(DOMAIN)

type CapybaraHomeConfigEntry = ConfigEntry[CapybaraHomeWatcher]

CREATE_TASK_SCHEMA = vol.Schema(
    {
        vol.Required(ATTR_TITLE): cv.string,
        vol.Optional(ATTR_NOTES): cv.string,
        vol.Optional(ATTR_CATEGORY): cv.string,
        vol.Optional(ATTR_DUE_DATE): cv.date,
        vol.Optional(ATTR_STATUS): vol.In(TASK_STATUSES),
        vol.Optional(ATTR_CONFIG_ENTRY_ID): cv.string,
    }
)


async def async_setup(hass: HomeAssistant, config: ConfigType) -> bool:
    """Register the actions the integration offers."""
    hass.services.async_register(
        DOMAIN,
        SERVICE_CREATE_TASK,
        _create_task_handler(hass),
        schema=CREATE_TASK_SCHEMA,
        supports_response=SupportsResponse.OPTIONAL,
    )
    return True


async def async_setup_entry(
    hass: HomeAssistant, entry: CapybaraHomeConfigEntry
) -> bool:
    """Check the token and start polling for finished tasks."""
    client = CapybaraHomeClient(
        async_get_clientsession(hass), entry.data[CONF_URL], entry.data[CONF_TOKEN]
    )

    try:
        await client.async_verify()
    except CapybaraHomeAuthError as err:
        raise ConfigEntryAuthFailed(str(err)) from err
    except CapybaraHomeError as err:
        raise ConfigEntryNotReady(str(err)) from err

    watcher = CapybaraHomeWatcher(hass, client)
    entry.async_on_unload(watcher.async_start())
    entry.runtime_data = watcher
    return True


async def async_unload_entry(
    hass: HomeAssistant, entry: CapybaraHomeConfigEntry
) -> bool:
    """Drop the entry; the poll stops with it."""
    return True


def _create_task_handler(hass: HomeAssistant):
    """Return the handler for the create_task action."""

    async def async_create_task(call: ServiceCall) -> ServiceResponse:
        """Create one task on the server the call names."""
        entry = _target_entry(hass, call.data.get(ATTR_CONFIG_ENTRY_ID))
        client = entry.runtime_data.client

        category_id = None
        if name := call.data.get(ATTR_CATEGORY):
            category_id = await _category_id(client, name)

        try:
            created = await client.async_create_task(
                title=call.data[ATTR_TITLE],
                notes=call.data.get(ATTR_NOTES),
                status=call.data.get(ATTR_STATUS),
                category_id=category_id,
                due_date=call.data.get(ATTR_DUE_DATE),
            )
        except CapybaraHomeError as err:
            raise HomeAssistantError(str(err)) from err

        return {"task": created}

    return async_create_task


def _target_entry(hass: HomeAssistant, entry_id: str | None) -> CapybaraHomeConfigEntry:
    """Return the loaded entry the call is for, or explain why there is not exactly one."""
    loaded = [
        entry
        for entry in hass.config_entries.async_entries(DOMAIN)
        if entry.state is ConfigEntryState.LOADED
    ]

    if entry_id is not None:
        for entry in loaded:
            if entry.entry_id == entry_id:
                return entry
        raise ServiceValidationError(
            translation_domain=DOMAIN, translation_key="entry_not_loaded"
        )

    if not loaded:
        raise ServiceValidationError(
            translation_domain=DOMAIN, translation_key="no_entry"
        )
    if len(loaded) > 1:
        raise ServiceValidationError(
            translation_domain=DOMAIN, translation_key="several_entries"
        )
    return loaded[0]


async def _category_id(client: CapybaraHomeClient, name: str) -> int:
    """Return the id of the category with that name, matched without regard to case."""
    try:
        categories: list[dict[str, Any]] = await client.async_list_categories()
    except CapybaraHomeError as err:
        raise HomeAssistantError(str(err)) from err

    wanted = name.strip().casefold()
    for category in categories:
        if str(category.get("name", "")).casefold() == wanted:
            return int(category["id"])

    raise ServiceValidationError(
        translation_domain=DOMAIN,
        translation_key="unknown_category",
        translation_placeholders={"category": name},
    )
