# spreadsheet_remove_sheet

Removes a worksheet from a workbook. The deletion is destructive — every cell, formula, formatting, and per-sheet view state on that sheet is lost.

## Last-sheet guarantee

A workbook must contain at least one sheet, so removing the only remaining sheet fails. If the goal is to wipe a workbook to a single empty sheet, use `spreadsheet_clear` with an empty range on the surviving sheet (which preserves sheet identity) rather than removing and re-adding.

## Failure modes

- Sheet not found.
- Sheet is the only sheet in the workbook.

## See also

- `spreadsheet_clear` to empty a sheet in place without removing it.
- `spreadsheet_add_sheets`, `spreadsheet_rename_sheet`, `spreadsheet_move_sheet`, `spreadsheet_duplicate_sheet` for the rest of the sheet lifecycle.
