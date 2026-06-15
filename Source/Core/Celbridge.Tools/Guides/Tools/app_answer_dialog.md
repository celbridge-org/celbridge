# app_answer_dialog

Schedules an automated answer for the next modal dialog of the named kind, so a script can drive a flow that would otherwise block on user interaction. When a dialog of that kind is displayed, the timer begins; after the delay the answer is broadcast and the dialog closes itself with an affirmative response (OK, Confirm, Create, Delete, Rename — whichever the dialog's affirmative action is).

The dialog actually displays briefly before auto-closing. This is by design: an integration test exercises the real end-to-end UI flow, screenshots are useful, and the audit trail matches what a real user would have done.

**Debug-only.** The tool is wrapped in `#if DEBUG`, so it does not exist in release builds — `tools/list` does not advertise it and `app_call` returns "denied". Inside debug builds it is also gated by the `answer-dialog` user-level feature flag.

## When to call it

Right before triggering the call that opens the modal dialog. Order matters:

1. Call `app_answer_dialog(dialogKind, payload?, delayMs?)` to schedule the answer.
2. Call the tool that triggers the dialog (e.g. `package_unpublish`, `explorer_rename`).

The delay timer starts when the matching dialog is displayed, not when this call returns — so it's fine for agent timing to vary between the schedule and the dialog appearing.

## Parameters

- `dialogKind` (required string) — identifies which dialog kind the answer is for. The schedule only fires when a dialog of this kind appears; if a different dialog appears first, the schedule stays pending and the unexpected dialog blocks on the user. Valid values:
  - `"Confirmation"` — yes/no prompts (e.g. `package_delete`).
  - `"InputText"` — single-string text-entry prompts (e.g. `explorer_rename`).
- `payload` (optional string, default `""`) — the content the dialog should receive:
  - **Confirmation dialogs**: payload is ignored; the dialog OKs unconditionally.
  - **Input-text dialogs**: payload is the text to enter. Empty payload enters an empty string.
- `delayMs` (optional int, default `250`) — milliseconds to wait *after the dialog is displayed* before broadcasting the answer. Tune up for dialogs with slow initialization; tune down for tight test loops.

Only one schedule is held at a time. A subsequent call overwrites; the schedule is cleared on workspace teardown.

## Returns

`"ok"` on success. Errors when the `answer-dialog` feature flag is off.

The schedule itself is fire-and-forget: the tool returns immediately after recording the schedule. If no dialog of the scheduled kind ever appears, no broadcast happens. If a dialog of a *different* kind appears first, a warning is logged and the schedule stays pending — the unexpected dialog blocks on the user, which is the right outcome since it wasn't expected by the script.

## Examples

### Python (`cel.app.answer_dialog`)

```python
# Confirm the next package_unpublish prompt.
cel.app.answer_dialog("Confirmation")
package.unpublish("test-integration-pkg")

# Provide rename text for the next explorer_rename.
cel.app.answer_dialog("InputText", "Renamed.txt")
cel.explorer.rename("/Folder/Old.txt")

# Give a slow-loading dialog more headroom.
cel.app.answer_dialog("Confirmation", delayMs=500)
package.delete("heavy-package")
```

### JavaScript

```javascript
await app.answerDialog("Confirmation");                       // confirm
await app.answerDialog("InputText", "Renamed.txt");           // rename
await app.answerDialog("Confirmation", "", 500);              // longer delay
```

## Gotchas

- **Single schedule, single use.** A second call overwrites. The schedule is consumed by the first dialog of the matching kind.
- **Wrong-kind dialogs do not consume the schedule.** If the test is expected to walk through several dialogs and you only want to auto-answer one of them, schedule for that specific kind — interleaved dialogs of other kinds will not eat the schedule. The unexpected dialog still blocks on the user, so a script-induced unexpected dialog will hang the test (with a clear warning in the log).
- **No "decline" mechanism.** The tool only schedules an *affirmative* answer. Testing a "user cancels" path is not what this tool is for — exercise the underlying command directly or test the cancelled-outcome branch without going through the dialog.
- **Workspace teardown clears the schedule.** Re-schedule after each workspace load if your fixture runs across workspaces.
- **Delay is observable.** Every automated test pays the `delayMs` cost. Default 250ms is comfortable for normal dialogs; for tight loops with simple confirmations you can lower it.
