# spreadsheet_move_sheet

Moves an existing worksheet to a new tab position. Use this to reorder a workbook after `spreadsheet_add_sheets` (which always appends) or `spreadsheet_duplicate_sheet`.

## Position

`position` is a 1-based tab position. The valid range is `[1, sheetCount]`; values outside that range fail the call. Position `1` makes the sheet the leftmost tab; position equal to `sheetCount` makes it the rightmost.

The position is the absolute target on the tab strip, not a relative offset, and the count includes the sheet being moved (so moving the only sheet to position `1` is a no-op).

## Failure modes

- Sheet not found.
- Position outside `[1, sheetCount]`.

## Returned position

The response echoes the sheet name and its new 1-based position, which equals the input `position` on success.
