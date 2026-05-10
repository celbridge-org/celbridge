# Spreadsheet cell typing

Cells round-trip with their Excel type preserved.

| Cell type | JSON representation |
|---|---|
| Number | JSON number |
| Date / DateTime | ISO 8601 string (e.g. `"2026-05-02T00:00:00.0000000"`) |
| Boolean | JSON `true` / `false` |
| String | JSON string |
| Error | JSON string with the `#` prefix (e.g. `"#DIV/0!"`) |
| Blank | JSON `null` |

`spreadsheet_write_cells` accepts the same shapes on input.

## Formulas vs. text

Strings beginning with `=` are written as **text** by default. To write a formula, set `isFormula: true` on the edit. Explicit beats sniffing.

```python
spreadsheet.write_cells(resource="data/sales.xlsx", sheet="Q1",
    edits=[{"cell": "C3", "value": "=SUM(A1:A10)", "isFormula": True}])
```

## Recalculation

Every write tool recalculates the workbook's formulas as part of the save, so a `spreadsheet_read_sheet` issued after a formula write returns the up-to-date computed value. Use `mode: "formulas"` on `spreadsheet_read_sheet` to read formula text instead of computed values.
