---
name: spreadsheet_get_active_view
description: Reads the persisted view state (active sheet, selection, active cell, scroll anchor) of a workbook and round-trips with spreadsheet_set_active_view.
---

# spreadsheet_get_active_view

Reads the workbook's persisted view state: the active sheet, the selection on that sheet, the active cell within the selection, and the scroll anchor. Use this to discover what a user is currently looking at, or to capture a view before mutating it.

## Response shape

| Field | Meaning |
|---|---|
| `sheet` | Name of the active worksheet. |
| `range` | First entry of `ranges` — a convenience for the common single-range case. |
| `ranges` | Full selection in A1 notation. May contain multiple entries for a non-contiguous (Ctrl+click) selection. Always at least one entry. |
| `activeCell` | Anchor cell within the selection (Excel's white cell). Equal to `range` when the selection is a single cell. |
| `topLeftCell` | The cell pinned at the upper-left of the visible viewport — the scroll anchor. |

A1 entries omit the sheet qualifier; the sheet they apply to is the value of `sheet`.

## Round-tripping with set_active_view

The shape mirrors the parameters accepted by `spreadsheet_set_active_view`. Pass `ranges` back through the `rangesJson` parameter on `set_active_view` to restore a non-contiguous selection.

```python
view = spreadsheet.get_active_view(resource="data/sales.xlsx")
# ... do work that may move the selection ...
spreadsheet.set_active_view(
    resource="data/sales.xlsx",
    sheet=view["sheet"],
    ranges_json=json.dumps(view["ranges"]),
    active_cell=view["activeCell"],
    top_left_cell=view["topLeftCell"])
```

## See also

- `spreadsheet_set_active_view` for the writer side, including selection vs. active-cell vs. scroll-anchor semantics.
- `spreadsheet_editor_division` for what is persisted in the workbook vs. owned by the SpreadJS editor.
