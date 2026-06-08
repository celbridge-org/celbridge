# data_get_info

Returns the parsed fields of a resource's `.cel` sidecar inline.

Response shape:

```json
{
  "hasSidecar": true,
  "fields": {
    "editor": "celbridge.notes.note-document",
    "_tags": ["meeting"],
    "priority": "high"
  }
}
```

`hasSidecar` distinguishes the two empty-result cases:
- `hasSidecar: false`, empty fields → the resource has no sidecar on disk. This is also what you get when the *parent* resource itself doesn't exist (the tool only inspects the sidecar file, not the parent). Use `file_get_info` first if you need to confirm the parent exists.
- `hasSidecar: true`, empty fields → the sidecar file exists but is genuinely empty (zero-byte canonical empty form).

The no-sidecar case looks like this — note it is a success response, not an error:

```json
{
  "hasSidecar": false,
  "fields": {}
}
```

Errors with a clear message when the sidecar exists but is broken; use `file_read` for raw inspection in that case, or `data_check_project` for the system-level view.
