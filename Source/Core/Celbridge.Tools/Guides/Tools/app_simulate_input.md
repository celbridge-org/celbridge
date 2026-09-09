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

## Limits

It cannot dismiss the application's own modal dialogs. The operation runs as a command, and a modal
Celbridge dialog blocks the command queue until it is answered, so the call times out instead of
returning. Schedule the answer with `app_answer_dialog` before opening such a dialog. A dialog drawn by a
hosted page — a SpreadJS Designer dialog, say — is not modal to the host and takes the key press normally.

## Notes

The press goes to whatever currently holds the keyboard, exactly as a real one would. Establish focus
first, and read the resulting state back rather than assuming the press landed where you meant it to.
