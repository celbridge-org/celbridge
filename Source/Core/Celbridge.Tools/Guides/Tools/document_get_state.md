# document_get_state

Returns the visual state of the document editor: which document is active, which tab strips are on screen, and every open document with its section, tab order, active flag, and bound editor id. Use this to understand what the user is currently looking at before deciding which tools to invoke or which document to operate on.

The snapshot is taken via the command queue, so it observes state after any commands you have already enqueued.

## Sections

The editor is divided into three areas. Main is always visible; Bottom and Side can be collapsed. Each area shows one tab strip, or two when the user splits it, giving six possible sections:

| Section | Area | Present when |
| --- | --- | --- |
| `main_left` | Main | always |
| `main_right` | Main | Main is split |
| `bottom_left` | Bottom | Bottom is visible |
| `bottom_right` | Bottom | Bottom is visible and split |
| `side_top` | Side | Side is visible |
| `side_bottom` | Side | Side is visible and split |

Main and Bottom split into left and right; Side splits into top and bottom.

A document can stay open in a collapsed area. It appears in `openDocuments` with its own section even though that section is absent from `visibleSections`.

The Focus and Presentation layout modes give the active document's area the whole panel, so `visibleSections` lists only that area's sections while either is active — one section, or two when the area is split. Every other document stays open and keeps its section in `openDocuments`.

## Returns

A JSON object with these fields:

- `activeDocument` (string) — resource key of the active document, or empty when no document is active.
- `visibleSections` (array of string) — the section names currently on screen, in reading order.
- `openDocuments` (array) — every open document tab, including tabs in a collapsed area. Each entry has:
  - `resource` (string) — the document's resource key.
  - `section` (string) — which section the tab lives in, as one of the names above.
  - `tabOrder` (int) — position within that section's tab strip.
  - `isActive` (bool) — `true` for the active tab in its section.
  - `editorId` (string) — the bound editor id (e.g. `"celbridge.code"`), or empty when no editor is bound yet.
