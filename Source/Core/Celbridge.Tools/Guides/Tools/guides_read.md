# guides_read

Reads one or more entries from Celbridge's built-in agent guide library by name. A name may be a per-tool guide name (e.g. `file_grep`), a namespace guide name (e.g. `file`), or the orientation guide (`agent_instructions`).

Most of the time you do not need to call this tool. Per-tool, namespace, and orientation guides auto-attach on first use of the relevant tool, namespace, or session, so the body arrives in the response without an explicit fetch. Use `guides_read` for the deliberate cases:

- the host context auto-compacted and a guide you saw earlier has scrolled out, or
- you want the orientation guide back without making a no-op call into a namespace.

Tool entries also carry the Python and JavaScript invocation strings, so the agent does not have to translate from the MCP tool name when working inside scripts or contribution editors.

## names

A JSON-encoded array of guide names. The argument is a string containing JSON, not a native array.

```
guides_read('["agent_instructions"]')
guides_read('["file_grep", "file"]')
```

Pass multiple names in one call when you already know what you need rather than chaining several round-trips.

## Returns

A JSON object with two fields:

- `results` — an array of `{name, kind, body, pythonInvocation?, javascriptInvocation?}` entries, one per name that resolved. Invocation strings are only populated for tool entries.
- `unknown` — names that resolved to neither a guide nor a tool. Unknown names are reported here rather than failing the whole call, so a partial success still returns useful results.
