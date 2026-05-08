---
name: python_proxy_conventions
description: Write Python scripts and REPL commands that call Celbridge tools — the cel proxy, imports, parameter conventions, error handling, and structured-parameter formats.
---

# Python proxy conventions

The `cel` proxy is the canonical way to call Celbridge tools from Python. It is available at the REPL prompt and inside scripts run from the REPL via `%run`:

```python
cel.document.open("readme.md")
cel.app.log("Processing complete")
```

Tool namespaces are also exposed as bare names — `from celbridge import app, document` then `app.log("hi")` — and the bare form is equivalent to `cel.app.log("hi")`. Use whichever reads better for the script; the examples in this guide and in per-tool guides use the `cel.*` form.

## Conventions

- **Parameters use snake_case.** `cel.file.apply_edits(...)`, not `applyEdits`.
- **JSON results are returned as dicts.** Tools that return structured payloads (e.g. `app.get_state`, `document.get_state`) deserialise into native Python dicts.
- **Errors raise `CelError`** with a message string. The REPL is configured to display these without a traceback so the message is the focus.
- **Methods marked `-> ok`** return the string `'ok'` on success or raise `CelError`.
- **Methods with no return annotation** return `None`.

## Discovering the API

Type `help(cel)` to list the namespaces, or `help(cel.file)` to see the methods available on one. Each method's docstring shows its parameters and return shape, sourced from the trimmed MCP tool description. For full detail, call `cel.guides.read([tool_name])`.

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

Scripts run inside the REPL via `%run` (or the **Run** context-menu command on a `.py` file, which dispatches to `%run`) share the REPL's namespace, so `cel.*` and bare-namespace forms work the same as at the prompt. To make the dependency on the proxy explicit at the top of a script, import it:

```python
from celbridge import cel

cel.app.log("Hello from a script")
```

Truly standalone Python processes — invoked outside the REPL — are not a supported surface for `cel`; the proxy is wired up at REPL startup and is not available in a fresh `python script.py` invocation. If you need to drive the broker from outside the REPL, you would call MCP directly with `CELBRIDGE_PROJECT_FOLDER` and `CELBRIDGE_MCP_PORT` set in the environment.
