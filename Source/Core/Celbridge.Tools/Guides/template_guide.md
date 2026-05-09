# template_guide

This file is the authoring template for per-tool guides under `Source/Core/Celbridge.Tools/Guides/Tools/`. It is not documentation for a real tool.

Every MCP tool registered in `Celbridge.Tools` must have a corresponding guide under `Tools/<tool_name>.md` (matching the `[McpServerTool(Name = "...")]` attribute). The load-time validator hard-fails app startup if any tool is missing its guide.

## How to use this template

1. Copy this file to `Source/Core/Celbridge.Tools/Guides/Tools/<tool_name>.md` (e.g. `file_apply_edits.md`).
2. Replace the body using the structure below.
3. Adapt the section headings to fit the tool. The structure here is a starting scaffold — drop sections that don't apply, add sections the tool genuinely needs. Existing guides in this folder are good references for how much variation is acceptable.

## Body structure

Guide files are plain markdown — no YAML frontmatter. The first line must be a `#` heading whose text matches the tool name (`# <tool_name>`). The body is free-form Markdown; use whatever subheadings best capture what's actually tricky about the tool. The following are the sections most guides will want, in roughly this order:

### Opening paragraph (required)

A single paragraph describing what the tool does and the typical use case. Two or three sentences. Treat this as the lede the agent reads after deciding to call the tool.

### Parameters (recommended)

Cover any parameter whose semantics, valid range, sentinel values, or defaults are non-obvious from the name. Skip parameters whose meaning is fully captured by their name and type. Use level-3 subheadings per parameter, or a parameter table when most parameters are similar in shape.

### Returns (recommended)

Describe the response shape, especially when fields have non-obvious interpretation (e.g. `totalRowCount` excludes the header row when `headers` is true). Skip if the return is a trivial success/failure result with no payload.

### Edge cases / Gotchas (when applicable)

The non-obvious failure modes, atomicity guarantees, sentinel values, and interactions with other tools. This is usually the highest-value section — agents that pick the right tool still trip on these.

### Examples (when applicable)

Short, concrete invocations showing the parameter shape. Prefer one or two well-chosen examples over an exhaustive matrix.

### See also (when applicable)

Cross-references to related tools, concept guides, or paired read/write tools. List by guide name (e.g. `spreadsheet_a1_notation`, `file_changes`); the agent can fetch them via `guides_read`.

## Style notes

- Match the prose style of existing guides under `Tools/`. Short paragraphs, concrete language, present tense.
- Do not duplicate cross-cutting content (A1 notation rules, resource-key syntax, the file save model). Reference the relevant concept guide under `Concepts/` instead.
- Plain backtick code spans for parameter names, tool names, and short literals. Fenced code blocks for multi-line examples or JSON.
- No emojis, no decorative separators, no heading marker comments.
