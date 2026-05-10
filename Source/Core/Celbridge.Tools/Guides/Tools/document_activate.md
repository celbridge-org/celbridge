# document_activate

Activates an open document so it becomes the active tab in its editor section. The document must already be open — call `document_open` first if it is not. Use this to direct the user's attention to a tab they have opened, or when you want a follow-up `document_get_state` to reflect the document as active.

## Returns

`"ok"` on success. Returns an error if the document is not currently open.
