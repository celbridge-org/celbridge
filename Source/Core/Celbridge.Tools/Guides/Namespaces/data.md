# data

The `data` namespace reads and writes per-resource data stored in `.cel` sidecar files. A sidecar lives alongside its parent file (`foo.png.cel` next to `foo.png`) and carries TOML fields. The host scans `.cel` files on demand for tag queries and project-health checks; there is no persistent index.

## Must-knows

- **A broken sidecar blocks all `data_*` mutations.** When the sidecar fails to parse (invalid TOML, unterminated string), `data_set_fields`, `data_add_tags`, and their siblings refuse with a `Cannot mutate sidecar '...': TOML parse error(s): ...` message rather than silently overwriting the bad content. Repair by hand with `file_write` against valid TOML, then retry the mutation. `data_inspect` surfaces broken sidecars project-wide for batch triage.
- **Sidecars are addressed by their parent resource.** `data_get_fields docs/notes.md ["priority"]` consults the sidecar at `docs/notes.md.cel`. Passing the sidecar's own resource key (`docs/notes.md.cel`) is rejected with a clear error.
- **Sidecars are created on first write.** `data_set_fields` and `data_add_tags` create the sidecar when missing. `data_remove_fields` and `data_remove_tags` never create files and never delete sidecars (empty sidecars are kept).
- **Operations are batch-aware and atomic.** Every per-resource write tool takes a list/object and applies the whole batch in one read-modify-write — partial state is impossible. Reads also batch through `data_get_fields` (use `["*"]` to fetch every field).
- **Field values are JSON-encoded.** `data_set_fields` accepts each value as a JSON string so types pass through cleanly: `"\"high\""`, `"42"`, `"true"`, `"[\"a\", \"b\"]"`. Nested objects are rejected at write time.
- **Tags are the only structured cross-resource query.** Use `data_add_tags` / `data_remove_tags` for atomic mutation, `data_find_tag` to enumerate resources carrying one tag, and `data_list_tags` to enumerate every tag in use. The `tag:value` convention (`priority:high`, `status:draft`) covers most "search by field" needs.

## Tools

**Per-resource read.**

- `data_get_fields` — read a batch of named fields from a resource's sidecar (use `["*"]` for every field).
- `data_inspect` — per-resource sidecar inventory and health, scope from one resource to the whole project.

**Per-resource write.**

- `data_set_fields` — atomically write a batch of fields, creating the sidecar if missing.
- `data_remove_fields` — atomically remove a batch of fields; missing names are no-ops.

**Tag affordances.**

- `data_add_tags` — atomically append a batch of tags, creating the sidecar if missing.
- `data_remove_tags` — atomically remove a batch of tags; missing tags are no-ops.
- `data_find_tag` — find every resource whose tag list contains the given value.
- `data_list_tags` — enumerate the unique tag values across every healthy sidecar.

**Project-wide consistency.**

- `data_check_references` — find every dangling `"project:..."` reference in the workspace's allowlisted text files.

## When to use which surface

- "What does this sidecar carry?" → `data_inspect resources:[r]`.
- "What do these specific fields hold?" → `data_get_fields resource ["a", "b", "c"]`.
- "What resources are tagged X?" → `data_find_tag "X"`.
- "What tags exist in this project?" → `data_list_tags`.
- "Tag this resource so a future agent can find it" → `data_add_tags resource ["X"]`.
- "Are any sidecars in an attention state (broken / orphan / invalid)?" → `data_inspect` (no arguments).
- "Did my recent cascade leave any dangling references?" → `data_check_references`.

## Sidecar file format

A `.cel` sidecar is standard TOML. The writer picks the most readable on-disk representation for each string value: bare basic strings for short identifier-like content, literal triple-quoted strings (`'''...'''`) for verbatim multi-line content, and basic triple-quoted strings (`"""..."""`) only when the literal form cannot represent the content. The encoder is deterministic — the same input value always produces byte-identical output.

```toml
_tags = ["meeting", "draft"]
editor = "my-notes"
priority = "high"
summary = '''
A few lines of prose,
verbatim on disk.
'''
```

System metadata uses root-level field names that start with `_` (e.g. `_tags`). These appear at the top of the file in a canonical order; user-defined fields follow alphabetically. The reservation only applies at the root scope; nested-table keys may use any name.

The reserved namespace is closed to the field tools: `data_set_fields` rejects any `_`-prefixed field name, `data_get_fields` and `data_remove_fields` skip them, and the tag list (`_tags`) is surfaced through the dedicated tag tools and the `tags` key on `data_inspect`.

When repairing a sidecar by hand with `file_write`, write valid TOML; anything `Tomlyn` parses, the system accepts.

## Reading tool responses

Each `data_*` tool returns a single text payload. The Python `data.*` proxy and the JS `cel.data.*` proxy hand that payload back to the caller verbatim — no auto-parsing. Structured responses (`data_inspect`'s `{results, summary}` envelope, `data_get_fields`'s array of `{name, found, value}` records, `data_find_tag`'s resource list, `data_list_tags`'s `{tags}` envelope) are JSON-shaped strings that the caller parses:

```js
const inventory = JSON.parse(await cel.data.inspect(JSON.stringify([dataKey])));
const record = inventory.results[0];

const results = JSON.parse(await cel.data.getFields(dataKey, JSON.stringify(["graph", "viewport"])));
const graphResult = results.find(record => record.name === "graph");
```

```python
inventory = json.loads(data.inspect(json.dumps([data_key])))
record = inventory["results"][0]

results = json.loads(data.get_fields(data_key, json.dumps(["graph", "viewport"])))
graph_result = next(record for record in results if record["name"] == "graph")
```

Field values returned by `data_get_fields` carry their underlying TOML type — strings stay strings, integers stay numbers, lists stay arrays. The data layer is format-agnostic, so a string field carrying JSON, markdown, XML, or any other text round-trips byte-for-byte without the host interpreting it. Parse the field's content separately if your editor stored a structured payload there.

When loading state from a sidecar, distinguish three cases off `data_inspect`:

- `status: "NoSidecar"` → no sidecar on disk for this parent. Legitimate fresh state; seed defaults are appropriate.
- `status: "Healthy"` but your field is absent from the per-record `fields` array → sidecar exists, just doesn't carry that field. Log loudly before falling back to defaults — silent fallback hides bugs where a field went missing or your data shape changed.
- `status: "Healthy"` and your field is present → fetch it with `data_get_fields`; a `JSON.parse` failure or shape mismatch is unambiguously a bug, not a "fresh state" signal.

A broken sidecar surfaces as `status: "Broken"` with a `parseError` string; mutations against it fail until the file is repaired by hand.

## Notes

- Sidecars can also be read and written directly through the `file` namespace (`file_read docs/notes.md.cel`). Use the file tools for one-shot inspection or for repairing broken sidecars by hand; use the data tools for routine indexed field and tag access.
- For genuinely free-form search across `.cel` contents, use `file_grep --glob "*.cel"` and parse hits caller-side.
