# data_inspect

Per-resource inventory and health for `.cel` sidecars. Scope ranges from a single resource to the whole project; the response is always an array of per-resource records plus an aggregate summary.

## Parameters

- `resources` — JSON array of resource keys to inspect (e.g. `["notes.md", "drafts/article.md"]`). Optional.
- `pattern` — glob matched against resource keys (e.g. `"assets/**"`). Optional.
- `summaryOnly` — boolean. When `true`, `tags` and `fields` drop from each record; `status` and `parseError` stay. The aggregate summary counts always populate. Defaults to `false`.

Scope resolution mirrors `file_grep`:

| `resources` | `pattern` | Scope |
|---|---|---|
| empty | empty | Whole project: every parent file that could carry a sidecar, plus every orphan/broken/invalid sidecar. |
| set | empty | Just the listed keys. |
| empty | set | Every resource key matching the glob. |
| set | set | Union of the two. |

## Returns

```json
{
  "results": [
    {
      "resource": "project:notes.md",
      "status": "Healthy",
      "tags": ["draft"],
      "fields": [
        { "name": "title", "size": 11 },
        { "name": "summary", "size": 421 }
      ]
    },
    {
      "resource": "project:broken.md",
      "status": "Broken",
      "parseError": "TOML parse error(s): ..."
    },
    {
      "resource": "project:photo.png",
      "status": "NoSidecar"
    }
  ],
  "summary": {
    "healthy": 1,
    "broken": 1,
    "orphan": 0,
    "invalidSidecar": 0,
    "noSidecar": 1
  }
}
```

Status categories:

- `Healthy` — the sidecar exists and parses cleanly. `tags` is the resolved tag list; `fields` is an array of `{name, size}` records for every non-reserved field. `size` is an approximate byte-count of the field's value content (UTF-8 byte length for strings; ToString length for scalars; recursive sum for lists). It's an order-of-magnitude hint for "is this field worth fetching with `data_get_fields`?", not an exact on-disk byte count — the TOML framing (key, `=`, quotes, newline) is not included.
- `Broken` — the sidecar exists but its TOML does not parse. `parseError` carries the diagnostic; mutations against this resource are refused until the file is repaired by hand (`file_write` against valid TOML).
- `Orphan` — a `.cel` file exists on disk but its parent file is missing. The record's `resource` is the orphan sidecar key.
- `InvalidSidecar` — the resource key targets a `.cel.cel` shape (sidecars do not stack). The record's `resource` is the invalid key.
- `NoSidecar` — no sidecar file exists on disk for the resource.

The per-record `tags` array is the on-disk reserved `_tags` value surfaced under its domain name. The `fields` array does not include reserved (`_`-prefixed) names; the tag list and any other reserved metadata are surfaced through their dedicated keys.

`tags` is always present on a `Healthy` record, even when the sidecar carries no tag list (or had every tag removed). Check the array length, not its presence — `record.tags.length === 0` rather than `!record.tags`. The shape stays uniform so consumers do not need defensive null handling.

## When to use

- "What does this sidecar carry?" → `data_inspect resources:[r]`. Returns `Healthy` + tags + per-field size inventory in one round-trip.
- "Is the project in a consistent state?" → `data_inspect` with no arguments. The summary counts surface broken / orphan / invalid sidecars at a glance.
- "Are these resources tagged correctly?" → `data_inspect resources:[...]` and read the `tags` array per record.
- "Show only health, no content" → `data_inspect summaryOnly:true`.

## Notes

- Use `data_get_fields` to fetch field *values* — `data_inspect`'s `fields` array reports approximate sizes, not content, and the size estimate skips the full TOML encoding pass. The inventory call stays cheap regardless of how large the underlying values are.
- Healthy sidecars whose parent file has its sidecar at `parent.ext.cel` are addressed under the parent key, not the sidecar key. Passing the parent key (`notes.md`) returns the record; passing the sidecar key (`notes.md.cel`) inspects it directly and is mostly useful for orphan / invalid surfaces.
- A new project load runs the project-check reporter at workspace load and surfaces broken / orphan findings in the host log. `data_inspect` is the way to query the same state on demand from an agent.
