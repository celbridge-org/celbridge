# file_write

Writes text content to a file, creating it if it does not exist. For existing files, the entire content is replaced. Use this for new files or when the whole file is being regenerated; for small targeted changes, prefer `file_edit`, `file_multi_edit`, or `file_replace` — they are more precise and less likely to clobber concurrent edits.

## Parameters

- `fileResource` — resource key of the file. Created automatically if missing. Parent folders must already exist.
- `content` — the new text content. Line endings are normalised before writing.

## Line endings

- **New files** are written with LF (`\n`) line endings regardless of host platform. This matches what most cross-platform toolchains expect and what coding agents naturally produce.
- **Existing files** preserve their on-disk line-ending convention. If the file is CRLF the rewrite stays CRLF; if it is LF the rewrite stays LF. The convention is detected from the existing content before this write.

Pass `\n` or `\r\n` in `content` indifferently; the tool converts to the chosen target. For exact byte-level control (e.g. mixed line endings, or LF in an otherwise-CRLF file), use `file_write_binary` and supply the bytes you want on disk.

## Returns

A JSON object with `lineCount` — the line count of the written content.

## Editing `.cel` sidecars

`file_write` accepts `.cel` targets — useful for seeding a fresh sidecar in bulk or repairing one that has become unparseable. The on-disk format is plain TOML; anything `Tomlyn` accepts is read back cleanly by the data layer.

For routine field and tag mutations on an existing sidecar, prefer the `data_*` tools — `data_set_fields`, `data_add_tags`, etc. They are shorter, handle quote-style autoselection automatically (literal triple-quoted for multi-line content, basic strings for single-line), skip no-op disk hits when the input matches the current state, and enforce the reserved-namespace rules (`_`-prefixed fields are off-limits except through the tag tools). `file_write` does none of that — it writes whatever bytes you pass.

The natural division: use `file_write` for *initial seeding* and *broken-sidecar repair*; use `data_*` for *routine mutation*.

## Gotchas

- Writes go straight to disk. If the file is open in an editor, the buffer reloads from disk and Monaco's undo history is wiped — the write is not Ctrl-Z-revertable.
