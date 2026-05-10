# spreadsheet_rename_sheet

Renames an existing worksheet. References elsewhere in the workbook (formulas, named ranges, conditional formatting) are updated by the spreadsheet engine to point at the new name, so a rename is non-destructive for valid references.

## Failure modes

- Source sheet not found.
- New name collides with another sheet in the workbook.

## Returned names

The response includes both `previousName` and `newName`, which is useful when chaining rename calls or logging the transition.

## Renaming the default sheet

A freshly created `.xlsx` workbook starts with `Sheet1`. Rename it before adding more sheets:

```python
spreadsheet.rename_sheet(resource="data/sales.xlsx", sheet="Sheet1", new_name="Q1")
```
