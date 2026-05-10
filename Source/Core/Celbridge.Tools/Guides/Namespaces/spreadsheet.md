# spreadsheet

The `spreadsheet` namespace operates on `.xlsx` workbooks: reading and writing cells, adding and removing sheets, sorting and filtering, formatting, and capturing or restoring the active view. Spreadsheet operations are non-trivial — the cell-typing model, A1 notation, and headers-mode flag interact, and a misuse of any of them will silently produce wrong results, including on read-only operations. Read the relevant concept guides before you call.

## Must-knows

- **Read the concept guides before relying on cell values.** Spreadsheet reads can silently lie. Even a successful `spreadsheet_read_sheet` returns subtly wrong values if you misread the headers-mode flag or misinterpret the cell-typing semantics. The `spreadsheet_a1_notation`, `spreadsheet_cell_typing`, and `spreadsheet_headers_mode` concept guides are mandatory pre-reading.
- **Ranges are A1-notation strings.** `"A1:B10"`, not `(0,0,9,1)`, not `"A:B"`, not `"row 1 to row 10"`. See `spreadsheet_a1_notation`.
- **Cell typing is explicit.** Every cell carries a value and a type tag (`number`, `string`, `boolean`, `date`, `formula`, `error`). When writing, the type you supply is the type stored — `1` written as a string stays a string. See `spreadsheet_cell_typing`.
- **`headers: true` shifts row indexing.** Rows return as `{header: value}` objects, the header row is consumed, and `totalRowCount` excludes it. See `spreadsheet_headers_mode`.
- **Pagination is opt-in.** `spreadsheet_read_sheet` returns up to `pageSize` rows starting at `offset`; large sheets must be paged. See `spreadsheet_paging`.
- **The editor and tool surface share a workbook model.** Edits via these tools are visible to the open spreadsheet editor and vice versa. See `spreadsheet_editor_division`.
- **`spreadsheet_append_rows` is strict about row shape.** Every row must match the first row's field count — pad shorter rows with `null`. Cell values starting with `=` are stored as text, not formulas; for formulas use `spreadsheet_write_cells` with `isFormula: true`.
- **Conditional-formatting type names are arity-suffixed.** Use `colorScale2` (low + high) or `colorScale3` (low + mid + high) — there is no plain `colorScale`. See `spreadsheet_set_conditional_formatting`.

## Tools

**Reading.**

- `spreadsheet_get_info` — workbook structure: sheet names, used range per sheet, row and column counts.
- `spreadsheet_read_sheet` — read a range as values. Honours `headers` and pagination.
- `spreadsheet_read_format` — read formatting (fonts, fills, borders, number formats) for a range.
- `spreadsheet_find` — search for a value within a range.

**Writing cells and rows.**

- `spreadsheet_write_cells` — write values (with explicit types) to specific cells.
- `spreadsheet_append_rows` — append rows at the end of a sheet.
- `spreadsheet_insert` — insert blank rows or columns.
- `spreadsheet_delete` — delete rows, columns, or a range of cells.
- `spreadsheet_clear` — clear values or formatting from a range without removing structure.

**Formatting and views.**

- `spreadsheet_format_ranges` — apply font, fill, border, alignment, number formats.
- `spreadsheet_set_conditional_formatting` — add conditional rules.
- `spreadsheet_freeze_panes` — freeze rows / columns at a split point.
- `spreadsheet_set_auto_filter` — apply a sortable / filterable header bar to a range.
- `spreadsheet_sort` — sort a range by one or more keys.

**Sheet management.**

- `spreadsheet_add_sheets`, `spreadsheet_remove_sheet`, `spreadsheet_rename_sheet`, `spreadsheet_duplicate_sheet`, `spreadsheet_move_sheet` — manipulate the sheet collection.

**Active view.**

- `spreadsheet_get_active_view`, `spreadsheet_set_active_view` — read or change the editor's active sheet, selection, and scroll position.

**CSV interchange.**

- `spreadsheet_import_csv`, `spreadsheet_export_csv` — convert between a sheet and a `.csv` file.
