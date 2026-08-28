# app_show_utility

Reveals a utility by its id, optionally moving it to a workspace area first. Utilities are the buttons on the Utility Panel rail: the built-in Explorer and Search live in the rail on the left, the built-in Project Settings and Community Workshop open a document, and a contributed utility either lives in the rail until the user docks it into a document tab or opens a document of its own. This one tool reveals a utility wherever it currently is, so you do not need to know where it lives to bring it up.

Call `app_list_utilities` first to discover the valid ids, each utility's current `area`, the `allowedAreas` it may be moved to, and which are already shown.

## Parameters

- `utilityId` — the id of the utility to show. Two forms, one scheme:
  - Built-in: `celbridge.explorer`, `celbridge.search`, `celbridge.project-settings` and `celbridge.workshop`.
  - Custom utilities: the editor id of the contributed utility (the same id `app_list_utilities` reports).
- `area` (optional) — a workspace area to move the utility to before revealing it: `"utility"` (the Utility Panel rail), or `"main"`, `"bottom"` or `"side"` (a document tab in that area). It has to be one of the utility's `allowedAreas`. `"document"` is accepted as an alias for the utility's own document area, resolved from what it declares, so you can ask for a tab without knowing which areas it offers. Omit to reveal the utility wherever it currently is without moving it. Ignored for a utility that cannot move between areas: the built-in ones, and a contributed utility that opens a document rather than living in the rail.

## Behaviour

- With no `area`, the tool reveals the utility where it already lives: a utility in the **panel** has its rail tab selected (revealing the panel if another tab was showing); a utility that is a **document** has its tab activated and brought to the front, opening it first if it was closed. A custom utility's backing file is seeded on first reveal.
- With `area`, a custom utility is first moved to that area — reparenting its single live WebView, keeping all its state — and then revealed there. Moving to a document area docks it into that area's primary section (`main_left`, `bottom_left` or `side_top`) and reveals the area when it is collapsed; moving to `"utility"` returns it to the rail.

## Gotchas

- An unknown id returns an error. Use `app_list_utilities` to get the exact ids rather than guessing.
- An unrecognised `area` returns an error naming the accepted tokens, and an area the utility does not allow returns an error naming the areas it does. Read `allowedAreas` from `app_list_utilities` rather than guessing which areas a utility offers.
- `"document"` needs the utility to name one document area: it resolves to `default-area` when that is a document area, otherwise to the single document area the utility allows. A utility allowing several without defaulting to one of them returns an error asking you to name the area.
- Revealing a utility that is already in the requested area is a no-op (it stays put, with a brief highlight when it is a document).
- This tool reveals or relocates a utility; it never closes or destroys one. A utility that lives in the rail is never destroyed — closing its document tab docks it back into the Utility Panel rather than closing it. Project Settings, the Workshop and a contributed utility that opens a document are ordinary documents, so closing one of those closes it, and showing it again reopens it. `allowedAreas` tells the two apart: an entry that can be `"utility"` lives in the rail.
