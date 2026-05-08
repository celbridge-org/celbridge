---
name: document_close
description: Close one or more open document tabs, optionally as a batch.
---

# document_close

Closes one or more documents in the editor. Pass either a single resource key or a JSON array of keys. Closes are processed sequentially, and a failure on one document does not stop the remaining attempts — the response summarises how many succeeded and which failed.

Closing a document always saves it first; there is no "discard changes" prompt. See `file_changes` for the full save model.

## Parameters

### fileResource

A single resource key (e.g. `"docs/readme.md"`) or a JSON array of keys (e.g. `'["a.md", "b.md"]'`). See `resource_keys` for the syntax.

### forceClose

When `true`, forces the close without going through the editor's confirmation path. Use sparingly — the default is the safe choice for normal flows.

## Returns

A JSON object summarising the batch:

- `closed` (int) — number of documents successfully closed.
- `failed` (int) — number of documents that failed to close.
- `errors` (array of strings) — error messages for the failed documents, in attempt order.

When `failed` is non-zero the call is reported as an error, but the JSON body still describes which closes did succeed.

## See also

- `document_open`, `document_activate` — paired open and activate operations.
- `editing_documents` — lifecycle overview.
- `file_changes` — auto-save behaviour on close.
