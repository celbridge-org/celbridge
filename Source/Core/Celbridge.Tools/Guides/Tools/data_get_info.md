# data_get_info

Returns the parsed frontmatter of a resource's `.cel` sidecar inline plus an ordered list of block descriptors (id and byte size). One call answers both "what fields does this carry?" and "what blocks are present?"

Response shape:

```json
{
  "hasSidecar": true,
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

`hasSidecar` distinguishes the two empty-result cases:
- `hasSidecar: false`, empty fields and blocks → the resource has no sidecar on disk. This is also what you get when the *parent* resource itself doesn't exist (the tool only inspects the sidecar file, not the parent). Use `file_get_info` first if you need to confirm the parent exists.
- `hasSidecar: true`, empty fields and blocks → the sidecar file exists but is genuinely empty (zero-byte canonical empty form).

The no-sidecar case looks like this — note it is a success response, not an error:

```json
{
  "hasSidecar": false,
  "fields": {},
  "blocks": []
}
```

Errors with a clear message when the sidecar exists but is broken; use `file_read` for raw inspection in that case, or `data_check_project` for the system-level view.

`size` is the UTF-8 byte count of the block's semantic content (matching what `data_read_block` returns). Block content is line-oriented: the terminator that separates one block from the next on disk is not part of the content, so a block's `size` is stable as adjacent blocks are added or removed.
