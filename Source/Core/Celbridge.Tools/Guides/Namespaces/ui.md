# ui

The `ui` namespace is test automation for the application's own user interface. It finds the application's
controls and performs their default actions, places the elements of its documents' pages in the window, presses
keys a desktop automation cannot deliver, and answers modal dialogs. It is for a test harness that drives the
application from outside, and for scripts that drive a flow which would otherwise wait on the user.

## Must-knows

- **Test automation builds only.** Every Debug build has the `ui` tools. A Release build has them only when built
  with `-p:CelbridgeTestAutomation=true`, and otherwise every `ui` tool refuses. `app_get_state` reports
  `hasTestAutomation` as `true` when they work.
- **They answer while a dialog is open.** Each runs on the UI thread rather than through the command queue, so it
  still answers while a modal dialog holds that queue. `ui_press_key` with `Escape` cancels an open dialog, and
  `Return` accepts it. `ui_find_page_elements` also waits for the page to answer.
- **They are no substitute for real input.** Invoking a control through its automation peer, or posting a key into
  the application's own event queue, skips part of the route real input takes. A test about that routing sends
  real input.
- **Frames are in the window's own coordinates.** A control's or page element's `bounds` are device-independent
  pixels from the top left of the window's content. On macOS the content sits below the title bar, so a window
  button has a negative `y`. On Windows the content extends into the title bar, so the caption buttons sit inside
  it.

## Tools

**Controls.**

- `ui_find_controls` — find the app's own controls by automation ID, name or control type, with each one's frame
  and state. On macOS that includes the menu bar's items and the window's buttons, and on Windows the caption
  buttons.
- `ui_invoke_control` — perform the default action of one of the app's own controls, as assistive technology does.

**Pages.**

- `ui_find_page_elements` — find elements of a document's page by CSS selector, ARIA role or text, with each one's
  frame in the window and its state.

**Input.**

- `ui_press_key` — post a named key press into the app's own event queue. macOS and Windows.

**Dialogs.**

- `ui_answer_dialog` — schedule an automated answer for the next modal dialog of a kind.
