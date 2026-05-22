# metadata_list

Returns the full frontmatter for a resource's `.cel` sidecar as a JSON object.

## Arguments

- `resource` — the resource key of the parent file (e.g. `"docs/notes.md"`).

## Returns

A JSON object containing every top-level frontmatter field. Scalar fields appear as JSON primitives; lists appear as JSON arrays; tables appear as nested objects.

If the resource has no sidecar (or its sidecar is broken), returns `{}` — the empty object — so callers can iterate uniformly without branching on absence.

## Notes

- Resources without sidecars are not an error: a clean object is returned.
- Object-valued fields (`[section]` tables in TOML) are included but are not queryable via `metadata_find`. Callers needing to filter on nested values should read this response and filter locally.
- The response reflects the in-memory index, which is updated synchronously after `metadata_set` / `metadata_add_tag` / `metadata_remove` / `metadata_remove_tag` calls.
