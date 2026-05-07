---
name: file_changes
description: How saves and reloads work; why agent edits cannot be reverted with Ctrl+Z.
priority: 10
---

# File changes and saving

Celbridge saves automatically. There is no save tool, no "unsaved changes" dialog on close, and no user-facing flush command.

## Editing tools write straight to disk

`file_apply_edits`, `file_write`, `file_find_replace`, `file_delete_lines`, and `file_write_binary` write directly to the file. A follow-up `file_read` immediately sees the result.

If the document is open in the editor, its buffer reloads from disk after the agent's write. Monaco's undo history is wiped by the reload, so the user cannot revert the agent's edit with Ctrl+Z. Users who care about recovering pre-edit content rely on source control or backups.

## Editor edits

Edits typed by the user in the editor save automatically (~1 second after the last change). The agent does not need to flush the editor before reading; the per-view save timer drains before a `file_read` ever sees the file.

## External edits always win

If the file changes on disk while the editor's save is queued, the editor's save is discarded and the buffer reloads from disk. Concurrent agent writes therefore overwrite editor-side timing windows, not the other way around.

## Practical rules

- Treat every edit tool as immediately durable.
- Don't ask the user to save before reading; just call `file_read`.
- Don't try to undo an agent edit through Ctrl+Z — apply a reverse edit with `file_apply_edits` or `file_delete_lines`.
- For programmatic deletion of a file, use `explorer_delete`; this is undoable through the explorer's own undo stack.
