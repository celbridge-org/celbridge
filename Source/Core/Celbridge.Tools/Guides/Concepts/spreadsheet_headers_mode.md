# Spreadsheet headers mode

Set `headers: true` on `spreadsheet_read_sheet` to treat the first row in the requested range as column names. Each subsequent row is returned as an object keyed by header.

```python
spreadsheet.read_sheet(resource="data/sales.xlsx", sheet="Q1", headers=True)
# -> {"rows": [{"month": "Jan", "total": 100}, ...], ...}
```

Two rules apply:

- **Duplicate header strings get a numeric suffix.** First duplicate becomes `name`, second is `name_2`, third is `name_3`, and so on.
- **Empty header cells become `column_<letter>`.** A blank in column A becomes the key `column_A`; a blank in column C becomes `column_C`.

## When to use each mode

- **Use headers mode** when the task is "give me the rows by name" or "compute X across the rows" — record-shaped data where column letters do not matter.
- **Leave it off (the default)** when you want positional row arrays, especially for sub-ranges that don't include the header row, or when the column letters are meaningful (you intend to write back to the same cells).

## Restricted ranges with headers

`headers: true` interacts with `range`: the first row of the **requested range** is treated as the header. So `range="A5:C20", headers=true` uses row 5 as the header. To read a slice of data without consuming the actual header row, read the header row separately and pass `headers: false`.
