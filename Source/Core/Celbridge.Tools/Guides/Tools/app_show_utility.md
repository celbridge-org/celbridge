# app_show_utility

Reveals a utility by its id, optionally moving it to a workspace area first. Utilities are auxiliary surfaces the app hosts alongside the user's documents: the built-in Explorer and Search live in the Utility Panel rail on the left, and custom utilities live there too until the user docks one into a document tab. This one tool reveals a utility wherever it currently is, so you do not need to know where it lives to bring it up.

Call `app_list_utilities` first to discover the valid ids, each utility's current `area`, and which are already shown.

## Parameters

- `utilityId` — the id of the utility to show. Two forms, one scheme:
  - Built-in Utility Panel surfaces: `celbridge.explorer` and `celbridge.search`.
  - Custom utilities: the editor id of the contributed utility (the same id `app_list_utilities` reports).
- `area` (optional) — a workspace area to move the utility to before revealing it: `"utility"` (the Utility Panel rail), or `"main"`, `"bottom"` or `"side"` (a document tab in that area). `"document"` is accepted as an alias for the utility's document area. Omit to reveal the utility wherever it currently is without moving it. Ignored for the built-in utilities, which are always in the panel.

## Behaviour

- With no `area`, the tool reveals the utility where it already lives: a utility in the **panel** has its rail tab selected (revealing the panel if another tab was showing); a utility docked as a **document** has its tab activated and brought to the front. A custom utility's backing file is seeded on first reveal.
- With `area`, the utility is first moved to that area — reparenting its single live WebView, keeping all its state — and then revealed there. Moving to `"main"` or `"document"` docks it into `main_left`; moving to `"utility"` returns it to the rail.

## Gotchas

- An unknown id returns an error. Use `app_list_utilities` to get the exact ids rather than guessing.
- An invalid `area` returns an error naming the accepted values. Utilities can only be docked in `"main"` today, so `"bottom"` and `"side"` are rejected until utilities can declare the document area they use.
- Revealing a utility that is already in the requested area is a no-op (it stays put, with a brief highlight when it is a document).
- This tool reveals or relocates a utility; it never closes or destroys one. A custom utility is never destroyed — closing its document tab docks it back into the Utility Panel rather than closing it.
