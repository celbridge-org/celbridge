---
name: python_proxy_conventions
description: How the Celbridge Python REPL exposes MCP tools via the cel proxy; parameter conventions, error handling, and structured-parameter formats.
---

# Python proxy conventions

Access tools via the `cel` proxy in the Python REPL, or import namespaces:

```python
from celbridge import app, document, file

document.open("readme.md")
app.log("Processing complete")
```

Module names match tool namespaces. Inside the REPL, `cel.app.log("hi")` and `app.log("hi")` are equivalent.

## Conventions

- **Parameters use snake_case.** `cel.file.apply_edits(...)`, not `applyEdits`.
- **JSON results are returned as dicts.** Tools that return structured payloads (e.g. `app.get_status`, `document.get_context`) deserialise into native Python dicts.
- **Errors raise `CelError`** with a message string. The REPL is configured to display these without a traceback so the message is the focus.
- **Methods marked `-> ok`** return the string `'ok'` on success or raise `CelError`.
- **Methods with no return annotation** return `None`.

## Discovering the API

Type `help(cel)` to list the namespaces, or `help(cel.file)` to see the methods available on one. Each method's docstring shows its parameters and return shape, sourced from the trimmed MCP tool description. For full detail, call `cel.docs.read([tool_name])`.

## Structured-parameter formats

Some parameters accept structured data as JSON-encoded strings. The proxy auto-serialises Python lists and dicts (including nested structures with `str`, `int`, `float`, `bool`, and `None` values) for these parameters, so you can pass native Python objects directly:

| Parameter | Shape |
|---|---|
| `edits_json` (in `file.apply_edits`) | `[{"line": int, "endLine": int, "newText": str, "column"?: int (default 1), "endColumn"?: int (default -1 = end of line)}]` |
| `resources` (in `file.read_many`) | `["a.txt", "scripts/b.py"]` |
| `files` (in `file.grep`) | `["a.txt", "scripts/b.py"]` |
| `file_resource` (in `document.close`) | A single resource key or a list: `"a.txt"` or `["a.txt", "b.txt"]` |

You don't need to call `json.dumps` yourself.

## Running scripts

The REPL is the canonical surface; standalone Python scripts can also import the package and run, but they need `CELBRIDGE_PROJECT_FOLDER` and `CELBRIDGE_MCP_PORT` set in the environment to find the broker.
