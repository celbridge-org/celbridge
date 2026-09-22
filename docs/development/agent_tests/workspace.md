# Workspace

The parts of the application outside a document: the panels, the dialogs, the menus, and the project
settings document. Read the [README](README.md) for the invariants, evidence rules and levels.

## Surfaces

The Explorer tree, the Search panel and its field, modal dialogs with text fields, the application's Edit
menu, the document tab strip and its context menu, and the project settings form.

## Cases

| Situation | Action | Expected | Level |
|---|---|---|---|
| A dialog's text field | cut, copy, paste, undo | each acts on the field | 1 |
| The Explorer tree | select all, copy, paste | the verbs act on resources, not on text | 1 |
| A dialog's text field, opened from a focused panel | select all | the field's text is selected; the panel behind keeps its own selection | 2 |
| A dialog with several controls, text selected in a field | Tab | focus moves within the dialog, and the field's text is unchanged | 2 |
| Any text field in the application, text selected | a chord the application binds to nothing | the field's text is unchanged | 2 |
| The Search field | paste, select all | each acts on the field, and the Explorer selection is untouched | 2 |
| A field in the project settings document | paste, select all | each acts on the field | 2 |
| A locked resource open as a document | paste, cut | refused, and the document is unchanged on disk | 2 |
| A dialog's text field, caret mid-text | the platform's end-of-line and start-of-line chords | the caret moves to each end of the field's text | 2 |
| A document focused, then the Edit menu opened | list the items and their enabled state, then reach each offered verb by its shortcut on the same document | the two agree: every verb the menu offers works by its shortcut | 2 |
| A document tab's context menu, just used to split the area | press SPACE | no item of the dismissed menu runs: the document stays open, the split remains, and the tab has not moved again | 2 |
| A dialog opened from a button in the project settings document, such as the browse button beside a shortcut's File field | type as soon as it opens, then cancel it and press SPACE | the text lands in the dialog's search field and not in the document behind, and SPACE then opens the same dialog again: the keyboard went back to the button that opened it | 2 |
| A dialog open, with a selection in its field and another in the document behind | inspect the Edit menu, then use the same verbs by shortcut | the two agree, and both act on the dialog's field rather than on the document behind it | 3 |
| A dialog open, text selected in its field | shortcuts for close and find | they do not act on the document behind it, and the field's text is unchanged | 3 |
| Focus on a toolbar or other chrome, just after editing a document | copy, and a verb that touches no clipboard | both still reach the surface the user was last editing, and the Edit menu offers what the shortcuts do | 3 |
| The Explorer's context menu, just used to copy a path | press SPACE | no item of the dismissed menu runs: no document opens and the tree is unchanged | 3 |
| The Explorer's context menu on a resource with another below it, just used to open Rename, and that dialog canceled | press Down | the selection moves to the resource below: the keyboard came back to the tree | 3 |
| The Explorer holding the keyboard, with the project settings document on screen | press empty space in the document, away from any control | the document takes the keyboard: `app_get_state` names Documents as the focused panel | 3 |
| The downloads folder picker in project settings, with the name of a folder other than `downloads` typed into its search field and Down pressed | press Enter | the highlighted folder is chosen: the Downloads folder field and the project file both name it | 3 |
| A shortcut to a project file, its Opens in list just opened with a click | press Down, then Enter | the open list keeps the keyboard: Enter chooses the next area, and the project file records it | 3 |
| The caret in a code editor document, and an alert raised over it by a tool call | accept the alert, then type | the text lands in the editor where the caret was: the page got the keyboard back | 3 |

## Not covered

What the commands themselves do — creating, renaming and deleting resources, or the results of a search.
The subject here is which surface a keystroke reaches.

A menu nested inside another, such as the submenu a sub-item opens, is not covered: it is not a flyout and
the host cannot tell when one closes.

Of the Explorer's own verbs only copy and paste are exercised, and only into the folder the resource
already sits in, which is the path that goes through the duplicate-name dialog. The Search panel's replace
field is not covered either.

## Platform

The rows about a dismissed menu keeping the keyboard apply to the macOS head. The keyboard is handed back
only where a hosted web view takes native focus of its own, so on the other heads the same sequence is
expected to leave focus where the toolkit put it.
