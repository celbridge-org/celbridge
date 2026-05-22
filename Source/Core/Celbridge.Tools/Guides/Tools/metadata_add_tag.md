# metadata_add_tag

Appends a tag to a resource's standardised `tags` list inside its `.cel` sidecar frontmatter. Creates the sidecar if missing. Idempotent — adding a tag that is already present is a no-op success.

## Arguments

- `resource` — the resource key of the parent file (e.g. `"docs/notes.md"`).
- `tag` — the tag string. Case-sensitive. Non-empty.

## Returns

`"ok"` on success.

## Side effects

- Creates the sidecar at `<resource>.cel` if no sidecar previously existed, with a single `tags` field containing the new tag.
- Existing tags in the list are preserved; the new tag is appended only when not already present.
- Other frontmatter fields are preserved unchanged.

## Notes

- Use this rather than `metadata_set` with `field="tags"` and `value_json="[\"new\"]"` so concurrent agents adding different tags don't clobber each other's append.
- Use `metadata_find "tags" "<tag>"` to look up resources by tag.
