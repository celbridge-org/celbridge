# app_log_error

Writes an error message to the running Celbridge application log at `Error` level.

For failures the agent could not recover from. The application's log filters surface error-level entries prominently, so saving lower-severity messages with `app_log` or `app_log_warning` keeps this channel meaningful.
