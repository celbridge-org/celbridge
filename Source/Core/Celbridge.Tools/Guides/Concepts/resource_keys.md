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

## Roots

- `project:` — the visible project tree. The default root; the prefix is optional in input and omitted in output. Use for all user content.
- `temp:` — host scratch space (`.celbridge/temp/`). Hidden from the resource tree. Used by host tools, scripts, and agents for transient artifacts and staging output. Contents are not version-controlled. Conventional sub-folders include `temp:staging/...`, `temp:scratch/...`, and `temp:cache/...`.
- `logs:` — host diagnostic logs (`.celbridge/logs/`). Hidden from the resource tree. Used by the host engine, Python scripts, agents, and Console panel session loggers.

## Output canonical form

When a tool reports a resource key in its result or in an error message, it uses the canonical form:

- `project:` keys are reported as bare paths (e.g. `Scripts/hello.py`), never with the explicit `project:` prefix.
- Non-`project:` keys are reported with their full root prefix (e.g. `temp:staging/pkg/file.txt`).

So `file_read` against a missing `temp:foo/bar` reports `temp:foo/bar` in the error, never bare `foo/bar`.

## Rules

- Forward slashes only. Backslashes are rejected.
- No leading slash. `/readme.md` is invalid.
- No absolute paths or drive letters. The key is always relative to its root's backing folder.
- Root prefixes are lowercase and match `[a-z][a-z0-9_]+`. Single-character roots and uppercase roots are rejected.
- An undeclared root (e.g. `unknown:foo`) is an error, not a missing-file failure.
- Case sensitivity follows the underlying filesystem; on Windows the system is case-preserving but case-insensitive.

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

In a `.cel` frontmatter field, TOML's string quotes again:

```
+++
target = "project:docs/intro.md"
+++
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
- **JSON `\/` escape**: a reference written as `"project:foo\/bar.md"` (representing `project:foo/bar.md`) is not tracked — the scanner sees the literal `\` and treats it as a key boundary. JSON serialisers almost never emit `\/`; write `/` directly.
- **References inside `.md` files**: not tracked at all (see the "Excluded extensions" section below). Markdown is documentation; references inside it are mentions for human readers, not active links. Rename them manually when a referenced resource moves.

## Where the scanner looks

The reference scanner reads the full text of every text file in the project (skipping binary files via extension and content sniffing) — *except* for the deliberately excluded extensions below. Quoted `project:` references are tracked wherever they appear in scanned files:

- **Plain text and source files** — code, TOML/JSON/YAML configs, plain `.txt` files, etc.
- **Sidecar (`.cel`) frontmatter** — quoted `project:` references in the TOML frontmatter of a sidecar are tracked the same as anywhere else.
- **Sidecar (`.cel`) body** — and so is the opaque body section. Either location works equally for editor data that needs to track resources.

### Excluded extensions

**Markdown (`.md`) files are deliberately excluded from reference scanning.** A quoted `"project:..."` literal inside a `.md` file is treated as descriptive prose, not as an active reference. Documentation, READMEs, runbooks, and test prompts can mention resource keys in their canonical form for the reader's benefit without participating in cascades or `data_check_project`'s broken-reference detection.

Consequences of the exclusion:

- **Cascade does not rewrite references inside `.md` files on rename.** If you move `foo.md` and a doc file references it as `"project:foo.md"`, that mention stays as written. You (or an agent) need to update the doc manually — same as you would under any GUID-style addressing scheme where doc mentions are never machine-rewritten.
- **`data_check_project` does not report references inside `.md` files as broken.** A `"project:gone.md"` mention in a README won't surface as a finding even if `gone.md` is missing — the system can't reliably tell "agent meant a tracked reference but used the wrong extension" from "this paragraph describes what `gone.md` used to be." Doc accuracy is the author's responsibility.
- **Markdown files can still BE referenced.** Other (scanned) files referring to a `.md` via `"project:notes.md"` ARE tracked normally. The exclusion is about what happens to references *inside* `.md` content, not what can be referenced.

Other file types not currently scanned:

- **Binary files** (PNG, XLSX, PDF, etc.). A reference baked into a binary asset won't participate in the cascade — those workflows must use sidecar frontmatter or a paired text file instead.

The exclusion list is intentionally narrow: only `.md` today. Other documentation formats (`.rst`, `.adoc`, `.org`) may be added if concrete need emerges. Plain `.txt` stays scannable — it's the natural extension for fixtures and config-like data files where embedded references should track.
