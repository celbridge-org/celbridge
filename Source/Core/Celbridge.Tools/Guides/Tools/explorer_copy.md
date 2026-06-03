# explorer_copy

Copies a single resource (file or folder) to a new location in the project tree. The original resource is left in place. Folder copies are recursive. The copy is recorded on the explorer undo stack and can be reversed with `explorer_undo`.

A paired `.cel` sidecar is copied alongside its parent. References inside the copied content are *not* rewritten — the copy points at the same targets as the original. If you want the copied content to reference the copies of its dependencies, edit the references after the copy (or rename them through `explorer_move`).

## destinationResource resolution

Resolved against the source:

- If `destinationResource` names an existing folder, the source is copied **into** that folder, keeping its original name (`Notes/todo.md` to `Archive` becomes `Archive/todo.md`).
- Otherwise the destination is treated as the new full path and name (`Notes/todo.md` to `Archive/old-todo.md` produces that file directly).

## Returns

For a clean copy, returns the compact `"ok"`.

When one or more resources were refused or failed (destination hidden by the project's resource policy, a `[resources].lock`, a read-only root, a file lock, or an IO error), returns a JSON payload:

```json
{
  "status": "partial_failure",
  "failedResources": [
    { "resource": "project:source.txt", "message": "Write of 'project:keep.bak' was denied by the [resources].ignore-file pattern '*.bak'." },
    ...
  ]
}
```

**`partial_failure` is a successful response, not an error.** It comes back with the tool's success flag set, even when the copy was *entirely* refused (e.g. a single resource whose destination the policy hides). The reason is in `failedResources[].message`. The tool reports an error (the MCP `isError` flag) only when the batch could not run at all — for example an invalid resource key. So never treat a non-error response as proof the copy happened: confirm `status == "ok"` (the compact string, or an empty `failedResources`) before assuming the destination exists. This mirrors `explorer_move` and `explorer_delete`.

Other resources in the batch are still copied; the failed ones are named with their reason so you can retry just those.
