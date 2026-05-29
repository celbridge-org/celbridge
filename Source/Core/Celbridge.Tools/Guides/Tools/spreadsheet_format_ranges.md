# spreadsheet_format_ranges

Each edit is an object with `sheet`, `range`, and `format` fields. `range` is an A1 cell range, column letter or range, or row number or range. Edits may target different sheets and run in order. If any edit fails, the whole batch fails and nothing is saved.

Only fields present in each edit's `format` are applied — formatting outside the listed keys (or on cells the target range does not cover) is preserved.

## Format spec keys

Each `format` field's value shape is given below. Omit a field to leave that aspect untouched.

| Key | Value shape | Example |
|---|---|---|
| `textFormat` | object: `{bold?, italic?, underline?, strikethrough?, fontFamily?, fontSize?, foregroundColor?}` | `{"bold": true, "foregroundColor": "#FF0000"}` |
| `backgroundColor` | string CSS hex `#RRGGBB`, or `""` to clear | `"#FFFF00"` |
| `borders` | object: `{top?, bottom?, left?, right?}`, each side `{style?, color?}` | `{"top": {"style": "SOLID", "color": "#000000"}}` |
| `horizontalAlignment` | string: `LEFT`, `CENTER`, `RIGHT`, `GENERAL`, `JUSTIFY` | `"CENTER"` |
| `verticalAlignment` | string: `TOP`, `MIDDLE`, `BOTTOM` | `"MIDDLE"` |
| `wrapText` | bool | `true` |
| `numberFormat` | string Excel format pattern (see "Common number formats" below) | `"$#,##0.00"` |
| `columnWidth` | number in Excel character units; negative resets | `15` |
| `rowHeight` | number in points; negative resets | `20` |
| `autoFitColumns` | bool: when true, sizes the column to its content | `true` |
| `mergeRange` | bool: `true` merges, `false` unmerges any existing merge | `true` |

`textFormat.fontSize` accepts a non-positive value as a reset sentinel. Border `style` accepts `SOLID`, `DASHED`, `DOTTED`, `DOUBLE`, `NONE`, or any ClosedXML `XLBorderStyleValues` name.

## Common number formats

`numberFormat` is **only** a raw Excel format string — pass `"$#,##0.00"`, not a typed wrapper like `{"type": "CURRENCY", "pattern": "$#,##0.00"}`. Reach for these first:

| Goal | Pattern |
|---|---|
| Currency (USD) | `$#,##0.00` |
| Currency with negatives in red | `$#,##0.00;[Red]-$#,##0.00` |
| Percent (whole) | `0%` |
| Percent (two decimals) | `0.00%` |
| Date ISO | `yyyy-mm-dd` |
| Date US | `m/d/yyyy` |
| Date long | `mmm d, yyyy` |
| Accounting (USD) | `_($* #,##0.00_);_($* (#,##0.00);_($* "-"??_);_(@_)` |
| Plain number with commas | `#,##0` |
| Plain number with two decimals | `#,##0.00` |

## Units

- `columnWidth` is in **Excel character units, not pixels**. Default is 8.43, typical column is 10-60, anything above 100 is almost certainly wrong. Use `autoFitColumns: true` to fit width to content automatically.
- `rowHeight` is in **points**. Default is 15, typical row is 12-30.

## Clearing or resetting a value

To clear a colour or reset a value back to the workbook default:

| Field | Sentinel value |
|---|---|
| Colour fields (`backgroundColor`, `foregroundColor`, border colour) | empty string |
| `fontFamily` | empty string |
| `fontSize` | non-positive number |
| `columnWidth`, `rowHeight` | negative number |
| `mergeRange` | `false` (unmerges an existing merge) |
