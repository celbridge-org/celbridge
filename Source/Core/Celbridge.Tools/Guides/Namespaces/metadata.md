# metadata

The `metadata` namespace reads and writes per-resource information stored in `.cel` sidecar files. A sidecar lives alongside its parent file (`foo.png.cel` next to `foo.png`) and carries TOML frontmatter plus an optional body. The host indexes the frontmatter so resources can be looked up by field value or by tag, and the indexes hydrate from a persisted cache on workspace load.

## Must-knows

- **Sidecars are addressed by their parent resource.** `metadata_get docs/notes.md priority` consults the sidecar at `docs/notes.md.cel`. The sidecar's own resource key (`docs/notes.md.cel`) is only used by direct file-tool reads, not by the metadata API.
- **Sidecars are created on first write.** `metadata_set` and `metadata_add_tag` create the sidecar when missing. `metadata_remove` and `metadata_remove_tag` never create files and never delete sidecars (empty sidecars are kept).
- **Values are JSON-encoded.** `metadata_set` and `metadata_find` accept the value as a JSON string so types pass through cleanly: `"high"`, `42`, `true`, `["a", "b"]`. Nested objects are stored in the file but not queryable.
- **Tags are a standardised list-of-string field.** Prefer `metadata_add_tag` / `metadata_remove_tag` over `metadata_set "tags" ...` so concurrent edits don't clobber each other's append.

## Tools

**Per-resource read.**

- `metadata_get` — read a single field value from a resource's sidecar.
- `metadata_list` — return the full frontmatter as a JSON object (empty object when the resource has no sidecar).

**Per-resource write.**

- `metadata_set` — write a single field, creating the sidecar if missing.
- `metadata_remove` — remove a single field; no-op when absent.

**Tag affordances.**

- `metadata_add_tag` — append a tag, creating the sidecar if missing.
- `metadata_remove_tag` — remove a tag; no-op when absent.

**Project-wide search.**

- `metadata_find` — find every resource whose frontmatter matches a field/value pair.
- `metadata_check_project` — report broken project: references, orphan sidecars, and any sidecar that fails to parse cleanly.

## When to use which surface

- "What metadata does this resource carry?" → `metadata_list`.
- "What does this specific field hold?" → `metadata_get`.
- "What resources are tagged X?" → `metadata_find "tags" "X"`.
- "What resources have `priority = high`?" → `metadata_find "priority" "high"`.
- "Add a tag to this resource so a future agent can find it" → `metadata_add_tag`.
- "Is the project in a consistent state?" → `metadata_check_project`.

## Notes

- Reads await the metadata index's first rebuild; the call blocks briefly during workspace startup until the index is ready.
- Writes are followed by a synchronous drain of the watcher / rescan queue, so the next `metadata_get` / `metadata_find` always sees the new state.
- Sidecars can also be read and written directly through the `file` namespace (`file_read docs/notes.md.cel`). Use the file tools when you need to inspect or repair sidecar contents by hand; use the metadata tools for normal indexed field access.
