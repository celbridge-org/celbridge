# Celbridge JavaScript API Reference

Access tools from a package extension via the `cel` global (or `celbridge.cel`).
All methods return Promises; errors throw `CelToolError` with { code, tool, message }.

## Manifest (do this first)

The `cel.*` proxy is built from your package's `requires_tools` allowlist at
`initialize()` time. Namespaces you did not declare are NOT present on the
proxy — calling `cel.document.open(...)` without declaring `document.*` throws
`TypeError: Cannot read properties of undefined`, not `CEL_TOOL_DENIED`.

Declare every tool your package calls in package.toml before writing JS:

  [mod]
  requires_tools = ["document.*", "file.read", "app.get_status"]

Glob patterns are supported. The host re-enforces the allowlist on every
call; a tool within a partially-matched namespace that is not itself
allowed is rejected at call time with CEL_TOOL_DENIED.

### Naming: manifest vs. JS call site

The allowlist uses the tool's **alias** form: `namespace.snake_case_method`.
The signatures listed below use the **JS method** form: `cel.namespace.camelCaseMethod`.
The JS proxy converts snake_case to camelCase automatically — the manifest does not.

Matching pairs (always declare the left form, always call the right form):

  requires_tools entry       →  JS call site
  "file.list_contents"       →  cel.file.listContents(...)
  "file.read_binary"         →  cel.file.readBinary(...)
  "file.apply_edits"         →  cel.file.applyEdits(...)
  "explorer.create_folder"   →  cel.explorer.createFolder(...)

Do NOT use `"file.listContents"` (camelCase in the manifest) or underscore MCP
names like `"file_list_contents"` — both fail to match, and the `cel.*` proxy
will omit the namespace with no diagnostic beyond a later `TypeError`.

## Getting Started

Package extensions run inside a WebView hosted by a document editor contribution.
Import the client from the `shared.celbridge` virtual host, then await `initialize()`:

  import celbridge from 'https://shared.celbridge/celbridge-client/celbridge.js';
  await celbridge.initialize();
  const tree = await cel.file.getTree("");

`initialize()` performs the `document/initialize` handshake with the host and
loads the tool descriptors. Accessing `cel.*` before it resolves throws.
The celbridge-client source is served from the Celbridge install and is NOT
readable via `file.read` — use this reference for signatures.

## Conventions

- Arguments are positional and camelCase. Extra arguments throw CEL_TOOL_INVALID_ARGS.
- Methods marked `Promise<"ok">` resolve with the string 'ok' on success.
- Methods marked `Promise<void>` resolve with `undefined`.
- Error codes: CEL_TOOL_NOT_FOUND, CEL_TOOL_DENIED, CEL_TOOL_INVALID_ARGS, CEL_TOOL_FAILED.

## Structured parameters

Arrays and objects passed to `string`-typed parameters are JSON-encoded
automatically, so you can pass native JS values directly:

- `editsJson`: `[{ line: number, endLine: number, newText: string, column?: number, endColumn?: number }]`
- `resources` (in file.readMany): `string[]` of resource keys
- `files` (in file.grep): `string[]` of resource keys
- `fileResource` (in document.close): a single resource key or a `string[]`
