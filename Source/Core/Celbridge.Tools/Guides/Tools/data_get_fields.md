# data_get_fields

Reads a batch of named fields from a resource's `.cel` sidecar. Returns an array of `{name, found, value}` records in the same order as the input `names` list. Missing fields surface with `found: false` (no `value`) rather than failing the call, so a batch read can mix present and absent names without a per-name error chase.

## Parameters

- `resource` — the parent resource key (e.g. `notes.md`). Passing the sidecar's own key (`notes.md.cel`) is rejected with a typed error.
- `names` — JSON array of field-name strings. The single-entry sentinel `["*"]` returns every field on the sidecar in alphabetical order. Errors if the array is empty.

## Returns

```json
[
  { "name": "title", "found": true, "value": "Sunset" },
  { "name": "summary", "found": true, "value": "Photo shot at golden hour..." },
  { "name": "nonexistent", "found": false }
]
```

Each value carries the underlying TOML type: strings come back as JSON strings, integers as numbers, lists as arrays, etc.

## Reserved namespace

Field names beginning with `_` are reserved for system metadata. They return `found: false` here even when present on disk; the tag list (`_tags`) is surfaced through `data_add_tags` / `data_remove_tags` / `data_list_tags` and the per-resource `tags` key on `data_inspect`.

## Errors

- The resource has no sidecar.
- The sidecar exists on disk but does not parse (broken TOML); repair with `file_write` against valid TOML, then retry.
- `names` is missing, malformed, empty, or contains non-string entries.
