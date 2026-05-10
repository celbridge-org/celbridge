# explorer

The `explorer` namespace operates on the resource tree: it creates, renames, moves, copies, and deletes files and folders, and it manipulates the explorer panel's selection and expanded-folder state. The `file` namespace handles content reads and edits; `explorer` handles existence, location, and naming. Most explorer operations are routed through the same undo stack as user-driven actions.

## Must-knows

- **Resource keys are forward-slash paths relative to the project content root.** No backslashes, no absolute paths. `Scripts/hello.py` is a file; `Data` is a folder; the empty string is the project root. See `resource_keys`.
- **Most explorer mutations participate in the undo stack.** `explorer_undo` and `explorer_redo` reverse the last user-driven or tool-driven action. The undo unit is the operation, not the keystroke.
- **`explorer_rename` and `explorer_duplicate` are interactive.** They surface a dialog the user must confirm. For non-interactive renames, use `explorer_move`. See `silent_vs_interactive`.
- **Resolve "the folder I'm looking at" against the explorer selection.** Call `explorer_get_state` to read selection and expanded folders before resorting to project-wide search. See `workspace_panels`.

## Tools

**Mutating operations.**

- `explorer_create_file`, `explorer_create_folder` — create a new resource at a given key.
- `explorer_rename` — interactive rename via dialog.
- `explorer_move` — non-interactive rename or move.
- `explorer_copy` — copy a resource to a new key.
- `explorer_duplicate` — interactive duplicate via dialog.
- `explorer_delete` — delete a resource. Sends to the system trash where supported.

**Selection and tree state.**

- `explorer_select` — focus a resource in the tree (auto-expands ancestors).
- `explorer_expand_folder` — expand or collapse a single folder.
- `explorer_collapse_all` — collapse every expanded folder.
- `explorer_get_state` — snapshot of selected resource(s) and expanded folders.

**Undo / redo.**

- `explorer_undo`, `explorer_redo` — step through the explorer undo stack.
