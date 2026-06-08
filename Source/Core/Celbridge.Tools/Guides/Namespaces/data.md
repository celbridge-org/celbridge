# data

The `data` namespace reads and writes per-resource data stored in `.cel` sidecar files. A sidecar lives alongside its parent file (`foo.png.cel` next to `foo.png`) and carries TOML fields. The host scans `.cel` files on demand for tag queries and project-health checks; there is no persistent index.

## Must-knows

- **A broken sidecar blocks all `data_*` mutations.** When the sidecar fails to parse (invalid TOML, unterminated string), `data_set_field`, `data_add_tag`, and their siblings refuse with a `Cannot mutate sidecar '...': TOML parse error(s): ...` message rather than silently overwriting the bad content. Repair by hand with `file_write` against valid TOML, then retry the mutation. `data_check_project` surfaces broken sidecars project-wide for batch triage.
- **Sidecars are addressed by their parent resource.** `data_get_field docs/notes.md priority` consults the sidecar at `docs/notes.md.cel`. Passing the sidecar's own resource key (`docs/notes.md.cel`) is rejected with a clear error.
- **Sidecars are created on first write.** `data_set_field` and `data_add_tag` create the sidecar when missing. `data_remove_field` and `data_remove_tag` never create files and never delete sidecars (empty sidecars are kept).
- **Field values are JSON-encoded.** `data_set_field` accepts the value as a JSON string so types pass through cleanly: `"high"`, `42`, `true`, `["a", "b"]`. Nested objects are rejected at write time.
- **Tags are the only structured cross-resource query.** Use `data_add_tag` / `data_remove_tag` for atomic mutation and `data_find_tag` to enumerate resources carrying a tag. The `tag:value` convention (`priority:high`, `status:draft`) covers most "search by field" needs.

## Tools

**Per-resource read.**

- `data_get_field` — read a single field value from a resource's sidecar.
- `data_get_info` — return fields inline in one response.

**Per-resource write.**

- `data_set_field` — write a single field, creating the sidecar if missing.
- `data_remove_field` — remove a single field; no-op when absent.

**Tag affordances.**

- `data_add_tag` — append a tag, creating the sidecar if missing.
- `data_remove_tag` — remove a tag; no-op when absent.
- `data_find_tag` — find every resource whose tag list contains the given value.

**Project-wide health.**

- `data_check_project` — report broken `project:` references, orphan `.cel` files, and any `.cel` file that fails to parse cleanly.

## When to use which surface

- "What does this sidecar carry?" → `data_get_info`.
- "What does this specific field hold?" → `data_get_field`.
- "What resources are tagged X?" → `data_find_tag "X"`.
- "Tag this resource so a future agent can find it" → `data_add_tag`.
- "Is the project in a consistent state?" → `data_check_project`.

## Sidecar file format

A `.cel` sidecar is standard TOML. The writer picks the most readable on-disk representation for each string value: bare basic strings for short identifier-like content, literal triple-quoted strings (`'''...'''`) for verbatim multi-line content, and basic triple-quoted strings (`"""..."""`) only when the literal form cannot represent the content. The encoder is deterministic — the same input value always produces byte-identical output.

```toml
_tags = ["meeting", "draft"]
editor = "celbridge.notes.note-document"
priority = "high"
summary = '''
A few lines of prose,
verbatim on disk.
'''
```

System metadata uses root-level field names that start with `_` (e.g. `_tags`). These appear at the top of the file in a canonical order; user-defined fields follow alphabetically. The reservation only applies at the root scope; nested-table keys may use any name.

When repairing a sidecar by hand with `file_write`, write valid TOML; anything `Tomlyn` parses, the system accepts.

## Reading tool responses

Each `data_*` tool returns a single text payload. The Python `data.*` proxy and the JS `cel.data.*` proxy hand that payload back to the caller verbatim — no auto-parsing. Structured responses (`data_get_info`'s `{hasSidecar, fields}` envelope, `data_get_field`'s JSON-encoded value, `data_find_tag`'s resource list, `data_check_project`'s report) are JSON-shaped strings that the caller parses:

```js
const info = JSON.parse(await cel.data.getInfo(dataKey));
const graphField = info.fields.graph;
```

```python
info = json.loads(data.get_info(data_key))
graph_field = info["fields"].get("graph")
```

Field values inside the envelope stay as strings — the data layer is format-agnostic, so a string field carrying JSON, markdown, XML, or any other text round-trips byte-for-byte without the host interpreting it. Parse the field's content separately if your editor stored a structured payload there.

When loading state from a sidecar, distinguish three cases off `data_get_info`:

- `hasSidecar: false` → no sidecar on disk for this parent. Legitimate fresh state; seed defaults are appropriate.
- `hasSidecar: true` but your field is absent from `fields` → sidecar exists, just doesn't carry that field. Log loudly before falling back to defaults — silent fallback hides bugs where a field went missing or your data shape changed.
- `hasSidecar: true` and your field is present → parse it; a `JSON.parse` failure or shape mismatch is unambiguously a bug, not a "fresh state" signal.

A broken sidecar surfaces as a tool-call error (not a returned payload), so the JS `try/catch` or Python `except CelError` sees it directly.

## Notes

- Sidecars can also be read and written directly through the `file` namespace (`file_read docs/notes.md.cel`). Use the file tools for one-shot inspection or for repairing broken sidecars by hand; use the data tools for routine indexed field and tag access.
- For genuinely free-form search across `.cel` contents, use `file_grep --glob "*.cel"` and parse hits caller-side.
