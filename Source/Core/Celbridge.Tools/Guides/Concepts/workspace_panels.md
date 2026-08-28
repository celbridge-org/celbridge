# Workspace panels

A loaded project arranges the UI around a central editor area. You can highlight any named part of this UI for the user with `app_spotlight` (which lists the landmark names); prefer that over describing locations in prose when onboarding or answering "where is X?".

- **Explorer** (left sidebar) — the project file tree, with toolbar buttons to add a file, add a folder, and open project settings. `explorer_*` tools create, move, rename, and delete resources; `explorer_undo` / `explorer_redo` reverse file system operations only, not document text edits.
- **Documents** — the editor area, filling the workspace to the right of the Utility Panel. It is divided into three areas: **Main** (centre, always visible), **Bottom** (below Main, collapsible) and **Side** (right, full height, collapsible). Each area shows one tab strip, or two when the user splits it, so a document can live in any of six sections. Splitting is driven from the document tab context menu — Split Right (Split Down for the Side area) moves that document into a new section — and an area folds back automatically as soon as one of its sections runs out of documents, so a split section is never empty. Documents drag between any two sections. `document_*` tools open, close, activate, and inspect tabs; `file_*` tools edit content. See `document_get_state` for the section names.
- **Search** — full-text search, reached from the Utility Panel rail alongside Explorer. From the agent, use `file_grep` for the same purpose.
- **Notification bar** (below the editor area) — shows project error and notification banners. It sizes to its content, so it takes no space when nothing is showing. Interactive consoles are `.console` documents opened like any other document.

The left sidebar is the **Utility Panel**: an icon rail switches its content between Explorer, Search, and any utilities the project's packages contribute. The Utility Panel and the two collapsible document areas are shown or hidden from the title-bar toggle buttons, and each collapsible area also has its own close button. Collapsing an area leaves its documents open, and they reappear in place when it is shown again. `app_get_state` reports which panels are currently visible and focused, and `activeUtility` names the rail surface currently shown.

The Focus and Presentation layout modes give the active document's area the whole panel and hide the other two. The area keeps its split, so a split area still shows both of its documents. Nothing about the layout is rearranged: leaving the mode brings the other areas back exactly as they were. While one of those modes is active, `document_get_state.visibleSections` lists only that area's sections.

## Utilities

A utility is an auxiliary surface — a colour picker, a scratchpad, a process view. A package contributes the utility editor, and a discovered package's utility appears automatically as a rail item in the Utility Panel alongside Explorer and Search — one per contribution. The user can dock a utility into a document tab and back into the panel at will (the same surface, moved, not a copy). Use `app_list_utilities` to see every utility (built-in and contributed) with its current `area` (`utility` for the panel, or a document area), and `app_show_utility` to reveal one by id wherever it currently is (optionally moving it to an `area` first). See `utility_documents` for how they are authored.

## Resolving ambiguous file references

When the user refers to "the file" or "this script" without naming it, resolve against workspace state, not against a project-wide search:

1. **Active document.** Call `document_get_state` and check `activeDocument`.
2. **Other open documents.** Same call, check `openDocuments`.
3. **Explorer selection.** Call `explorer_get_state` and check selected resource(s) and expanded folders.

Only after these don't resolve, fall back to `file_grep` or `file_get_tree`. Searching the whole project for an ambiguous reference burns time and risks acting on the wrong file.
