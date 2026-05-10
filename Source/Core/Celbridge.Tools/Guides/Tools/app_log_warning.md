# app_log_warning

Writes a warning message to the running Celbridge application log at `Warning` level.

For situations that are unexpected or look wrong, but the work continues — an unusual but non-fatal precondition, a fallback path, a heuristic guess made on missing data. If the agent had to give up, use `app_log_error` instead. If everything is normal, use `app_log`.
