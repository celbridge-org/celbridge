# Celbridge Python API Reference

Access tools via the `cel` proxy in the Python REPL.
Import namespaces with: `from celbridge import app, document, file`

## Conventions

- Parameters use snake_case. JSON results are returned as dicts.
- Errors raise `CelError` with a message string.
- Methods marked `-> ok` return the string 'ok' on success or raise `CelError`.
- Methods with no return annotation return `None`.

## Parameter Formats

Some parameters accept structured data as JSON-encoded strings. The proxy
auto-serializes Python lists and dicts (including nested structures with
str, int, float, bool, and None values) to JSON for these parameters,
so you can pass native Python objects directly:

- `edits_json`: a list of edit dicts:
  `[{"line": int, "endLine": int, "newText": str, "column"?: int (default 1), "endColumn"?: int (default -1 = end of line)}]`
- `resources` (in file.read_many): a list of resource key strings: `["a.txt", "scripts/b.py"]`
- `files` (in file.grep): a list of resource key strings: `["a.txt", "scripts/b.py"]`
- `file_resource` (in document.close): a single resource key or a list: `"a.txt"` or `["a.txt", "b.txt"]`
