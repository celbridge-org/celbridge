# app_show_utility

Reveals a utility by its id, optionally moving it to a dock location first. Utilities are auxiliary surfaces the app hosts alongside the user's documents: the built-in Explorer and Search live in the Utility Panel rail on the left, and custom utilities live there too until the user docks one into a document tab. This one tool reveals a utility wherever it currently is, so you do not need to know where it lives to bring it up.

Call `app_list_utilities` first to discover the valid ids, each utility's current `location`, and which are already shown.

## Parameters

- `utilityId` — the id of the utility to show. Two forms, one scheme:
  - Built-in Utility Panel surfaces: `celbridge.explorer` and `celbridge.search`.
  - Custom utilities: `{packageName}.{documentId}` (the same id `app_list_utilities` reports).
- `location` (optional) — a dock location to move the utility to before revealing it: `"panel"` (the Utility Panel rail) or `"document"` (a document tab). Omit to reveal the utility wherever it currently is without moving it. Ignored for the built-in utilities, which are always in the panel.

## Behaviour

- With no `location`, the tool reveals the utility where it already lives: a utility in the **panel** has its rail tab selected (revealing the panel if another tab was showing); a utility docked as a **document** has its tab activated and brought to the front. A custom utility's backing file is seeded on first reveal.
- With `location`, the utility is first moved to that dock location — reparenting its single live WebView, keeping all its state — and then revealed there. Moving to `"document"` docks it into the active document's section; moving to `"panel"` returns it to the rail.

## Gotchas

- An unknown id returns an error. Use `app_list_utilities` to get the exact ids rather than guessing.
- An invalid `location` (anything other than `"panel"` or `"document"`) returns an error.
- Revealing a utility that is already at the requested location is a no-op (it stays put, with a brief highlight when it is a document).
- This tool reveals or relocates a utility; it never closes or destroys one. A custom utility is never destroyed — closing its document tab docks it back into the Utility Panel rather than closing it.
