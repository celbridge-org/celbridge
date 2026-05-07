---
name: javascript_proxy_conventions
description: How JavaScript inside a contribution editor calls Celbridge tools through the cel global; manifest declarations, naming, errors, and structured parameters.
---

# JavaScript proxy conventions

Package extensions run inside a WebView hosted by a document editor contribution (declared in `package.toml` under `[contributes].document_editors`). There is currently no non-editor WebView surface, so JS tool calls happen from within an editor.

## Manifest first

Before writing any JS that calls `cel.*`, declare the tools your package needs in `package.toml` under `[mod].requires_tools`. The `cel.*` proxy is built from this allowlist at `initialize()` time, so namespaces you didn't declare simply do not exist on the proxy. Glob patterns are supported (`document.*`, `file.*`).

```toml
[mod]
requires_tools = ["document.*", "file.*", "app.get_status"]
```

## Naming: manifest vs. JS call site

Use the **alias form** — `namespace.snake_case_method` — in the manifest. This is the same form listed for the Python API, and matches the MCP tool name after swapping the first underscore for a dot. The JS proxy converts to camelCase at the call site, but the manifest does **not**.

| `requires_tools` entry | JS call site |
|---|---|
| `"file.list_contents"` | `cel.file.listContents(...)` |
| `"file.read_binary"` | `cel.file.readBinary(...)` |
| `"file.apply_edits"` | `cel.file.applyEdits(...)` |
| `"explorer.create_folder"` | `cel.explorer.createFolder(...)` |

Do not use camelCase (`"file.listContents"`) or the MCP underscore form (`"file_list_contents"`) — neither matches, and the namespace is silently omitted from the proxy.

## Initialising

Import the client from the `shared.celbridge` virtual host and call host tools through the `cel` global (populated after `initialize()` resolves):

```javascript
import celbridge from 'https://shared.celbridge/celbridge-client/celbridge.js';
await celbridge.initialize();
const tree = await cel.file.getTree("");
```

`initialize()` performs the `document/initialize` RPC handshake with the host and loads the tool descriptors. The celbridge-client source is served from the Celbridge install directory and is **not** readable via `file.read`.

## Conventions

- **Arguments are positional and camelCase.** Extra arguments throw `CEL_TOOL_INVALID_ARGS`.
- **Methods marked `Promise<"ok">`** resolve with the string `'ok'` on success.
- **Methods marked `Promise<void>`** resolve with `undefined`.
- **Errors throw `CelToolError`** with `{ code, tool, message }`. Common codes: `CEL_TOOL_NOT_FOUND`, `CEL_TOOL_DENIED`, `CEL_TOOL_INVALID_ARGS`, `CEL_TOOL_FAILED`.

## Failure modes

- Accessing `cel.*` before `initialize()` resolves throws `CelToolError`.
- Calling a namespace not covered by `requires_tools` throws `TypeError: Cannot read properties of undefined` — the namespace was never added to the proxy. Fix the manifest, not the call site.
- Calling a tool whose name isn't covered but whose namespace partially matches another entry throws `CEL_TOOL_DENIED` at call time (the host re-checks the allowlist on every call).

## Structured parameters

Arrays and objects passed to `string`-typed parameters are JSON-encoded automatically, so you can pass native JS values directly:

| Parameter | Shape |
|---|---|
| `editsJson` (in `file.applyEdits`) | `[{ line: number, endLine: number, newText: string, column?: number, endColumn?: number }]` |
| `resources` (in `file.readMany`) | `string[]` of resource keys |
| `files` (in `file.grep`) | `string[]` of resource keys |
| `fileResource` (in `document.close`) | A single resource key or a `string[]` |

For per-tool parameter detail, call `docs_read([tool_name])` (the agent surface) or browse the trimmed MCP tool descriptions.
