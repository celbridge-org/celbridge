# app_simulate_input

Performs a test-automation input operation against the running application, for input that an external
harness cannot deliver. The operation set is deliberately small and grows only when a real need appears;
`key` is the only operation today.

**Debug-only.** The tool is declared in every build so its guide stays paired with a registered tool, but
in a release build it refuses with "available in debug builds only". There is no feature flag: a release
build cannot perform the operation at all, so there is nothing for a flag to gate.

## What it is for

Driving the app from outside — clicks, typing, ordinary shortcuts — is the job of whatever automation the
caller already has. This tool covers the gap where that automation cannot produce a particular input at
all, so a behaviour is otherwise untestable. The Escape key is the case that prompted it: the desktop
automation used to run the agent test plans reports success for Escape and delivers nothing, which makes
"cancel the current edit" impossible to exercise. Reach for this tool only for that kind of gap, not as a
general substitute for driving the UI.

It runs outside the command queue, so it still works while a modal dialog is open — which is when a caller
most needs it, because an open dialog holds every queued tool until it is answered.

## Operations

### `key`

Posts a key-down and key-up for a named key into the application's own event queue.

The press enters at the earliest point inside the process, so it travels the same route a real key press
takes from the queue onward — the app's key monitor, the window's event dispatch, the key-equivalent
phase, the responder chain, and any focused web view. It is an injection into the real routing, not a
bypass of it, so a result from this tool says something meaningful about how the application routes keys.

What it does not cover is the window server's hand-off to the process. A key that the operating system or
another application intercepts before Celbridge sees it will still appear to work here.

**Supported on macOS.** On every other platform the call fails with an unsupported-platform message, so a
test that depends on it can only run on macOS today.

## Parameters

- `operation` (required string) — the operation to perform. Valid value: `"key"`.
- `key` (string) — for `key`, the name of the key to press. Case-insensitive. Valid names:
  - `Escape`, `Tab`, `Return`, `Backspace`, `ForwardDelete`
  - `Home`, `End`, `PageUp`, `PageDown`
  - `Up`, `Down`, `Left`, `Right`
  - `F1` through `F12`

  Printable characters are deliberately absent: they already reach the app through ordinary text input, so
  synthesising them gains nothing.
- `modifiers` (optional string, default `""`) — modifier keys held for the press, comma-separated. Valid
  names: `command`, `control`, `shift`, `option`. For example `"command,shift"`.

## Results

Returns `ok` on success. Fails when the operation or key name is not recognised (the error lists the valid
names), when the platform is not macOS, or when the application has no key window to receive the press —
which usually means the app is not frontmost.

## Answering a modal dialog

The application's own dialogs answer to the keyboard, so `Escape` cancels the open dialog and `Return`
accepts it — no separate operation, and the same route a user takes. Both are worth reaching for when a
dialog appears unexpectedly and is holding the command queue.

`app_answer_dialog` is still the better choice when the answer can be scheduled before the dialog opens,
or when the dialog needs a value rather than a button.

A dialog drawn by a hosted page — a SpreadJS Designer dialog, say — is not modal to the host and takes the
key press the same way.

## Notes

The press goes to whatever currently holds the keyboard, exactly as a real one would. Establish focus
first, and read the resulting state back rather than assuming the press landed where you meant it to.

The application must hold the keyboard: the call fails with "no key window to receive the key press" when
the app is not the active one, or when its window is frontmost without being key. Activating the app, or
clicking into its window, resolves it.
