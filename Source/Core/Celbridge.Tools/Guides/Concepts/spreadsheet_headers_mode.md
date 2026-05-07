---
name: spreadsheet_headers_mode
description: How the headers mode on spreadsheet_read_sheet returns rows keyed by header name, and the rules for collisions and blanks.
---

# Spreadsheet headers mode

Set `headers: true` on `spreadsheet_read_sheet` to treat the first row in the requested range as column names. Each subsequent row is returned as an object keyed by header.

```python
spreadsheet.read_sheet(resource="data/sales.xlsx", sheet="Q1", headers=True)
# -> {"rows": [{"month": "Jan", "total": 100}, {"month": "Feb", "total": 200}], ...}
```

Two rules apply:

- **Duplicate header strings get a numeric suffix.** First duplicate becomes `name`, second is `name_2`, third is `name_3`, and so on.
- **Empty header cells are replaced with `column_<letter>`.** A blank cell in column A becomes the key `column_A`; a blank in column C becomes `column_C`.

## When to use each mode

- **Use headers mode** when the agent task is "give me the rows by name" or "compute X across the rows" — the data is record-shaped and column letters don't matter.
- **Leave it off (the default)** when you want positional row arrays, especially for sub-ranges that don't include the header row, or when the column letters are meaningful (e.g. you're going to write back to the same cells).

## Restricted ranges with headers

`headers: true` interacts with `range`: the first row of the **requested range** is treated as the header. So `range="A5:C20", headers=true` uses row 5 as the header. If you only want a slice of a sheet's data without the actual header row, read the header row separately and pass `headers: false`.
