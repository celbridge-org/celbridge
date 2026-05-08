---
name: spreadsheet_add_sheets
description: Batch-creates empty worksheets at the end of a workbook in append order, with collision rules and the JSON shape for sheetsJson.
---

# spreadsheet_add_sheets

Adds one or more empty worksheets to a workbook in a single open/save cycle. New sheets are appended after the existing sheets in the order given. Use this to scaffold a multi-sheet workbook before populating cells.

## sheetsJson

A JSON array of sheet name strings, e.g. `["Q1", "Q2", "Q3", "Q4"]`. Must contain at least one name. Names must be unique within the batch and must not collide with any sheet already present in the workbook. If any name is invalid or collides, the whole batch fails and nothing is saved.

## Position

New sheets are always appended after the existing sheets, in the order listed. To place a sheet elsewhere on the tab strip, follow up with `spreadsheet_move_sheet`.

## Renaming the default sheet

A freshly created `.xlsx` workbook starts with a single `Sheet1` worksheet. The typical scaffolding pattern is to rename it first and then add the rest:

```python
spreadsheet.rename_sheet(resource="data/sales.xlsx", sheet="Sheet1", new_name="Q1")
spreadsheet.add_sheets(resource="data/sales.xlsx", sheets_json='["Q2", "Q3", "Q4"]')
```

## See also

- `spreadsheet_workflows` for the full multi-sheet scaffolding pattern.
- `spreadsheet_remove_sheet`, `spreadsheet_rename_sheet`, `spreadsheet_move_sheet`, `spreadsheet_duplicate_sheet` for the rest of the sheet lifecycle.
