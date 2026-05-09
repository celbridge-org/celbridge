# app_show_alert

Shows a modal alert dialog to the user with a message and an optional title. Always interactive — there is no silent mode. The call blocks until the user dismisses the dialog.

## When to use it

Sparingly, for genuinely user-facing information that has to be acknowledged: a result the user asked for, a confirmation of a completed task, an explanation of why something cannot be done. For ordinary progress or diagnostic output, use the console (or `app_log*`) instead — modal dialogs interrupt the user's flow.

If the user has stepped away from the keyboard, the call sits there waiting. Prefer non-modal channels for anything that doesn't truly require human attention.

## Parameters

### message

The text shown in the dialog body.

### title

Optional dialog title. Empty string omits the title.

## Returns

`"ok"` on success.

## See also

- `silent_vs_interactive` — which tools surface UI vs. run silently.
