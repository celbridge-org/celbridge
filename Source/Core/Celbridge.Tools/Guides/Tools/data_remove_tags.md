# data_remove_tags

Atomically removes a batch of tag strings from a resource's tag list. Idempotent: tags absent from the list are no-ops. Drops the tag field entirely when the list becomes empty after removal.

## Parameters

- `resource` — the parent resource key (e.g. `notes.md`). Passing the sidecar's own key (`notes.md.cel`) is rejected with a typed error.
- `tags` — JSON array of tag-string entries. Errors if the array is empty or contains an empty string.

## Notes

- The sidecar file is kept after the last tag is removed; the file is rewritten only when at least one removal actually changes the on-disk state.
- On disk the list lives under the reserved `_tags` field. Agents see only `tags` in tool responses.
- The `file_*` byte-write tools refuse `.cel` targets to protect the sidecar's TOML structure; this is the structured route they point at.
