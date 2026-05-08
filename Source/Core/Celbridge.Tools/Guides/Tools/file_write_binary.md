---
name: file_write_binary
description: Replace a binary file's contents from a base64 string; binary files have no targeted-edit tooling.
---

# file_write_binary

Replaces the entire content of a binary file with bytes decoded from a base64 string. Creates the file if it does not exist. There is no partial-edit equivalent for binary content — every write is wholesale.

## Parameters

- `fileResource` — resource key of the file. Created automatically if missing. Parent folders must already exist.
- `base64Content` — the new content as a standard base64 string. Invalid base64 fails the call.

## Returns

The literal string `"ok"` on success.

## See also

- `file_changes` — save model and how the editor reloads after the write.
- `file_read_binary` — read binary content as base64 first when modifying.
- `file_write` — text equivalent.
- `resource_keys`.
