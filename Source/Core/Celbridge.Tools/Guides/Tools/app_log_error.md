# app_log_error

Writes an error message to the running Celbridge application log at `Error` level.

## When to use it

For failures the agent could not recover from. The application's own log filters surface error-level entries prominently, so saving lower-severity messages with `app_log` or `app_log_warning` keeps this channel meaningful.

## Parameters

### message

The error text. Include enough context that someone reading the log later can understand what went wrong.

## Returns

Void. Fire-and-forget.

## See also

- `app_log` — informational messages.
- `app_log_warning` — warnings.
