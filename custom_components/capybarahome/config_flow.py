"""Config flow that takes the address of a CapybaraHome server and a token."""

from __future__ import annotations

from typing import Any

import voluptuous as vol
from homeassistant.config_entries import ConfigFlow, ConfigFlowResult
from homeassistant.helpers.aiohttp_client import async_get_clientsession
from homeassistant.helpers.selector import (
    TextSelector,
    TextSelectorConfig,
    TextSelectorType,
)
from yarl import URL

from .api import (
    CapybaraHomeAuthError,
    CapybaraHomeClient,
    CapybaraHomeConnectionError,
)
from .const import CONF_TOKEN, CONF_URL, DOMAIN

STEP_USER_SCHEMA = vol.Schema(
    {
        vol.Required(CONF_URL): TextSelector(
            TextSelectorConfig(type=TextSelectorType.URL)
        ),
        vol.Required(CONF_TOKEN): TextSelector(
            TextSelectorConfig(type=TextSelectorType.PASSWORD)
        ),
    }
)


class CapybaraHomeConfigFlow(ConfigFlow, domain=DOMAIN):
    """Collects the address and the token, and checks them before finishing."""

    VERSION = 1

    async def async_step_user(
        self, user_input: dict[str, Any] | None = None
    ) -> ConfigFlowResult:
        """Ask for the address and the token, then sign in once with them."""
        errors: dict[str, str] = {}

        if user_input is not None:
            url = user_input[CONF_URL].strip().rstrip("/")
            parsed = URL(url)
            if parsed.scheme not in ("http", "https") or not parsed.host:
                errors[CONF_URL] = "invalid_url"
            else:
                client = CapybaraHomeClient(
                    async_get_clientsession(self.hass), url, user_input[CONF_TOKEN]
                )
                try:
                    await client.async_verify()
                except CapybaraHomeAuthError:
                    errors[CONF_TOKEN] = "invalid_auth"
                except CapybaraHomeConnectionError:
                    errors["base"] = "cannot_connect"
                else:
                    await self.async_set_unique_id(url.lower())
                    self._abort_if_unique_id_configured()
                    return self.async_create_entry(
                        title=parsed.host,
                        data={CONF_URL: url, CONF_TOKEN: user_input[CONF_TOKEN]},
                    )

        return self.async_show_form(
            step_id="user", data_schema=STEP_USER_SCHEMA, errors=errors
        )
