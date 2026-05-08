---
name: app_log_warning
description: Write a warning to the application log; for unexpected-but-recoverable situations.
---

# app_log_warning

Writes a warning message to the running Celbridge application log at `Warning` level.

## When to use it

For situations that are unexpected or look wrong, but the work continues. A precondition that was unusual but not fatal, a fallback path the agent had to take, a heuristic guess made on missing data. If the agent had to give up, use `app_log_error` instead. If everything is normal, use `app_log`.

## Parameters

### message

The warning text. Keep it short and self-contained.

## Returns

Void. Fire-and-forget.

## See also

- `app_log` — informational messages.
- `app_log_error` — error-level messages.
