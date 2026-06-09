# file_set_writeable

Toggles the filesystem read-only attribute on a file or folder resource. Pass `writeable: true` to unlock; pass `writeable: false` to lock. Idempotent — re-running with the same value is a no-op.

## Parameters

- `fileResource` — the resource key of the file or folder.
- `writeable` — boolean. `true` clears the read-only attribute (write operations will succeed); `false` sets it (writes will fail until cleared).

## Returns

The literal string `"ok"` on success.

## When to use

The dominant use case is **unlocking files from external sources** that arrive read-only by convention:

- DCC tool exports (Maya, Houdini, ZBrush) when checked out from source control (Perforce, SVN, Git LFS).
- Animation frames produced by a render manager that locks outputs to prevent re-render races.
- Spreadsheets from email attachments or restricted network shares.
- Files extracted from archives (`.tar`, `.zip`) that preserved the read-only attribute.
- Anything from a vendor's "delivered as locked, expected to be unlocked before editing" workflow.

The typical pattern: an attempt to edit fails with a read-only error → call `file_set_writeable(resource, true)` → retry the original edit.

Use `file_get_info`'s `isReadOnly` field to check the state without attempting a write.

## Cascade integration

When `explorer_move` rewrites references inside files, a read-only referencer is reported in `skippedReferencers` with `reason: "ReadOnly"` and the move itself still completes. The agent can then unlock the referencer with `file_set_writeable(referencer, true)` and re-run the same move. The cascade rewriter is idempotent — re-running after the unlock completes the deferred rewrite without disturbing the files that were already updated.

## Notes

- Implemented as the Windows DOS `ReadOnly` attribute. On non-Windows backends (when those land), the closest equivalent is "remove all write permission bits"; the abstraction is leakier than it looks for cross-platform code, but for Windows-flavored Celbridge usage this is the right mental model.
- Setting `writeable: false` makes the file read-only. The locking direction exists for symmetry but most workflows use the unlock direction; if you find yourself locking files often, it's worth asking whether the workflow needs a different tool.
- Folders carry the attribute too; setting it on a folder affects only the folder entry itself, not the files inside (Windows DOS semantics).
