# data

The `data` namespace reads and writes per-resource data stored in `.cel` sidecar files. A sidecar lives alongside its parent file (`foo.png.cel` next to `foo.png`) and carries TOML frontmatter plus zero-or-more named content blocks. The host scans `.cel` files on demand for tag queries and project-health checks; there is no persistent index.

## Must-knows

- **A broken sidecar blocks all `data_*` mutations.** When the sidecar fails to parse (invalid TOML, unterminated string, garbled fence line), `data_set_field`, `data_add_tag`, `data_write_block`, and their siblings refuse with a `Cannot mutate sidecar '...': TOML parse error(s): ...` message rather than silently overwriting the bad content. Repair by hand with `file_write` against one of the three on-disk shapes below, then retry the mutation. `data_check_project` surfaces broken sidecars project-wide for batch triage.
- **Sidecars are addressed by their parent resource.** `data_get_field docs/notes.md priority` consults the sidecar at `docs/notes.md.cel`. Passing the sidecar's own resource key (`docs/notes.md.cel`) is rejected with a clear error.
- **Sidecars are created on first write.** `data_set_field`, `data_add_tag`, and `data_write_block` create the sidecar when missing. `data_remove_field`, `data_remove_tag`, and `data_remove_block` never create files and never delete sidecars (empty sidecars are kept).
- **Field values are JSON-encoded.** `data_set_field` accepts the value as a JSON string so types pass through cleanly: `"high"`, `42`, `true`, `["a", "b"]`. Nested objects are rejected at write time.
- **Tags are the only structured cross-resource query.** Use `data_add_tag` / `data_remove_tag` for atomic mutation and `data_find_tag` to enumerate resources carrying a tag. The `tag:value` convention (`priority:high`, `status:draft`) covers most "search by field" needs.
- **Content blocks are opaque text.** Block IDs follow `[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)*` (lowercase, dotted, hyphens). By convention each editor namespaces its blocks under its own ID (`celbridge.notes.note-document.content`).

## Tools

**Per-resource read.**

- `data_get_field` — read a single field value from a resource's sidecar.
- `data_get_info` — return frontmatter inline plus the list of block IDs and their byte sizes in one response.
- `data_read_block` — return the verbatim content of a named block.

**Per-resource write.**

- `data_set_field` — write a single field, creating the sidecar if missing.
- `data_remove_field` — remove a single field; no-op when absent.
- `data_write_block` — create or overwrite a named block.
- `data_remove_block` — remove a named block; no-op when absent.

**Tag affordances.**

- `data_add_tag` — append a tag, creating the sidecar if missing.
- `data_remove_tag` — remove a tag; no-op when absent.
- `data_find_tag` — find every resource whose `tags` list contains the given value.

**Project-wide health.**

- `data_check_project` — report broken `project:` references, orphan `.cel` files, and any `.cel` file that fails to parse cleanly.

## When to use which surface

- "What does this sidecar carry?" → `data_get_info`.
- "What does this specific field hold?" → `data_get_field`.
- "What resources are tagged X?" → `data_find_tag "X"`.
- "Tag this resource so a future agent can find it" → `data_add_tag`.
- "Read or write the prose body that an editor stores alongside this file" → `data_read_block` / `data_write_block`.
- "Is the project in a consistent state?" → `data_check_project`.

## Sidecar file format

A `.cel` sidecar is TOML frontmatter optionally followed by one or more named content blocks. The format has three on-disk shapes:

**Empty sidecar** — a zero-byte file (or one containing only whitespace). Carries no fields and no blocks. This is the canonical "minimal valid" sidecar shape.

**Frontmatter only** — plain TOML, no fence delimiters. There is no enclosing `+++` block:

```toml
editor = "celbridge.notes.note-document"
tags = ["meeting", "draft"]
priority = "high"
```

**Frontmatter plus named blocks** — TOML at the top, then each block introduced by a fence line of the exact form `+++ "<block-id>"` (one space, double-quoted ID, nothing else). Block content runs from the line after the fence to the line before the next fence (or to EOF):

```toml
editor = "celbridge.notes.note-document"
tags = ["meeting"]
+++ "celbridge.notes.note-document.content"
# Meeting Notes

Body of the note.
+++ "celbridge.notes.note-document.revisions"
rev-1
rev-2
```

Block IDs follow `[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)*`. The fence regex is strict: unquoted `+++` lines are NOT fences — they parse as TOML frontmatter (which will usually fail), classifying the sidecar as broken. When repairing a sidecar by hand with `file_write`, use one of the three shapes above.

## Notes

- Sidecars can also be read and written directly through the `file` namespace (`file_read docs/notes.md.cel`). Use the file tools for one-shot inspection or for repairing broken sidecars by hand; use the data tools for routine indexed field and tag access.
- For genuinely free-form search across `.cel` contents, use `file_grep --glob "*.cel"` and parse hits caller-side.
