# metadata_remove

Removes a single field from a resource's `.cel` sidecar frontmatter. Idempotent — removing a field that is not present returns success without modifying the file.

## Arguments

- `resource` — the resource key of the parent file (e.g. `"docs/notes.md"`).
- `field` — the top-level frontmatter field name. Case-sensitive.

## Returns

`"ok"` on success (including the no-op case).

## Side effects

- The sidecar file remains on disk even when removing the last field. Empty sidecars are valid; the editor or user that created the file owns its lifetime.
- If no sidecar exists for the resource, the call is a no-op success — there is nothing to remove.

## Notes

- Use `metadata_remove_tag` to take a single value out of the standardised `tags` list. `metadata_remove` deletes the entire field.
