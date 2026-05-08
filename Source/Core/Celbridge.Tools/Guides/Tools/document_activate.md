---
name: document_activate
description: Bring an already-open document to the foreground as the active editor tab.
---

# document_activate

Activates an open document so it becomes the active tab in the editor section it lives in. The document must already be open — call `document_open` first if it isn't. Use this when you need to direct the user's attention to a specific tab they've already opened, or when you want a follow-up `document_get_state` to reflect the document as active.

## Parameters

### fileResource

Resource key of the document to activate. See `resource_keys` for the syntax.

## Returns

`"ok"` on success. Returns an error if the document is not currently open.

## See also

- `document_open` — open a document, optionally activating it in one call (`activate: true`).
- `document_get_state` — confirm what is currently active and which sections are visible.
- `editing_documents` — overall lifecycle of opening, editing, and closing documents.
