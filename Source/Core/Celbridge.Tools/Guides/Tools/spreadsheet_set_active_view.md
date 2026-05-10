# spreadsheet_set_active_view

Sets the persisted view state of a workbook so a user opening the file lands on a chosen sheet, selection, active cell, and scroll position. The named `sheet` is always made active.

Selecting a cell auto-scrolls the viewport to it on open, so for "show this content" use a selection alone — `topLeftCell` is for the rarer case where you want to control the surrounding context (e.g. select row 50 but show rows 30-60).

If the workbook is open in the spreadsheet editor, the new view state is applied via the editor's normal external-reload path; the document tab does not close.

## range vs. rangesJson

- `range` is a single A1 cell or range. Empty string leaves the sheet's selection unchanged.
- `rangesJson` is a JSON array of A1 cells or ranges that together form a non-contiguous selection (e.g. `["A7:B8", "A12:B13"]`) — the Ctrl+click selection in Excel. When non-empty, takes precedence over `range`.

Each entry must omit the sheet qualifier.

## activeCell

The anchor cell within the selection (Excel's white cell within a multi-cell selection).

- Empty string → active cell becomes the first cell of the first range.
- Set with no selection → the selection becomes just this single cell.
- Set together with a selection → must lie inside one of the ranges.

Must be a single cell, not a range.

## topLeftCell

The cell anchored at the upper-left of the visible viewport on the target sheet. Empty string leaves the scroll position unchanged. Must be a single cell.

Scroll position is best-effort: frozen panes may clamp `topLeftCell`, and Excel or other host applications may reset it on open. A subsequent `spreadsheet_get_active_view` may report a different `topLeftCell` than the one written.

## Round-tripping a multi-range selection

The shape mirrors `spreadsheet_get_active_view`. Pass the `ranges` value from `get_active_view` back through `rangesJson` on `set_active_view` to round-trip a non-contiguous selection.
