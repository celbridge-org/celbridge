# metadata_set

Writes a single field to a resource's `.cel` sidecar frontmatter. Creates the sidecar if it does not already exist.

## Arguments

- `resource` — the resource key of the parent file (e.g. `"docs/notes.md"`).
- `field` — the top-level frontmatter field name. Case-sensitive.
- `value_json` — the value as a JSON-encoded string. Supported shapes: scalars (`"string"`, `42`, `3.14`, `true`) and lists of scalars (`["a", "b"]`). Nested objects are rejected with a clear error.

## Returns

`"ok"` on success.

## Side effects

- The sidecar is created at `<resource>.cel` if missing. No envelope (`[celbridge]` table) is written unless the caller has previously set `celbridge.editor_id`.
- An existing sidecar's other fields and body are preserved; only the target field is replaced.
- The mutation is followed by a synchronous drain of the metadata-index update queue, so subsequent `metadata_get` / `metadata_find` calls see the new state without an extra wait.

## Notes

- For the standardised `tags` field, prefer `metadata_add_tag` / `metadata_remove_tag` so concurrent mutations don't clobber each other's append.
- Numbers parse as 64-bit integers when possible, otherwise as `double`. Cached values reload with the same canonicalisation, so a `42` written today still matches a `42` query after a project reload.
- Writing a value of an unsupported shape (nested object, mixed-type array) fails before any sidecar write occurs — the file on disk is never partially-mutated.
