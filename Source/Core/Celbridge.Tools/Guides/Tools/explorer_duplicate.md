# explorer_duplicate

Creates a copy of a resource alongside the original. Silent by default — picks a unique name like `"foo - Copy.md"` (or `"foo - Copy (2).md"`, etc. on collision) in the same folder, performs the copy, and returns the new resource key. Pass `showDialog: true` for the interactive form where the rename dialog opens preseeded and the user confirms or types a different name.

The copy runs the same cascade as `explorer_copy` — a paired `.cel` sidecar is copied alongside the parent. References inside the duplicated content are *not* rewritten; they keep pointing at the original targets.

## showDialog

When `false` (the default), an auto-generated name is used and the duplicate happens without UI. When `true`, the rename dialog opens for the user to confirm or change the name.

## Returns

Silent form: a JSON payload with the new resource key:

```json
{
  "status": "ok",
  "createdResource": "notes/foo - Copy.md"
}
```

Dialog form: `"ok"` on success. If the user cancels the dialog, the result is still success and nothing is duplicated.

## Gotchas

- Auto-naming convention is Windows-style: `"foo - Copy.ext"`, then `"foo - Copy (2).ext"`, `"foo - Copy (3).ext"`, etc. A file with no extension (`README`) becomes `"README - Copy"`. A dotfile (`.gitignore`) becomes `".gitignore - Copy"` rather than `" - Copy.gitignore"`.
- For per-resource refinement (rename after duplicate, retarget references to point at the copy instead of the original), follow up with `explorer_move` or text edits.
