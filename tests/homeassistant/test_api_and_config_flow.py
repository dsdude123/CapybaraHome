"""Covers the REST client's requests and the config flow's checks."""

from datetime import UTC, date, datetime

import pytest
from homeassistant.config_entries import SOURCE_USER
from homeassistant.core import HomeAssistant
from homeassistant.data_entry_flow import FlowResultType
from homeassistant.helpers.aiohttp_client import async_get_clientsession
from pytest_homeassistant_custom_component.common import MockConfigEntry
from pytest_homeassistant_custom_component.test_util.aiohttp import AiohttpClientMocker

from custom_components.capybarahome.api import (
    CapybaraHomeAuthError,
    CapybaraHomeClient,
    CapybaraHomeConnectionError,
)
from custom_components.capybarahome.const import CONF_TOKEN, CONF_URL, DOMAIN

BASE = "http://capybara.test:8101"


def make_client(hass: HomeAssistant) -> CapybaraHomeClient:
    """Return a client pointed at the mocked server."""
    return CapybaraHomeClient(async_get_clientsession(hass), BASE, "a-token")


async def test_completed_since_asks_for_done_tasks_after_the_moment(
    hass: HomeAssistant, aioclient_mock: AiohttpClientMocker
) -> None:
    """The cutoff travels as UTC, which is what the API parses."""
    aioclient_mock.get(f"{BASE}/api/tasks", json=[{"id": 1}])
    client = make_client(hass)

    await client.async_completed_since(datetime(2026, 10, 30, 18, 0, tzinfo=UTC))

    query = aioclient_mock.mock_calls[0][1].query
    assert query["status"] == "Done"
    assert query["completedSince"] == "2026-10-30T18:00:00Z"
    assert aioclient_mock.mock_calls[0][3]["Authorization"] == "Bearer a-token"


async def test_creating_a_task_sends_only_the_fields_that_were_given(
    hass: HomeAssistant, aioclient_mock: AiohttpClientMocker
) -> None:
    """An empty field is left out rather than sent as null."""
    aioclient_mock.post(f"{BASE}/api/tasks", json={"id": 2}, status=201)
    client = make_client(hass)

    await client.async_create_task(
        "Bleed the radiators", category_id=3, due_date=date(2026, 11, 15)
    )

    assert aioclient_mock.mock_calls[0][2] == {
        "title": "Bleed the radiators",
        "categoryId": 3,
        "dueDate": "2026-11-15",
    }


async def test_a_refused_token_and_a_server_error_are_told_apart(
    hass: HomeAssistant, aioclient_mock: AiohttpClientMocker
) -> None:
    """401 is an auth failure; anything else over 400 is a connection failure."""
    aioclient_mock.get(f"{BASE}/api/me", status=401)
    client = make_client(hass)
    with pytest.raises(CapybaraHomeAuthError):
        await client.async_verify()

    aioclient_mock.clear_requests()
    aioclient_mock.get(f"{BASE}/api/me", status=500, text="boom")
    with pytest.raises(CapybaraHomeConnectionError):
        await client.async_verify()


async def test_the_config_flow_stores_a_working_address_and_token(
    hass: HomeAssistant, aioclient_mock: AiohttpClientMocker
) -> None:
    """A token the server accepts finishes the flow."""
    aioclient_mock.get(f"{BASE}/api/me", json={"username": "first"})

    result = await hass.config_entries.flow.async_init(
        DOMAIN, context={"source": SOURCE_USER}
    )
    assert result["type"] is FlowResultType.FORM

    result = await hass.config_entries.flow.async_configure(
        result["flow_id"], {CONF_URL: f"{BASE}/", CONF_TOKEN: "a-token"}
    )

    assert result["type"] is FlowResultType.CREATE_ENTRY
    assert result["title"] == "capybara.test"
    assert result["data"] == {CONF_URL: BASE, CONF_TOKEN: "a-token"}


@pytest.mark.parametrize(
    ("status", "field", "error"),
    [(401, CONF_TOKEN, "invalid_auth"), (500, "base", "cannot_connect")],
)
async def test_the_config_flow_reports_what_went_wrong(
    hass: HomeAssistant,
    aioclient_mock: AiohttpClientMocker,
    status: int,
    field: str,
    error: str,
) -> None:
    """A refused token and an unreachable server each name themselves on the form."""
    aioclient_mock.get(f"{BASE}/api/me", status=status)

    result = await hass.config_entries.flow.async_init(
        DOMAIN, context={"source": SOURCE_USER}
    )
    result = await hass.config_entries.flow.async_configure(
        result["flow_id"], {CONF_URL: BASE, CONF_TOKEN: "a-token"}
    )

    assert result["type"] is FlowResultType.FORM
    assert result["errors"] == {field: error}


async def test_an_address_that_is_not_http_is_refused(hass: HomeAssistant) -> None:
    """The flow stops before it tries to connect to something that is not a web address."""
    result = await hass.config_entries.flow.async_init(
        DOMAIN, context={"source": SOURCE_USER}
    )
    result = await hass.config_entries.flow.async_configure(
        result["flow_id"], {CONF_URL: "capybara.test", CONF_TOKEN: "a-token"}
    )

    assert result["errors"] == {CONF_URL: "invalid_url"}


async def test_the_same_server_cannot_be_added_twice(
    hass: HomeAssistant, aioclient_mock: AiohttpClientMocker
) -> None:
    """A second entry for one address aborts instead of polling the server twice."""
    MockConfigEntry(
        domain=DOMAIN, data={CONF_URL: BASE, CONF_TOKEN: "a-token"}, unique_id=BASE
    ).add_to_hass(hass)
    aioclient_mock.get(f"{BASE}/api/me", json={"username": "first"})

    result = await hass.config_entries.flow.async_init(
        DOMAIN, context={"source": SOURCE_USER}
    )
    result = await hass.config_entries.flow.async_configure(
        result["flow_id"], {CONF_URL: BASE, CONF_TOKEN: "another"}
    )

    assert result["type"] is FlowResultType.ABORT
    assert result["reason"] == "already_configured"


async def test_a_redirect_is_refused_rather_than_followed(
    hass: HomeAssistant, aioclient_mock: AiohttpClientMocker
) -> None:
    """The bearer token is never carried to whatever address a redirect names."""
    aioclient_mock.get(
        f"{BASE}/api/me",
        status=302,
        headers={"Location": "http://elsewhere.test/api/me"},
    )
    client = make_client(hass)

    with pytest.raises(CapybaraHomeConnectionError):
        await client.async_verify()
