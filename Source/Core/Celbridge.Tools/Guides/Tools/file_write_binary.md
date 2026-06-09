# file_write_binary

Replaces the entire content of a binary file with bytes decoded from a base64 string. Creates the file if it does not exist. There is no partial-edit equivalent for binary content — every write is wholesale.

## Parameters

- `fileResource` — resource key of the file. Created automatically if missing. Parent folders must already exist.
- `base64Content` — the new content as a standard base64 string. Invalid base64 fails the call.

## Returns

The literal string `"ok"` on success.

## Editing `.cel` sidecars

`file_write_binary` accepts `.cel` targets, but binary writes are almost never the right path for a TOML sidecar — the format is text and the data layer reads it as UTF-8. Use `file_write` for text-level seeding or repair, and the `data_*` tools (`data_set_fields`, `data_add_tags`, etc.) for routine mutation. Reach for `file_write_binary` on a `.cel` only when you genuinely need to control the byte sequence (e.g. preserving a specific BOM or line-ending convention) — and remember that a write producing non-UTF-8 or non-TOML content will surface as `Broken` through `data_inspect`.
