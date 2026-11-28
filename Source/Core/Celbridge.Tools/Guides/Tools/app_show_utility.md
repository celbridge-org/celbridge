# app_show_utility

Reveals a utility by id, and can first move the utility to a workspace area. Utilities are the buttons on the Utility Panel rail. The built-in Explorer and Search live in the Utility Panel on the left. The built-in Project Settings and Workshop each open a document. A custom utility lives in the Utility Panel until the user docks the utility in a document tab. The tool reveals a utility wherever the utility is.

Call `app_list_utilities` first to discover the valid ids, each utility's `currentArea`, its declared `dockArea`, and which are already visible.

## Parameters

- `utilityId` — the id of the utility to show. Two forms, one scheme:
  - Built-in: `celbridge.explorer`, `celbridge.search`, `celbridge.project-settings` and `celbridge.workshop`.
  - Custom utilities: the editor id of the contributed utility (the same id `app_list_utilities` reports).
- `area` (required) — a workspace area to move the utility to before revealing it: `"utility"` (the Utility Panel rail), or `"main"`, `"bottom"` or `"side"` (a document tab in that area). `"document"` is accepted as an alias for the utility's own document area, so you can ask for a tab without naming an area. Pass an empty string to reveal the utility wherever it currently is without moving it. Only a custom utility moves between areas: for a built-in or a document shortcut, naming the area it is already in reveals it, and naming any other one is an error.

## Behaviour

- With an empty `area`, the tool reveals the utility where it already lives: a utility in the **panel** has its rail tab selected (revealing the panel if another tab was showing); a utility that is a **document** has its tab activated and brought to the front, opening it first if it was closed. Revealing always presents the area the utility lands in, so a collapsed area is brought back and the utility ends up on screen. A custom utility's backing file is seeded on first reveal.
- With `area`, a custom utility is first moved to that area — reparenting its single live WebView, keeping all its state — and then revealed there. Moving to a document area docks it into that area's primary section (`main_left`, `bottom_left` or `side_top`) and reveals the area when it is collapsed; moving to `"utility"` returns it to the rail.

## Gotchas

- An unknown id returns an error. Use `app_list_utilities` to get the exact ids rather than guessing.
- An unrecognised `area` returns an error naming the accepted tokens. A utility that stays in the Utility Panel refuses a document area, and `"document"` returns an error for it, because it has nowhere to be sent. Read `dockArea` from `app_list_utilities` to tell those apart.
- Explorer, Search and the document shortcuts do not move. Asking for an area they are not already in returns an error rather than quietly revealing them where they were, so pass an empty string when you only want to bring one up.
- Revealing a utility that is already in the requested area is a no-op (it stays put, with a brief highlight when it is a document).
- This tool reveals or moves a utility. The tool never closes a utility. Closing a custom utility's document tab docks the utility back into the Utility Panel. Project Settings and the Workshop are ordinary documents. Closing one of them closes the document, and showing the utility again reopens the document.
