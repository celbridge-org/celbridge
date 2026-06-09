# explorer_move

Moves a single resource (file or folder) to a new location in the project tree. The original is removed. This is also the silent rename path — pass a destination with a different name in the same parent folder to rename. Folder moves are recursive. The move is recorded on the explorer undo stack.

A paired `.cel` sidecar moves alongside its parent. Every quoted `"project:<source>"` reference to the moved resource (and, for folder moves, every `"project:<source>/<child>"` reference under it) is rewritten in place across the project, so other files keep pointing at the new location. References must be in the canonical quoted form to participate — see [resource_keys](../Concepts/resource_keys.md).

## destinationResource resolution

Resolved against the source:

- If `destinationResource` names an existing folder, the source is moved **into** that folder, keeping its original name (`Notes/todo.md` to `Archive` becomes `Archive/todo.md`).
- Otherwise the destination is treated as the new full path and name (`Notes/todo.md` to `Notes/done.md` renames the file in place).

## Returns

The compact `"ok"` is reserved for the no-side-effect case: the move touched no references, no referencers were skipped, and no resources failed mechanically. Whenever the move actually rewrote references, left a cascade incomplete, or had a per-resource failure, the response is the JSON payload below — so an agent that needs to report what changed gets the rewritten-referencer list without a follow-up grep.

Both the compact `"ok"` string and the JSON `{"status":"ok", ...}` object indicate overall success — the difference is that the compact form means zero observable side effects, while the JSON form means at least one reference was rewritten or one cascade step ran. An agent that only branches on `response.status == "ok"` misses the compact-vs-JSON distinction; branch on the response shape (string vs object) first.

**`partial_failure` is a successful response, not an error.** Every status above — including `partial_failure` — comes back with the tool's success flag set. A move that was *entirely* refused (its only resource is locked, or its destination is hidden by the project's resource policy) still returns success with `status: "partial_failure"` and the reason in `failedResources[].message`. The tool reports an error (the MCP `isError` flag) only when the batch could not run at all — for example an invalid resource key. So never treat a non-error response as proof the move happened: confirm `status == "ok"` (or that `failedResources` is empty) before assuming the source moved. This mirrors `explorer_delete` and `explorer_copy`, which report per-resource refusals the same way.

```json
{
  "status": "ok" | "ok_with_skipped_referencers" | "partial_failure",
  "updatedReferencers": ["project:doc.md", ...],
  "skippedReferencers": [
    { "resource": "project:locked.md", "reason": "ReadOnly", "message": "file is read-only" },
    ...
  ],
  "failedResources": [
    { "resource": "project:source.txt", "message": "Write of 'project:keep.tmp' was denied by the [resources].ignore-file pattern '*.tmp'." },
    ...
  ]
}
```

Resource keys appear in their canonical `root:path` form (with the explicit `project:` prefix for project-rooted resources), matching the literal form the reference scanner detects in tracked content.

- `status`:
  - `"ok"` — every cascade step succeeded; `updatedReferencers` may be non-empty.
  - `"ok_with_skipped_referencers"` — the move itself completed but the cascade left some references stale (see `skippedReferencers`).
  - `"partial_failure"` — one or more resources in the batch were refused or failed (see `failedResources`). Still a success-flagged response.
- `updatedReferencers` lists the files whose references were rewritten.
- `skippedReferencers` lists the files the cascade couldn't update. `reason` is one of `ReadFailed` / `WriteFailed` / `ReadOnly` / `PermissionDenied`. `ReadOnly` is the DOS read-only attribute (trivially clearable); `PermissionDenied` is an ACL / POSIX denial (needs the right account or admin). The reference is left as-is and will surface at workspace load via the project-check reporter. Re-running the move after the blocker clears (clear the read-only flag, grant write access, close the editor that holds the lock) completes the cascade idempotently.
- `failedResources` lists the resources whose move was refused or failed, each with the `message` explaining why. This is where **policy denials** surface: a destination hidden by the project's resource policy (ignore-file or `[resources].remove`), or a source or destination frozen by `[resources].lock`, appears here with the reason — not as a tool error.

## Gotchas

- Moving the document currently open in the editor updates the tab to point at the new path; the tab does not close.
- Renaming a folder that contains open documents updates each open tab's resource path automatically.
- Read-only on the source itself is cleared before the move; read-only on a referencer is *not* cleared (the user invoked move on the source, not on incidental referencers). The referencer is reported in `skippedReferencers` with `reason: "ReadOnly"`.
- A re-run after fixing a blocker completes the residual rewrites; the FS layer is idempotent under partial completion.
- The cascade does not distinguish a calling script, test prompt, or documentation file from a regular content file. Any file in the project whose body contains a quoted `"project:<source>"` reference — including the file driving the operation — appears in `updatedReferencers` and its bytes are rewritten in place. This is correct per spec but can surprise authors of test fixtures or how-to docs that quote reference paths as examples.
