# spreadsheet_read_format

Reads cell formatting from a sheet. Returns one `FormatSpec` object per cell in the same shape accepted by `spreadsheet_format_ranges`, with most non-default properties included.

## Round-trip with spreadsheet_format_ranges

Feeding the output straight back into `spreadsheet_format_ranges` reproduces the source cell's fill and colour state on the destination. The empty string is the explicit clear/reset sentinel:

- Cells with no fill emit `backgroundColor` as the empty string.
- Theme and auto colours emit as the empty string.

Use this to inspect existing formatting, or to capture formatting before copying it to another range or sheet via `spreadsheet_format_ranges`.
