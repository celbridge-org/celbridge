---
name: guides_read
description: Fetch full bodies of one or more guides or per-tool guides from Celbridge's built-in library, plus Python and JavaScript invocation strings for tools.
---

# guides_read

Reads one or more entries from Celbridge's built-in agent guide library. A name may be a conceptual guide name (e.g. `resource_keys`) or a tool name (e.g. `file_grep`); the resolver tries both.

Tool entries also carry the Python and JavaScript invocation strings, so the agent doesn't have to translate from the MCP tool name when working inside scripts or contribution editors.

## Parameters

### names

A JSON-encoded array of guide or tool names. The argument is a string containing JSON, not a native array.

```
guides_read('["resource_keys"]')
guides_read('["resource_keys", "file_grep", "python_proxy_conventions"]')
```

Pass multiple names in one call when you already know what you need — it's cheaper than several round-trips.

## Returns

A JSON object with two fields:

- `results` — an array of `{name, kind, description, body, pythonInvocation?, javascriptInvocation?}` entries, one per name that resolved successfully. `pythonInvocation` and `javascriptInvocation` are only populated for tool entries.
- `unknown` — an array of names that resolved to neither a guide nor a tool. Unknown names are reported here rather than failing the whole call, so a partial success still returns useful results for the names that did resolve.

## Tool aliases without an authored guide

Every registered MCP tool has an authored guide; `unknown` should remain empty for any valid tool name. If a tool name appears in `unknown`, it indicates either a typo or a missing guide that should be reported.

## See also

- `guides_list` — enumerates the available names.
- `guides_search` — regex-search when you don't know the exact name.
