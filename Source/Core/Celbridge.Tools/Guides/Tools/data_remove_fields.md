# data_remove_fields

Atomically removes a batch of named fields from a resource's `.cel` sidecar. The sidecar is read once, mutated in memory, then written once. Missing field names are silent no-ops. Never creates a sidecar; if there is no sidecar on disk, the call is a no-op.

## Parameters

- `resource` — the parent resource key (e.g. `notes.md`). Passing the sidecar's own key (`notes.md.cel`) is rejected with a typed error.
- `names` — JSON array of field-name strings. Errors if the array is empty.

## Reserved namespace

Field names beginning with `_` are ignored at this surface — the field tools do not address the reserved underscore-prefixed keys. Use `data_remove_tags` to drop entries from `_tags`.

## Notes

- The sidecar file is kept after the last field is removed (an empty `.cel` is a valid state, not an error).
- The `file_*` byte-write tools refuse `.cel` targets to protect the sidecar's TOML structure; this is the structured route they point at.
