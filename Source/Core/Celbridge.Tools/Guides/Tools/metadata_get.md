# metadata_get

Reads a single field from a resource's `.cel` sidecar frontmatter. Returns the value as a JSON-encoded string — strings come back as `"value"`, numbers as `42`, booleans as `true`, lists as `["a", "b"]`.

## Arguments

- `resource` — the resource key of the parent file (e.g. `"docs/notes.md"`, not the sidecar key).
- `field` — the top-level frontmatter field name. Case-sensitive.

## Returns

The JSON-encoded value on success. If the resource has no sidecar (or its sidecar is broken), an error explains that no frontmatter is indexed. If the sidecar exists but the field is absent, the response is an error naming the field — there is no "not found" sentinel value.

## Notes

- Frontmatter is keyed by the parent resource, not by the sidecar key. Reading `"docs/notes.md"` consults the sidecar at `"docs/notes.md.cel"`.
- Nested object values (`[section]` tables in TOML) can be returned but are not queryable via `metadata_find`. Use `metadata_list` to inspect the full structure.
- The metadata index reflects the on-disk state at the last completed scan; recent writes through `metadata_set` are visible synchronously because the tool awaits the post-write rescan.
