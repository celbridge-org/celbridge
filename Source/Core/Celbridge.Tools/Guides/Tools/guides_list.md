---
name: guides_list
description: Enumerate every guide in Celbridge's built-in agent guide library — names, kinds, and one-line descriptions you can pass to guides_read.
---

# guides_list

Lists Celbridge's built-in agent guide library. The library is meta-documentation about Celbridge itself — tool usage patterns, conventions, and gotchas — and is distinct from any markdown files in the user's project tree (those are content; this is help).

Use `guides_list` to scan the available titles when you don't yet know what's there. Use `guides_search` when you already know roughly what you want but not the exact guide name. Use `guides_read` to fetch full bodies.

## Returns

A JSON object with a single `guides` field — an array of `{name, kind, description}` entries.

- `kind` is one of `concept` (cross-cutting documentation under `Concepts/`, e.g. `resource_keys`, `regex_syntax`, `python_proxy_conventions`), `namespace` (per-namespace overview under `Namespaces/`, e.g. `file`, `spreadsheet`), or `tool` (per-tool guide under `Tools/`, e.g. `file_grep`, `spreadsheet_clear`).
- Concept guides come first, ordered by frontmatter `priority` then by name. Namespace guides come next, ordered alphabetically. Per-tool guides come last, ordered alphabetically.
- The `description` is the one-sentence summary from the guide's frontmatter — enough to decide whether to fetch the full body.

## Pairing with guides_read

`guides_list` returns names; `guides_read` consumes them. Pass the chosen names as a JSON-encoded array to fetch their bodies in a single call:

```
guides_read('["resource_keys", "file_grep"]')
```

## See also

- `guides_read` — fetches one or more guide bodies.
- `guides_search` — regex-search across the library.
- `agent_instructions` — the orientation guide; mandatory reading before any tool work on a fresh session.
