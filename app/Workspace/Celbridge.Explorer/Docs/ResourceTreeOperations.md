# Resource Tree Operations Manual

This document describes the supported operations and behaviors for the Explorer Resource Tree.

---

## Selection Behavior

### Left-Click Actions

| Target | Result |
|--------|--------|
| Regular item | Select item, clear any multi-selection |
| Root folder | No effect (root is not selectable) |
| Empty space | Clear selection |

### Right-Click Actions

| Target | Selection State | Result |
|--------|-----------------|--------|
| Selected item | Single or multi | Preserve selection, show context menu |
| Unselected item | Any | Select only that item, show context menu |
| Root folder | Any | Clear selection, target root for context menu |
| Empty space | Any | Clear selection, target root for context menu |

### Keyboard Modifiers

| Modifier | Click Target | Result |
|----------|--------------|--------|
| Ctrl+Click | Item | Toggle item in/out of selection |
| Shift+Click | Item | Extend selection from anchor to clicked item |

---

## Root Folder Special Behavior

The root folder (project folder) has unique handling:

| Behavior | Value | Reason |
|----------|-------|--------|
| Selectable | No | Prevents accidental operations on project root |
| Draggable | No | Cannot move the project folder |
| Collapsible | No | Project contents should always be visible |
| Deletable | No | Cannot delete project root |
| Renamable | No | Project name managed elsewhere |
| Context menu | Yes | Allows Add File/Folder, Paste, Open in Explorer |
| Resource key | Empty string | Root has no relative path |

---

## Context Menu Operations

### Always Visible (Multi-select Enabled)

| Operation | Enabled When |
|-----------|--------------|
| Cut | Selection exists AND no root folder in selection |
| Copy | Selection exists AND no root folder in selection |
| Delete | Selection exists AND no root folder in selection |

### Single-Item or Root Operations

| Operation | Visible When | Enabled When |
|-----------|--------------|--------------|
| Add File | Single item OR root targeted | Always |
| Add Folder | Single item OR root targeted | Always |
| Paste | Single item OR root targeted | Clipboard has resources |
| Copy File Path | Single item OR root targeted | Always |
| Open in Explorer | Single item OR root targeted | Always |

### Single-Item Only (Not Root)

| Operation | Visible When | Enabled When |
|-----------|--------------|--------------|
| Run | Single item selected | Item is executable script |
| Open | Single item selected | Item is supported document |
| Rename | Single item selected | Item is not root folder |
| Copy Resource Key | Single item selected | Item is not root folder |
| Open in Application | Single item selected | Item is a file |

---

## Drag and Drop

### Drag Source Rules

| Source | Can Drag |
|--------|----------|
| Regular items | Yes |
| Root folder | No (drag cancelled) |
| Mixed (includes root) | Root filtered out, others dragged |

### Drop Target Rules

| Target | Allowed | Destination Folder |
|--------|---------|-------------------|
| Folder | Yes | That folder |
| File | Yes | File's parent folder |
| Empty space | Yes | Root folder |
| Root folder | Yes | Root folder |

### Modifier Keys

| Modifier | Operation |
|----------|-----------|
| None | Move |
| Ctrl | Copy |

---

## Keyboard Shortcuts

| Key | Context | Action |
|-----|---------|--------|
| Delete | Item(s) selected | Show delete dialog |
| Enter | Single item selected | Open (file) or Toggle expand (folder) |
| Right Arrow | Folder selected | Expand folder |
| Left Arrow | Folder selected (expanded) | Collapse folder |
| Left Arrow | Item selected (not expanded) | Select parent folder |
| Escape | Any | Clear selection |
| Ctrl+C | Item(s) selected | Copy to clipboard |
| Ctrl+X | Item(s) selected | Cut to clipboard |
| Ctrl+V | Item(s) selected | Paste from clipboard |

---

## Double-Click Actions

| Target | Action |
|--------|--------|
| File | Open document |
| Folder | Toggle expand/collapse |
| Root folder | Open in file explorer |
