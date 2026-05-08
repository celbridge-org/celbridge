---
name: spreadsheet_set_auto_filter
description: Adds or clears the single auto-filter on a worksheet; only A1 cell ranges are accepted, not column or row ranges.
---

# spreadsheet_set_auto_filter

Sets or clears the auto-filter on a single worksheet. Each sheet supports at most one auto-filter; setting a new one replaces any existing filter. The Celbridge editor and Excel both render the filter as the familiar dropdown arrows on the header row.

## Range form

Only A1 **cell** ranges are accepted (e.g. `"A1:F100"`). Column-letter ranges (`"A:F"`) and row-number ranges (`"1:1"`) are rejected, because Excel anchors the filter to a concrete rectangular region.

- Empty `range` applies the filter to the worksheet's used range.
- `range` is ignored when `enabled` is false.

## enabled

- `enabled: true` (the default) applies the filter to `range`.
- `enabled: false` clears any existing auto-filter on the sheet.

## Filter values are not preset

This tool only sets the filter region, not the per-column filter criteria — the Celbridge MCP tools intentionally do not expose Excel's filter-criteria model. Users select filter values interactively in the editor.

## Response

`enabled` reflects the post-call state. `filterRange` is the A1 range the filter covers when `enabled` is true, or the empty string when the filter was cleared.
