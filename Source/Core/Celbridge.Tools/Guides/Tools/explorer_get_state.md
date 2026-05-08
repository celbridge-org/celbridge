---
name: explorer_get_state
description: Read the current explorer panel state — selected resources and expanded folders — to ground "this file" or "the folder I'm looking at" references.
---

# explorer_get_state

Returns the visual state of the explorer panel: the currently selected resource(s) and the set of folders that are expanded. Use this to resolve ambiguous references like "this file" or "that folder" before falling back to a project-wide search. Pair with `document_get_state` when the user might be referring to an open editor tab rather than an explorer selection.

## Returns

A JSON object with these fields:

- `selectedResource` (string) — the primary selected resource key. Empty string when nothing is selected.
- `selectedResources` (array of string) — every selected resource key, in selection order. Empty when nothing is selected.
- `expandedFolders` (array of string) — resource keys of every folder currently expanded in the tree.

## See also

- `document_get_state` — paired call for the editor area.
- `app_get_state` — coarser view that includes the focused panel.
- `workspace_panels` — how to resolve ambiguous file references against panel state.
- `resource_keys`.
