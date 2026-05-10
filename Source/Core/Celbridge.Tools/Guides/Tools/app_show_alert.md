# app_show_alert

Shows a modal alert dialog to the user with a message and an optional title. Always interactive — there is no silent mode. The call blocks until the user dismisses the dialog.

Use sparingly, for genuinely user-facing information that has to be acknowledged. For ordinary progress or diagnostic output, use the console (or `app_log*`) instead — modal dialogs interrupt the user's flow. If the user has stepped away from the keyboard, the call sits there waiting.

## Parameters

- `message` — text shown in the dialog body.
- `title` — optional dialog title. Empty string omits the title.

## Returns

`"ok"` on success.
