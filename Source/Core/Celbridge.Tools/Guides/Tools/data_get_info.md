# data_get_info

Returns the parsed frontmatter of a resource's `.cel` sidecar inline plus an ordered list of block descriptors (id and byte size). One call answers both "what fields does this carry?" and "what blocks are present?"

Response shape:

```json
{
  "fields": {
    "editor": "celbridge.notes.note-document",
    "tags": ["meeting"],
    "priority": "high"
  },
  "blocks": [
    {"id": "celbridge.notes.note-document.content", "size": 1234},
    {"id": "celbridge.notes.note-document.revisions", "size": 567}
  ]
}
```

Returns `{ "fields": {}, "blocks": [] }` when the resource has no sidecar. Errors with a clear message when the sidecar exists but is broken; use `file_read` for raw inspection in that case, or `data_check_project` for the system-level view.

`size` is the UTF-8 byte count of the block's semantic content (matching what `data_read_block` returns). Block content is line-oriented: the terminator that separates one block from the next on disk is not part of the content, so a block's `size` is stable as adjacent blocks are added or removed.
