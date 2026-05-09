# Tool naming

A single tool method is exposed under three names — the MCP form, the Python form, and the JavaScript form. They differ only in punctuation, but mixing them up is one of the most common parameter errors agents make.

| Surface | Form | Example |
|---|---|---|
| MCP tool name (in `tools/list`) | `<namespace>_<snake_method>` | `file_apply_edits` |
| Python REPL proxy (`cel.*`) | `cel.<namespace>.<snake_method>(...)` | `cel.file.apply_edits(...)` |
| JavaScript call site (in a package) | `cel.<namespace>.<camelMethod>(...)` | `cel.file.applyEdits(...)` |
| `requires_tools` manifest entry | `<namespace>.<snake_method>` | `"file.apply_edits"` |

The dot-form alias used in manifests matches the MCP tool name after swapping the first underscore for a dot. The JavaScript proxy converts the method portion to camelCase at the call site automatically; the manifest does **not**.

## Common pitfalls

- Calling `cel.file.applyEdits(...)` from Python (it's `apply_edits` there).
- Calling `cel.file.apply_edits(...)` from JavaScript (it's `applyEdits` there).
- Putting `"file.applyEdits"` or `"file_apply_edits"` in `requires_tools` — both fail to match, and the namespace is silently omitted from the JS proxy.
