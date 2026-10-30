"""Constants shared by the CapybaraHome integration."""

from datetime import timedelta

DOMAIN = "capybarahome"

CONF_URL = "url"
CONF_TOKEN = "token"

EVENT_TASK_COMPLETED = "capybarahome_task_completed"

SERVICE_CREATE_TASK = "create_task"

ATTR_TITLE = "title"
ATTR_NOTES = "notes"
ATTR_CATEGORY = "category"
ATTR_DUE_DATE = "due_date"
ATTR_STATUS = "status"
ATTR_CONFIG_ENTRY_ID = "config_entry_id"

TASK_STATUSES = ["ToDo", "InProgress", "OnHold", "Done"]

SCAN_INTERVAL = timedelta(seconds=60)
