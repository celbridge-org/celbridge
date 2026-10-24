# explorer_move

Moves a single resource (file or folder) to a new location in the project tree. The original is removed. This is also the silent rename path — pass a destination with a different name in the same parent folder to rename. Folder moves are recursive. A move under `project:` is recorded on the explorer undo stack.

A paired `.cel` sidecar moves alongside its parent. Nothing else changes: references to the moved resource in other files still name its old key. Before a move, find and update them as [resource_keys](../Concepts/resource_keys.md) describes.

## destinationResource resolution

Resolved against the source:

- If `destinationResource` names an existing folder, the source is moved **into** that folder, keeping its original name (`Notes/todo.md` to `Archive` becomes `Archive/todo.md`).
- Otherwise the destination is treated as the new full path and name (`Notes/todo.md` to `Notes/done.md` renames the file in place).

## Returns

For a clean move, returns the compact `"ok"`.

When the move was refused or failed (a reserved path, a read-only root, a file lock, or an IO error), returns a JSON payload:

```json
{
  "status": "partial_failure",
  "failedResources": [
    { "resource": "project:source.txt", "message": "Write of 'project:.git/keep.tmp' was denied by the system policy pattern '.git'." },
    ...
  ]
}
```

Resource keys appear in their canonical `root:path` form, with the explicit `project:` prefix for project-rooted resources.

**`partial_failure` is a successful response, not an error.** It comes back with the tool's success flag set, even when the move was *entirely* refused (e.g. its only resource sits in a reserved folder). The reason is in `failedResources[].message`. This is where **policy denials** surface: a source or destination inside the reserved `.git` folder appears here with the reason, not as a tool error. The tool reports an error (the MCP `isError` flag) only when the batch could not run at all — for example an invalid resource key. So never treat a non-error response as proof the move happened: confirm the compact `"ok"` (or an empty `failedResources`) before assuming the source moved. This mirrors `explorer_delete` and `explorer_copy`, which report per-resource refusals the same way.

## Gotchas

- Moving the document currently open in the editor updates the tab to point at the new path; the tab does not close.
- Renaming a folder that contains open documents updates each open tab's resource path automatically.
- Read-only on the source itself is cleared before the move.
