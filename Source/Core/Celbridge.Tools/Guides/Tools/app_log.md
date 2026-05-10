# app_log

Writes an informational message to the running Celbridge application log at `Information` level.

For routine progress, decision points, or context that would be useful to the developer reviewing logs after the fact. Do not use it for warnings or errors — those have dedicated tools (`app_log_warning`, `app_log_error`) so the log filters and severity work correctly.
