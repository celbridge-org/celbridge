# explorer_move

Moves a single resource (file or folder) to a new location in the project tree. The original is removed. This is also the silent rename path — pass a destination with a different name in the same parent folder to rename. Folder moves are recursive. The move is recorded on the explorer undo stack.

A paired `.cel` sidecar moves alongside its parent. Every quoted `"project:<source>"` reference to the moved resource (and, for folder moves, every `"project:<source>/<child>"` reference under it) is rewritten in place across the project, so other files keep pointing at the new location. References must be in the canonical quoted form to participate — see [resource_keys](../Concepts/resource_keys.md).

## destinationResource resolution

Resolved against the source:

- If `destinationResource` names an existing folder, the source is moved **into** that folder, keeping its original name (`Notes/todo.md` to `Archive` becomes `Archive/todo.md`).
- Otherwise the destination is treated as the new full path and name (`Notes/todo.md` to `Notes/done.md` renames the file in place).

## Returns

The compact `"ok"` is reserved for the no-side-effect case: the move touched no references, no referencers were skipped, and no resources failed mechanically. Whenever the move actually rewrote references, left a cascade incomplete, or had a per-resource failure, the response is the JSON payload below — so an agent that needs to report what changed gets the rewritten-referencer list without a follow-up grep.

```json
{
  "status": "ok" | "ok_with_skipped_referencers" | "partial_failure",
  "updatedReferencers": ["project:doc.md", ...],
  "skippedReferencers": [
    { "resource": "project:locked.md", "reason": "ReadOnly", "message": "file is read-only" },
    ...
  ],
  "failedResources": ["project:source.txt", ...]
}
```

- `status`:
  - `"ok"` — every cascade step succeeded; `updatedReferencers` may be non-empty.
  - `"ok_with_skipped_referencers"` — the move itself completed but the cascade left some references stale (see `skippedReferencers`).
  - `"partial_failure"` — one or more resources in the batch failed mechanically (see `failedResources`).
- `updatedReferencers` lists the files whose references were rewritten.
- `skippedReferencers` lists the files the cascade couldn't update. `reason` is one of `ReadFailed` / `WriteFailed` / `ReadOnly` / `PermissionDenied`. `ReadOnly` is the DOS read-only attribute (trivially clearable); `PermissionDenied` is an ACL / POSIX denial (needs the right account or admin). The reference is left as-is and will surface via `metadata_check_project` (Phase 5). Re-running the move after the blocker clears (clear the read-only flag, grant write access, close the editor that holds the lock) completes the cascade idempotently.
- `failedResources` lists source resources whose bytes operation failed.

## Gotchas

- Moving the document currently open in the editor updates the tab to point at the new path; the tab does not close.
- Renaming a folder that contains open documents updates each open tab's resource path automatically.
- Read-only on the source itself is cleared before the move; read-only on a referencer is *not* cleared (the user invoked move on the source, not on incidental referencers). The referencer is reported in `skippedReferencers` with `reason: "ReadOnly"`.
- A re-run after fixing a blocker completes the residual rewrites; the FS layer is idempotent under partial completion.
