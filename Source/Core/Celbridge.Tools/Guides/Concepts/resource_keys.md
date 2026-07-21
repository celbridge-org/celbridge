# Resource keys

All file and folder references in Celbridge tools use **resource keys**: forward-slash paths under a named root. The default root is the project tree; other roots address host scratch space and diagnostic logs.

## Form

A resource key has the optional `root:path` form. When no root prefix is given, the key resolves under the implicit `project:` root.

| Key | What it refers to |
|---|---|
| `readme.md` | A file at the top of the project tree |
| `Scripts/hello.py` | A nested file in the project tree |
| `Data` | A subfolder in the project tree |
| `project:` (or `""`) | The project root itself |
| `temp:staging/pkg/v1/file.txt` | A file under the `temp:` scratch root |
| `logs:session.log` | A file under the `logs:` diagnostic root |
| `utils:scratchpad._notepad` | A file under the `utils:` utility-document root |

## Roots

- `project:` — the visible project tree. The default root; the prefix is optional in input but always present in output. Use for all user content.
- `temp:` — host scratch space (`.celbridge/temp/`). Hidden from the resource tree. Used by host tools, scripts, and agents for transient artifacts and staging output. Contents are not version-controlled. **All contents are wiped on workspace load** — if you need data to persist, write under `project:` instead. Conventional sub-folders include `temp:staging/...`, `temp:scratch/...`, `temp:cache/...`, and `temp:downloads/...`.
- `logs:` — host diagnostic logs (`.celbridge/logs/`). Hidden from the resource tree. Used by the host engine, Python scripts, agents, and Console panel session loggers.
- `utils:` — persistent state for utility documents (`.celbridge/utils/`). Hidden from the resource tree, text search, and New File, and local to the machine (`.celbridge/` is gitignored). Unlike `temp:`, it is **not** wiped on workspace load — it is the durable home for a utility's per-project state. Addressed by a fixed key per utility (`utils:{package}.{contribution}{resource-extension}`); see `utility_documents`.

## Output canonical form

When a tool reports a resource key in its result or in an error message, it always carries the explicit root prefix:

- `project:` keys are reported as `project:Scripts/hello.py`, never bare `Scripts/hello.py`.
- Non-`project:` keys are reported with their full root prefix (e.g. `temp:staging/pkg/file.txt`).

This form matches the literal that the reference scanner detects in file content, so a key copied from a tool response can be pasted straight into a quoted reference without forgetting the prefix.

## Rules

- Forward slashes only. Backslashes are rejected.
- No leading slash. `/readme.md` is invalid.
- No absolute paths or drive letters. The key is always relative to its root's backing folder.
- Root prefixes are lowercase and match `[a-z][a-z0-9_]+`. Single-character roots and uppercase roots are rejected.
- An undeclared root (e.g. `unknown:foo`) is an error, not a missing-file failure.
- Resource keys are case-sensitive on every platform — including Windows, where the filesystem itself is case-insensitive. A key whose case doesn't match the on-disk canonical case is rejected at the resolve boundary, with the canonical form named in the error message. Take resource keys from tool responses (file listings, search results, tool outputs) rather than typing them by memory to keep the case correct.

When in doubt about which keys exist, call `file_get_tree("")` to list the top level of the project tree, or pass a folder key to list its contents.

## Writing references that the cascade can track

Celbridge maintains a reference graph so that rename, move, and delete operations can update other files that point at the affected resource (see `explorer_move`, `explorer_delete`). To participate in the graph, every reference must be written in one canonical form: the `project:` prefix immediately wrapped in ASCII double or single quotes.

The wrapping quotes are mandatory. The scanner does not detect references in unquoted prose, because heuristic "find the end of the key" rules are ambiguous in arbitrary text and produce silent miss-tracking on subtle edge cases. A single rule — always quoted — is the only form that survives every text format reliably.

### The canonical form

A tracked reference is exactly one of these byte sequences in the file:

```
"project:<key>"
```

```
'project:<key>'
```

The opening quote sits immediately before `project:`. The matching close quote ends the key. Strict matching applies — the bytes between the quotes are taken verbatim as the key.

### The escaped form (for references inside already-quoted strings)

When the reference sits inside a string that has already been quoted by the host format — most commonly a JSON, TOML basic, or C-family string literal — the quote that opens it has been escaped as `\"` or `\'`. The scanner recognises both two-character forms and the matching escaped close, so a reference embedded in a JSON string is tracked end-to-end:

```
"description": "See \"project:foo.md\" for details"
```

```
"description": 'See \'project:foo.md\' for details'
```

### Examples by host format

In TOML, the format's own string quotes are the wrapping quotes:

```toml
target = "project:docs/intro.md"
```

In JSON, same — the string-value quotes are the wrapping quotes:

```json
{"target": "project:docs/intro.md"}
```

In source code, the language's string-literal quotes are the wrapping quotes:

```csharp
var target = "project:docs/intro.md";
```

In markdown body prose, write the reference in quotes:

```
See "project:docs/intro.md" for details.
```

In a `.cel` sidecar field, TOML's string quotes again:

```toml
target = "project:docs/intro.md"
```

### Keys containing whitespace

The rule is the same: wrap in `"..."` or `'...'`. The bytes between the wrapping quotes (including any spaces) are taken as the key.

```
"project:docs/My Document.md"
```

```
'project:docs/My Document.md'
```

```
"See \"project:docs/My Document.md\" thanks"
```

Strict matching means surrounding whitespace inside the wrapping quotes is part of the key. Write `"project:foo.md"`, not `" project:foo.md "`, or the recorded reference won't match the file.

### Forms that are NOT tracked

| Form | Why not |
|---|---|
| `project:docs/intro.md` (bare, no quotes) | References must be quoted; bare prose is not scanned |
| `[project:docs/intro.md]` (brackets only) | Brackets are not delimiters; only `"` and `'` open a tracked key |
| `(project:docs/intro.md)` (parens only) | Same — only `"` and `'` are delimiters |
| `` `project:docs/intro.md` `` (backticks only) | Same — backticks are not delimiters |
| `docs/intro.md` (no `project:` prefix) | The `project:` marker is what the scanner looks for |
| `temp:scratch/notes.md` | Only `project:` references are tracked; `temp:` and `logs:` are not |
| `https://example.com/foo` | External URLs are not resource keys |

### Known limitations

- **Unicode "smart quotes" (curly forms of `"` and `'`) are not recognised** — only the ASCII forms (`"` U+0022 and `'` U+0027) count. Pasted content from Word, chat apps, or auto-formatting editors may carry visually-identical curly quotes that the scanner ignores; check the raw bytes if a reference silently fails to track.
- **JSON `\/` escape**: a reference written as `"project:foo\/bar.json"` (representing `project:foo/bar.json`) is not tracked — the scanner sees the literal `\` and treats it as a key boundary. JSON serialisers almost never emit `\/`; write `/` directly.
- **References inside non-allowlisted file types**: not tracked at all (see the "Where the scanner looks" section below). The scanner only walks a fixed set of data-bearing extensions — references inside other file types are mentions for human readers, not active links. Rename them manually when a referenced resource moves.

## Where the scanner looks

The reference scanner walks an explicit **allowlist** of data-bearing file extensions. A file's extension determines whether it participates; nothing else (parent file, location, content sniffing) overrides that gate. Quoted `project:` references inside an allowlisted file are tracked; quoted `project:` references inside any other file type are ignored.

The current allowlist:

| Category | Extensions |
|---|---|
| Sidecars | `.cel` |
| Scripts | `.js`, `.py`, `.ipy`, `.ipynb` |
| Tabular data | `.csv`, `.tsv` |
| Structured data and configuration | `.json`, `.jsonl`, `.ndjson`, `.yaml`, `.yml`, `.toml`, `.xml` |

A `.cel` sidecar attached to a parent whose extension is NOT on the list (e.g. `notes.md.cel` next to `notes.md`) is still scanned — the sidecar carries the `.cel` extension under `Path.GetExtension`, not the parent's `.md`. Sidecars are data regardless of what they're paired with.

### Files that are NOT scanned

Every extension not in the allowlist is skipped. The most common implications:

- **Markdown (`.md`)** — documentation, READMEs, runbooks, agent-prompt files. Quoted `"project:..."` literals inside `.md` are descriptive prose; they don't cascade and they don't show up as broken references.
- **Plain text (`.txt`)** — fixtures and notes. If you need cascade tracking for a fixture, use `.json` (or attach a `.cel` sidecar with the reference in a field).
- **Source code outside the listed languages** — e.g. `.cs`, `.ts`, `.cpp`. Add the extension to the allowlist if you need cascade support there.
- **HTML and CSS** — HTML uses `href`-shaped references that don't follow the `"project:..."` form; CSS doesn't address resources by key at all.
- **Binary files** (PNG, XLSX, PDF, etc.) — never scanned. A reference baked into a binary asset won't participate in the cascade — those workflows must use sidecar fields or a paired text file instead.

Files that are not scanned can still BE referenced. The allowlist gates what gets *read for references*, not what can appear *as a target*. A `.json` referencer pointing at a `.md` document is fully tracked; renaming the `.md` cascades through the `.json`.

If you find yourself reaching for a file type that isn't on the list, add the extension to `ScannableExtensions` (or open a follow-up if the use-case is shared across projects).
