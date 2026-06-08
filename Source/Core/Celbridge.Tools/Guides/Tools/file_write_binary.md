# file_write_binary

Replaces the entire content of a binary file with bytes decoded from a base64 string. Creates the file if it does not exist. There is no partial-edit equivalent for binary content — every write is wholesale.

## Parameters

- `fileResource` — resource key of the file. Created automatically if missing. Parent folders must already exist.
- `base64Content` — the new content as a standard base64 string. Invalid base64 fails the call.

## Returns

The literal string `"ok"` on success.

## Not for `.cel` files

`.cel` files are project metadata sidecars with a structured TOML and block-fence format. A byte-level binary write would corrupt that structure, so `file_write_binary` refuses any `.cel` target with a typed denial. Use the `data_*` tools to mutate sidecar contents through the structured surface.
