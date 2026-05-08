---
name: app_log
description: Write an informational message to the application log; for routine progress or context, not for problems.
---

# app_log

Writes an informational message to the running Celbridge application log at `Information` level. The message lands in the same log stream the application uses for its own diagnostics.

## When to use it

For routine progress, decision points, or context that would be useful to the developer reviewing logs after the fact. Don't use it for warnings or errors — those have dedicated tools (`app_log_warning`, `app_log_error`) so the log filters and severity work correctly.

## Parameters

### message

The text to log. Keep it short and self-contained — log entries are read out of the surrounding tool-call context.

## Returns

Void. The call is fire-and-forget; success is implicit.

## See also

- `app_log_warning` — when something looks off but the work continues.
- `app_log_error` — when something has gone wrong.
