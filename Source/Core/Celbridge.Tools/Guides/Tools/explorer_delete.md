# explorer_delete

Deletes a resource. Folder deletes are recursive. A delete under `project:` is recorded on the explorer undo stack and can be reversed with `explorer_undo`, but undo only restores resources that the application itself deleted, so do not rely on it as a substitute for source control. A delete in another root, or of a symbolic link or junction or a folder that contains one, is permanent. See [undo_semantics](../Concepts/undo_semantics.md).

A paired `.cel` sidecar (e.g. `foo.png.cel` next to `foo.png`) is deleted alongside its parent and restored alongside its parent on undo. Nothing else changes: references to the deleted resource in other files are left as they are. Before a delete, find them as [resource_keys](../Concepts/resource_keys.md) describes.

## showDialog

When `false` (the default), the deletion proceeds silently. When `true`, a confirmation dialog opens and the tool waits for the user to confirm or cancel. Prefer the dialog form when the user has not explicitly approved this deletion in the current turn, especially for folders.

## Returns

For a clean delete (every resource deleted successfully and can be undone, and the sidecar cascade went through where one existed), returns `"ok"`. If the user cancels the `showDialog` confirmation, the result is still success — nothing happened, and the project is unchanged.

A JSON payload is returned whenever the response carries information the agent may need to act on, specifically any of:

- At least one resource failed mechanically (typed `outcome` other than `Deleted`).
- A sidecar cascade reported `Failed`.
- A delete cannot be undone (`undoable` is `false`).

The JSON shape is:

```json
{
  "batchOutcome": "DeletedAll" | "DeletedSome",
  "resourceResults": [
    {
      "resource": "project:doc.md",
      "outcome": "Deleted" | "NotFound" | "Locked" | "PermissionDenied" | "IOFailure",
      "sidecar": "NotPresent" | "Cascaded" | "Failed",
      "undoable": true | false,
      "failureMessage": "in use by another process (file may be locked by an editor or antivirus)"
    }
  ]
}
```

Resource keys appear in their canonical `root:path` form, with the explicit `project:` prefix for project-rooted resources.

- `batchOutcome` summarises the whole batch. `DeletedAll` means every resource was deleted. `DeletedSome` means at least one failed, and also covers the rare edge where every resource in the batch failed mechanically — inspect `resourceResults` for the per-resource detail.
- `resourceResults` carries one entry per input resource — for a folder delete that means one entry for the folder itself, not one entry per descendant file. `outcome` is typed so the agent can branch on the cause without parsing strings:
  - `NotFound` — the resource was already gone on disk. Treat as success — the user's intent is already satisfied.
  - `Locked` — another process holds the file (open editor, antivirus, indexer). The fix is usually to close the holding process and re-run.
  - `PermissionDenied` — an ACL / POSIX denial. The DOS read-only attribute is cleared before delete, so this is a genuine permissions problem that needs the right account or admin.
  - `IOFailure` — catch-all for disk full, network share gone, hardware error, and anything else not fitting the more specific reasons.
- `sidecar` reports the outcome of the paired `.cel` cascade per resource: `NotPresent` (no sidecar existed, or the parent delete didn't run), `Cascaded` (the sidecar was deleted alongside its parent), `Failed` (the sidecar cascade encountered an error — surfaced to the log).
- `undoable` is `true` when `explorer_undo` restores the resource. It is `false` for a delete outside `project:`, for a symbolic link or junction or a folder that contains one, and for a resource that was not deleted.
- `failureMessage` is the human-readable detail. `null` when `outcome` is `Deleted`.

## Gotchas

- A delete that targets the document currently open in the editor closes that tab. Document-level state (Monaco undo history, view position) is lost.
- Programmatic file edits made before the delete cannot be recovered through Monaco's undo, only through `explorer_undo`.
- Read-only files can be deleted; the read-only attribute is cleared before the operation. The cleared state persists through undo — a restored file is writable.
