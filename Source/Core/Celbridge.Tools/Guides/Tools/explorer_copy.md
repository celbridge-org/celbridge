# explorer_copy

Copies a single resource (file or folder) to a new location in the project tree. The original resource is left in place. Folder copies are recursive. The copy is recorded on the explorer undo stack and can be reversed with `explorer_undo`.

A paired `.cel` sidecar is copied alongside its parent. References inside the copied content are *not* rewritten — the copy points at the same targets as the original. If you want the copied content to reference the copies of its dependencies, edit the references after the copy (or rename them through `explorer_move`).

## destinationResource resolution

Resolved against the source:

- If `destinationResource` names an existing folder, the source is copied **into** that folder, keeping its original name (`Notes/todo.md` to `Archive` becomes `Archive/todo.md`).
- Otherwise the destination is treated as the new full path and name (`Notes/todo.md` to `Archive/old-todo.md` produces that file directly).

## Returns

For a clean copy, returns `"ok"`. On any failure the destination is not created and an error is returned.

When one or more resources in a batch failed mechanically (file locked, IO error), returns a JSON payload:

```json
{
  "status": "partial_failure",
  "failedResources": ["project:source.txt", ...]
}
```

Other resources in the batch are still copied; the failed ones are named so you can retry just those.
