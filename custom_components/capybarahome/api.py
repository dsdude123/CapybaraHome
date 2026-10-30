"""Thin client for the CapybaraHome REST API."""

from __future__ import annotations

from datetime import UTC, date, datetime
from typing import Any

import aiohttp
from yarl import URL


class CapybaraHomeError(Exception):
    """Base class for every failure this client reports."""


class CapybaraHomeAuthError(CapybaraHomeError):
    """The token was rejected."""


class CapybaraHomeConnectionError(CapybaraHomeError):
    """The server could not be reached, or answered with an error."""


class CapybaraHomeClient:
    """Reads and writes CapybaraHome over its bearer-token API."""

    def __init__(self, session: aiohttp.ClientSession, url: str, token: str) -> None:
        """Hold the session, the base address and the token to sign requests with."""
        self._session = session
        self._base = URL(url.rstrip("/") + "/")
        self._token = token

    async def async_verify(self) -> dict[str, Any]:
        """Return the user the token belongs to, raising when it is not accepted."""
        return await self._request("GET", "api/me")

    async def async_list_categories(self) -> list[dict[str, Any]]:
        """Return every category."""
        return await self._request("GET", "api/categories")

    async def async_completed_since(self, since: datetime) -> list[dict[str, Any]]:
        """Return the tasks completed strictly after ``since``."""
        params = {
            "status": "Done",
            "completedSince": since.astimezone(UTC).isoformat().replace("+00:00", "Z"),
        }
        return await self._request("GET", "api/tasks", params=params)

    async def async_create_task(
        self,
        title: str,
        notes: str | None = None,
        status: str | None = None,
        category_id: int | None = None,
        due_date: date | None = None,
    ) -> dict[str, Any]:
        """Create one task and return it as the server stored it."""
        body: dict[str, Any] = {"title": title}
        if notes:
            body["notes"] = notes
        if status:
            body["status"] = status
        if category_id is not None:
            body["categoryId"] = category_id
        if due_date is not None:
            body["dueDate"] = due_date.isoformat()
        return await self._request("POST", "api/tasks", json=body)

    async def _request(
        self,
        method: str,
        path: str,
        params: dict[str, str] | None = None,
        json: dict[str, Any] | None = None,
    ) -> Any:
        """Send one request and return its decoded body."""
        try:
            async with self._session.request(
                method,
                self._base.join(URL(path)),
                params=params,
                json=json,
                headers={"Authorization": f"Bearer {self._token}"},
                allow_redirects=False,
            ) as response:
                if response.status in (301, 302, 303, 307, 308):
                    raise CapybaraHomeConnectionError(
                        "CapybaraHome redirected the request, which would send the token "
                        "somewhere else; point the integration at the final address."
                    )
                if response.status in (401, 403):
                    raise CapybaraHomeAuthError("CapybaraHome rejected the token.")
                if response.status >= 400:
                    body = (await response.text())[:200]
                    raise CapybaraHomeConnectionError(
                        f"CapybaraHome answered {response.status}: {body}"
                    )
                if response.status == 204:
                    return None
                return await response.json()
        except aiohttp.ClientError as err:
            raise CapybaraHomeConnectionError(
                f"CapybaraHome could not be reached: {err}"
            ) from err
