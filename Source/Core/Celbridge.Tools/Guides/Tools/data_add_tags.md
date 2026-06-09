# data_add_tags

Atomically appends a batch of tag strings to a resource's tag list. Creates the sidecar if missing. Idempotent: tags already on the list are no-ops; the file is rewritten only when at least one tag actually changes the on-disk state.

## Parameters

- `resource` — the parent resource key (e.g. `notes.md`). Passing the sidecar's own key (`notes.md.cel`) is rejected with a typed error.
- `tags` — JSON array of tag-string entries. Errors if the array is empty or contains an empty string.

## Tag conventions

Use the `tag:value` convention (`priority:high`, `status:draft`) to piggyback structured queries onto the tag surface — `data_find_tag "priority:high"` then enumerates resources carrying that value, and `data_list_tags` returns the full taxonomy across the workspace.

## Notes

- The tags are appended to the existing list in input order; duplicates within the input collapse to one append.
- On disk the list lives under the reserved `_tags` field. Agents see only `tags` in tool responses.
- The `file_*` byte-write tools refuse `.cel` targets to protect the sidecar's TOML structure; this is the structured route they point at.
