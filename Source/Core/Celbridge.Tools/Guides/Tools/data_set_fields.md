# data_set_fields

Atomically writes a batch of fields to a resource's `.cel` sidecar, creating the sidecar if missing. The whole batch lands or none of it does — the sidecar is read once, mutated in memory, then written once. A validation failure on any field leaves the file untouched.

## Parameters

- `resource` — the parent resource key (e.g. `notes.md`). Passing the sidecar's own key (`notes.md.cel`) is rejected with a typed error.
- `fields` — JSON object mapping each field name to a JSON-encoded value string (the same `value_json` shape the singular tool used). Examples:
  - `"title": "\"Sunset\""`
  - `"count": "42"`
  - `"tags": "[\"a\", \"b\"]"`

Only scalars (string, number, bool) and lists of scalars are accepted; nested objects are rejected at write time.

## Atomicity

- All JSON values are parsed up front. Any failure aborts the whole batch with one error message naming every offending field.
- All values are validated as indexable up front.
- The sidecar file is written exactly once. If the canonical encoding before and after mutation is identical (e.g. every field is already at its target value), nothing is written and no watcher event fires.

## Reserved namespace

Field names beginning with `_` are reserved for system metadata and are rejected here. The tag list lives under `_tags` on disk; use `data_add_tags` / `data_remove_tags` to mutate it.

## Notes

- To mutate tags, use `data_add_tags` / `data_remove_tags`. They are append/remove-aware, so concurrent edits do not clobber each other.
- The `file_*` byte-write tools refuse `.cel` targets to protect the sidecar's TOML structure; this is the structured route they point at.
