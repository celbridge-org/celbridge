# Global Replace - Requirements Specification

This document defines the requirements for global text replace functionality in Celbridge, building on the search system defined in `search.md`.

> **Prerequisite**: This feature requires the Global Search system to be implemented first.

> **Design Philosophy**: Celbridge is a data workbench for learning Python and data science, not a full IDE. Replace functionality should be straightforward and safe—helping users refactor variable names or fix typos across files without risk of data loss.

---

## 1. Core Features

| Requirement | Description | Priority |
|-------------|-------------|----------|
| **Replace All in File** | Replace all matches in a specific file | P0 |
| **Replace All** | Replace all matches across all files | P0 |
| **Undo Support** | All replacements undoable | P0 |
| **Confirmation Dialog** | Confirm before Replace All operations | P0 |
| **Replace Single** | Replace the currently selected match | P2 |
| **Preview Changes** | Show before/after preview before applying | P3 |
| **Preserve Case** | Match the case pattern of replaced text | P3 |

---

## 2. User Interface

### 2.1 Search Panel Layout (Extended)

```
┌─────────────────────────────────┐
│ 🔍 [Search..................]   │
│ ↻  [Replace with............]   │
│                                 │
│ ☐ Match case  ☐ Whole word      │
│                                 │
│ ▶ Filters (collapsed)           │
├─────────────────────────────────┤
│ 8 matches in 3 files            │
│ [Replace All]             [Clear]│
├─────────────────────────────────┤
│ 📄 analysis.py (4)     [Replace]│
│   12: ...context...             │
│   45: ...context...             │
│ 📄 utils.py (3)        [Replace]│
│   8: ...context...              │
└─────────────────────────────────┘
```

### 2.2 Controls

| Control | Location | Function | Priority |
|---------|----------|----------|----------|
| Replace input | Below search input | Text to replace with | P0 |
| Replace All | Results header | Replace all matches (with confirmation) | P0 |
| Replace (per-file) | File header | Replace all in that file | P0 |
| Replace (per-match) | Per result line | Replace single match | P2 |
| Dismiss (per-match) | Per result line | Remove result from list | P3 |
| Preserve case toggle | Options area | Enable case preservation | P3 |

---

## 3. Keyboard Shortcuts

| Shortcut | Action | Priority |
|----------|--------|----------|
| `Ctrl+Shift+H` | Open Search panel with Replace input focused | P1 |
| `Ctrl+Alt+Enter` | Replace all (triggers confirmation) | P1 |
| `Ctrl+Shift+1` | Replace current match and move to next | P2 |

---

## 4. Safety Requirements

| Requirement | Description | Priority |
|-------------|-------------|----------|
| **Confirmation dialog** | "Replace X occurrences in Y files?" before Replace All | P0 |
| **Undo support** | Single undo action reverts entire Replace All operation | P0 |
| **Skip read-only** | Skip read-only files with warning message | P0 |
| **Modified indicator** | Show unsaved indicator on affected files | P1 |
| **Stale detection** | Warn if file changed since search was performed | P2 |
| **Auto-save option** | Option to auto-save after replace operations | P3 |

---

## 5. Behavioral Requirements

| Requirement | Description | Priority |
|-------------|-------------|----------|
| **Open file handling** | Apply changes to in-memory document if file is open | P0 |
| **Closed file handling** | Modify file directly on disk | P0 |
| **Error recovery** | If one file fails, continue with others and report errors | P0 |
| **Result updates** | Clear/update results after successful replace | P1 |
| **Progress feedback** | Show "Replacing..." during operation | P1 |
| **Atomic operations** | Replace All in File is atomic (all or nothing) | P2 |

---

## 6. Preview Mode (P3)

When preview mode is enabled:

### 6.1 Inline Preview
- Show before/after text in result excerpt
- Strikethrough for removed text, highlight for added text

### 6.2 Diff Preview
- Full diff view for reviewing changes before applying
- File-by-file preview option

---

## 7. Preserve Case Logic (P3)

When "Preserve Case" is enabled:

| Original | Search | Replace | Result |
|----------|--------|---------|--------|
| `Hello` | `hello` | `world` | `World` |
| `HELLO` | `hello` | `world` | `WORLD` |
| `hello` | `hello` | `world` | `world` |
| `HelloWorld` | `helloworld` | `foobar` | `FooBar` |

---

## 8. State Persistence

| State | Persistence | Priority |
|-------|-------------|----------|
| Last replace term | Session | P2 |
| Preserve case option | Workspace settings | P3 |
| Confirmation preferences | User settings | P3 |

---

## 9. Integration Points

| System | Integration | Priority |
|--------|-------------|----------|
| **Search System** | Extends search panel UI and results | P0 |
| **Undo System** | Replace operations integrate with command undo stack | P0 |
| **Document Editor** | Sync with open documents | P0 |
| **File System** | Direct file modifications for closed files | P0 |

---

## 10. Edge Cases

| Scenario | Behavior | Priority |
|----------|----------|----------|
| Empty replace string | Allow (effectively deletes matches) | P0 |
| No matches | Replace buttons disabled | P0 |
| File locked during replace | Skip with warning, continue others | P0 |
| Replace in open modified file | Apply to in-memory document | P0 |
| Replace in closed file | Modify file directly on disk | P0 |
| File changed since search | Warn user, suggest re-searching | P2 |
| Regex capture groups | Support `$1`, `$2` etc. in replace string | P3 |

---

## Summary

| Category | Priority | Release Target |
|----------|----------|----------------|
| Replace All | P0 | v1 |
| Replace All in File | P0 | v1 |
| Undo support | P0 | v1 |
| Confirmation dialog | P0 | v1 |
| Error handling & recovery | P0 | v1 |
| Keyboard shortcuts | P1 | v1 |
| Progress feedback | P1 | v1 |
| Modified file indicator | P1 | v1 |
| Result updates after replace | P1 | v1 |
| Replace single match | P2 | Future |
| Stale file detection | P2 | Future |
| State persistence | P2 | Future |
| Preview/diff mode | P3 | Future |
| Preserve case | P3 | Future |
| Regex capture groups | P3 | Future |
| Auto-save option | P3 | Future |
