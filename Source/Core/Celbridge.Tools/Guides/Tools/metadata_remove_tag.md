# metadata_remove_tag

Removes a tag from a resource's standardised `tags` list inside its `.cel` sidecar frontmatter. Idempotent — removing a tag that is not present (or removing from a resource that has no sidecar) is a no-op success.

## Arguments

- `resource` — the resource key of the parent file (e.g. `"docs/notes.md"`).
- `tag` — the tag string to remove. Case-sensitive.

## Returns

`"ok"` on success (including the no-op case).

## Side effects

- When the removed tag was the last entry in `tags`, the entire `tags` field is dropped rather than leaving an empty array.
- The sidecar file itself is never deleted, even when removing the last tag leaves an empty frontmatter — empty sidecars are valid.
- Other frontmatter fields are preserved unchanged.

## Notes

- Use this rather than `metadata_set "tags" "[<filtered>]"` so concurrent agents removing different tags don't clobber each other's edit.
