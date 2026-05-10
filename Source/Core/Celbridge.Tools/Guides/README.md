# Agent guide library

This folder holds the embedded markdown that the broker prepends to MCP tool responses. The runtime loads everything under `Concepts/`, `Namespaces/`, `Tools/`, and `Troubleshooters/` at app startup; this `README.md` is for human authors and is excluded from the embedded resource set in `Celbridge.Tools.csproj`.

For an authored example to copy from, look at an existing guide in the kind you are writing for. The "Reference guides" section at the end of this document lists strong examples per kind.

## What ships and how it auto-attaches

Four kinds, each in its own subfolder:

| Folder | Auto-attach trigger |
|---|---|
| `Concepts/` | Listed in a tool's `[RelatedGuides(...)]` attribute, attaches with that tool's first call |
| `Namespaces/` | First tool call into the matching namespace |
| `Tools/` | First call to the matching tool |
| `Troubleshooters/` | First time a `ToolResponse` helper that names this troubleshooter fires |

Plus one special case: `Concepts/agent_instructions.md` is the orientation guide. It attaches once to the first non-proxy tool call of every session, regardless of `[RelatedGuides]`.

Each guide attaches at most once per MCP session. If a session resumes (new MCP connection), state and guides re-attach on first use.

## Attachment order is broad-to-specific — do not repeat

For a given tool call, the agent's response carries (in order): the orientation guide, the namespace guide, the concept and troubleshooter guides named in `[RelatedGuides]`, then the per-tool guide. Each is delivered exactly once per session.

**Take advantage of that.** When you write a per-tool guide, assume the namespace guide and every guide in this tool's `[RelatedGuides]` are already in the agent's context. Cross-cutting topics (resource-key syntax, the file save model, the webview edit-reload-inspect loop, A1 notation) belong in the namespace or concept guide, not restated in every tool. The per-tool guide covers what is specific to *this* tool: parameter quirks, return shape, atomicity rules, the failure mode that catches agents out.

In practice this means: do not open every webview tool with "See `webview_devtools` for the edit-reload-inspect loop" — `webview_devtools` is auto-attached and the agent already has it. Same for "See `resource_keys` for the syntax" in `file_*` tools, "See `spreadsheet_a1_notation` for the format" in `spreadsheet_*` tools, etc. Empirically the agent does not chase a pointer to a guide it has already received.

## Universal rules

- **First line is `# <name>` heading.** The agent uses it for "I have already seen this; skip the body" recognition after a session resume. Do not start with a paragraph or front matter.
- **No YAML frontmatter.** The loader does not parse it.
- **No `<param>` / `<returns>` XML tags.** Those belong on the C# tool method, not in the guide body. The MCP source generator already exposes parameter descriptions to the agent; the guide is for what the parameter shape and signature do not say.
- **No emojis.** No decorative dividers, no section-marker comments.
- **Plain prose, present tense.** Short paragraphs. Backticks for parameter names, tool names, and short literals; fenced blocks for multi-line examples.
- **CRLF line endings** (Windows project convention).
- **Use full stops in English prose**, not semicolons. (C# statement terminators are unaffected.)

## Per-kind structure

### Tools/

These attach on first tool call and are the bulk of the library. Aim for 20-40 lines. The agent has just decided to call this tool — the lede orients them quickly, the body covers the things they will trip on.

Recommended sections, in order. Drop any that does not apply:

1. **Lede** (always) — one or two sentences: what the tool does and when to pick it over alternatives.
2. **Parameters** — only for parameters whose semantics, sentinel values, or interactions are non-obvious. Skip parameters whose meaning is fully captured by their name and type. Use `### param_name` per parameter, or a table when most parameters are similar in shape.
3. **Returns** — only when the response shape has non-obvious interpretation (e.g. `totalRowCount` excludes the header row when `headers: true`). Skip for trivial `"ok"` results.
4. **Failure modes / Gotchas / Edge cases** — usually the highest-value section. Atomicity guarantees, sentinel values, silent-failure rules, interactions with other tools.

Per-tool guides do **not** carry a `## See also` section. Empirically the agent does not chase pointers — it reaches for what is already in context. Use `[RelatedGuides]` to attach the concepts and troubleshooters the agent will actually need, and rely on the namespace guide to introduce sibling tools.

Match the structure of `file_grep.md`, `file_apply_edits.md`, or `spreadsheet_read_sheet.md` if in doubt.

### Namespaces/

One per `<namespace>_*` MCP tool family. Attach on first call into that namespace. Keep under 50 lines.

1. **Lede** — what this namespace covers and how it relates to siblings (e.g. `file` vs. `explorer`).
2. **Must-knows** — 3-6 bullets on the silent-failure rules, the conventions, the "if you forget this you will get subtly wrong results" items. Each bullet should reference the concept guide that carries the full rule. This is also where you bank cross-cutting context that would otherwise have to be repeated in every tool guide in the namespace.
3. **Tools** — categorised list of every tool in the namespace, one short line each. Group by purpose (Reading, Writing, Sheet management, Lifecycle...). The agent uses this to pick a tool without listing them.

Like per-tool guides, namespace guides do not carry a `## See also` section.

`file.md` and `spreadsheet.md` are the canonical examples.

### Concepts/

Cross-cutting topics referenced by one or more tools' `[RelatedGuides]`. Concepts that no tool references are flagged as orphans by the load-time validator and will fail app startup. There is no fixed structure — the goal is "the rule, with just enough mechanics that the agent does not have to ask a follow-up." Aim for 15-30 lines.

A concept guide typically:

- Opens with a one-paragraph statement of the rule or model.
- Walks the mechanics with a small table or short examples.
- Cross-references the tools that exercise the concept.

Look at `resource_keys.md` (rules + table), `regex_syntax.md` (rules + common patterns), or `spreadsheet_a1_notation.md` (range forms + per-tool subsetting note) for shape variations.

### Troubleshooters/

One per `ToolResponse` helper that maps an error class to a focused recovery note. The library validator enforces a 1:1 mapping between helpers and troubleshooter files.

1. **Lede** — what just happened, in one short paragraph.
2. **Recovering** — bulleted concrete steps. The agent has the failing input in scope; tell it how to repair the call.
3. **Common cases** or **Verifying** — optional, when the recovery splits into a few common sub-cases.

Aim for 15-20 lines. `troubleshoot_resource_key.md` is the reference.

## Validator rules to respect

- A per-tool guide filename must match the registered `[McpServerTool(Name = "...")]`.
- A namespace guide filename must match a real namespace prefix.
- Every name in a `[RelatedGuides(...)]` attribute must resolve to a loaded guide.
- Every concept guide must be reachable from at least one tool's `[RelatedGuides]` (only `agent_instructions.md` is exempt).
- Every troubleshooter guide must be referenced by at least one `ToolResponse` helper.

Renaming a guide file is a breaking change. Update every `[RelatedGuides]` reference and every `ToolResponse.WithTroubleshooter(...)` call site at the same time.

## Reference guides

Strong examples to copy structure from when authoring a new guide:

- **Tools** — `file_grep.md` (parameter-heavy with response-size cap), `file_apply_edits.md` (batch shape with edge cases), `spreadsheet_read_sheet.md` (compact param sections, headers-mode interaction).
- **Namespaces** — `file.md`, `spreadsheet.md`.
- **Concepts** — `resource_keys.md`, `regex_syntax.md`, `spreadsheet_a1_notation.md`.
- **Troubleshooters** — `troubleshoot_resource_key.md`.

The orientation guide `Concepts/agent_instructions.md` is load-bearing — every session reads it first. Edit it conservatively.
