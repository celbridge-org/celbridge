# explorer_delete

Deletes a resource from the project tree. Folder deletes are recursive. The delete is recorded on the explorer undo stack and can be reversed with `explorer_undo`, but undo only restores resources that the application itself deleted, so do not rely on it as a substitute for source control.

A paired `.cel` sidecar (e.g. `foo.png.cel` next to `foo.png`) is deleted alongside its parent and restored alongside its parent on undo.

## showDialog

When `false` (the default), the deletion proceeds silently. When `true`, a confirmation dialog opens and the tool waits for the user to confirm or cancel. Prefer the dialog form when the user has not explicitly approved this deletion in the current turn, especially for folders.

## referencePolicy

Controls what happens when the resource you're deleting is referenced by other resources in the project (via quoted `"project:<key>"` literals in their text content — see [resource_keys](../Concepts/resource_keys.md) for the exact form). Three values:

- `"require_confirmation"` (default) — if external references exist, a confirmation dialog lists them and waits for the user. Decline cancels the delete. If no references exist, deletes silently.
- `"break_references"` — proceeds without prompting; the references in other files are left as-is and become dangling. Use when the agent has already gathered the user's intent and the dangling state is acceptable (e.g. the user is about to clean up the references themselves).
- `"fail_if_referenced"` — refuses to delete if external references exist; returns an error naming the referencers. Use for batch deletes where the agent needs to know about conflicts before proceeding.

Intra-batch references are filtered out: deleting `[a, b]` where `a` references `b` does not block on `b`'s referencer because `a` is also going away.

## Returns

For a clean delete (every resource deleted successfully, the sidecar cascade went through where one existed, and no external references were touched), returns `"ok"`. If the user cancels either confirmation dialog (the `showDialog` one or the reference-conflict one), the result is still success — nothing happened, and the project is unchanged.

A JSON payload is returned whenever the response carries information the agent may need to act on, specifically any of:

- At least one resource failed mechanically (typed `outcome` other than `Deleted`).
- A sidecar cascade reported `Failed`.
- The policy gate refused the batch (`CancelledByUser` / `BlockedByReferences`).
- External references were detected — whether they were broken (`break_references` policy) or blocked the batch (`fail_if_referenced`). The `referencers` field enumerates which files now have dangling references (`break_references`) or which files block the delete (`fail_if_referenced`).

The JSON shape is:

```json
{
  "batchOutcome": "DeletedAll" | "DeletedSome" | "CancelledByUser" | "BlockedByReferences",
  "resourceResults": [
    {
      "resource": "project:doc.md",
      "outcome": "Deleted" | "NotFound" | "Locked" | "PermissionDenied" | "IOFailure",
      "sidecar": "NotPresent" | "Cascaded" | "Failed",
      "failureMessage": "in use by another process (file may be locked by an editor or antivirus)"
    }
  ],
  "referencers": {
    "project:doc.md": ["project:other.md", "project:third.md"]
  }
}
```

- `batchOutcome` summarises the whole batch. `DeletedAll` and `DeletedSome` mean execution ran (the policy gate passed); `CancelledByUser` and `BlockedByReferences` mean the gate refused before any filesystem changes. `DeletedSome` also covers the rare edge where every resource in the batch failed mechanically — inspect `resourceResults` for the per-resource detail in any non-`DeletedAll` case.
- `resourceResults` carries one entry per input resource. `outcome` is typed so the agent can branch on the cause without parsing strings:
  - `NotFound` — the resource was already gone on disk. Treat as success — the user's intent is already satisfied.
  - `Locked` — another process holds the file (open editor, antivirus, indexer). The fix is usually to close the holding process and re-run.
  - `PermissionDenied` — an ACL / POSIX denial. The DOS read-only attribute is cleared before delete, so this is a genuine permissions problem that needs the right account or admin.
  - `IOFailure` — catch-all for disk full, network share gone, hardware error, and anything else not fitting the more specific reasons.
- `sidecar` reports the outcome of the paired `.cel` cascade per resource: `NotPresent` (no sidecar existed, or the parent delete didn't run), `Cascaded` (the sidecar was deleted alongside its parent), `Failed` (the sidecar cascade encountered an error — surfaced to the log).
- `failureMessage` is the human-readable detail. `null` when `outcome` is `Deleted`.
- `referencers` maps each input resource to the resources outside the batch that referenced it. Populated when external references were detected, whether the batch proceeded (`BreakReferences` policy) or was gated (`CancelledByUser` / `BlockedByReferences`).

## Gotchas

- A delete that targets the document currently open in the editor closes that tab. Document-level state (Monaco undo history, view position) is lost.
- Programmatic file edits made before the delete cannot be recovered through Monaco's undo, only through `explorer_undo`.
- Read-only files can be deleted; the read-only attribute is cleared before the operation. The cleared state persists through undo — a restored file is writable.
