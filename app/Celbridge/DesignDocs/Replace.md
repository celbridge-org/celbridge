# Global Replace - Requirements Specification

This document defines the requirements for global text replace functionality in Celbridge, building on the search system defined in `search.md`.

> **Prerequisite**: This feature requires the Global Search system to be implemented first.

> **Design Philosophy**: Celbridge is a data workbench for learning Python and data science, not a full IDE. Replace functionality should be straightforward and safe—helping users refactor variable names or fix typos across files without risk of data loss.

> **Implementation Status**: Not yet implemented. Search foundation is complete (see `search.md`).

---

## 1. Core Features

| Requirement | Description | Status | Priority |
|-------------|-------------|--------|----------|
| **Replace All in File** | Replace all matches in a specific file | ❌ Not Implemented | P0 |
| **Replace All** | Replace all matches across all files | ❌ Not Implemented | P0 |
| **Undo Support** | All replacements undoable | ❌ Not Implemented | P0 |
| **Confirmation Dialog** | Confirm before Replace All operations | ❌ Not Implemented | P0 |
| **Replace Single** | Replace the currently selected match | ❌ Not Implemented | P2 |
| **Preview Changes** | Show before/after preview before applying | ❌ Not Implemented | P3 |
| **Preserve Case** | Match the case pattern of replaced text | ❌ Not Implemented | P3 |

---

## 2. User Interface

### 2.1 Proposed Search Panel Layout (Extended)

```
┌─────────────────────────────────┐
│ SEARCH                          │
├─────────────────────────────────┤
│ [Search..................] 🔍  │
│ [Replace with............] ↻   │
│                        Aa  ab   │
│                                 │
│ ▶ Filters (collapsed)           │
├─────────────────────────────────┤
│ 8 matches in 3 files            │
│ [Replace All]           [Clear] │
├─────────────────────────────────┤
│ 📄 analysis.py (4)     [Replace]│
│   12: ...context...      [↻]   │
│   45: ...context...      [↻]   │
│ 📄 utils.py (3)        [Replace]│
│   8: ...context...       [↻]   │
└─────────────────────────────────┘
```

### 2.2 Proposed Controls

| Control | Location | Function | Status | Priority |
|---------|----------|----------|--------|----------|
| Replace input | Below search input | Text to replace with | ❌ Not Implemented | P0 |
| Replace All | Results header | Replace all matches (with confirmation) | ❌ Not Implemented | P0 |
| Replace (per-file) | File header | Replace all in that file | ❌ Not Implemented | P0 |
| Replace (per-match) | Per result line | Replace single match | ❌ Not Implemented | P2 |
| Dismiss (per-match) | Per result line | Remove result from list | ❌ Not Implemented | P3 |
| Preserve case toggle | Options area | Enable case preservation | ❌ Not Implemented | P3 |

---

## 3. Keyboard Shortcuts

| Shortcut | Action | Status | Priority |
|----------|--------|--------|----------|
| `Ctrl+Shift+H` | Open Search panel with Replace input focused | ❌ Not Implemented | P1 |
| `Ctrl+Alt+Enter` | Replace all (triggers confirmation) | ❌ Not Implemented | P1 |
| `Ctrl+Shift+1` | Replace current match and move to next | ❌ Not Implemented | P2 |

---

## 4. Safety Requirements

| Requirement | Description | Status | Priority |
|-------------|-------------|--------|----------|
| **Confirmation dialog** | "Replace X occurrences in Y files?" before Replace All | ❌ Not Implemented | P0 |
| **Undo support** | Single undo action reverts entire Replace All operation | ❌ Not Implemented | P0 |
| **Skip read-only** | Skip read-only files with warning message | ❌ Not Implemented | P0 |
| **Modified indicator** | Show unsaved indicator on affected files | ❌ Not Implemented | P1 |
| **Stale detection** | Warn if file changed since search was performed | ❌ Not Implemented | P2 |
| **Auto-save option** | Option to auto-save after replace operations | ❌ Not Implemented | P3 |

---

## 5. Behavioral Requirements

| Requirement | Description | Status | Priority |
|-------------|-------------|--------|----------|
| **Open file handling** | Apply changes to in-memory document if file is open | ❌ Not Implemented | P0 |
| **Closed file handling** | Modify file directly on disk | ❌ Not Implemented | P0 |
| **Error recovery** | If one file fails, continue with others and report errors | ❌ Not Implemented | P0 |
| **Result updates** | Clear/update results after successful replace | ❌ Not Implemented | P1 |
| **Progress feedback** | Show "Replacing..." during operation | ❌ Not Implemented | P1 |
| **Atomic operations** | Replace All in File is atomic (all or nothing) | ❌ Not Implemented | P2 |

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

| State | Persistence | Status | Priority |
|-------|-------------|--------|----------|
| Last replace term | Session | ❌ Not Implemented | P2 |
| Preserve case option | Editor settings (global) | ❌ Not Implemented | P3 |
| Confirmation preferences | Editor settings (global) | ❌ Not Implemented | P3 |

**Design Note**: Following the pattern established for search options, replace settings should be stored in Editor Settings for consistency across projects.

---

## 9. Integration Points

| System | Integration | Status | Priority |
|--------|-------------|--------|----------|
| **Search System** | Extends search panel UI and results | ❌ Not Implemented | P0 |
| **Command System** | Replace operations integrate with command undo stack | ❌ Not Implemented | P0 |
| **Document Editor** | Sync with open documents | ❌ Not Implemented | P0 |
| **File System** | Direct file modifications for closed files | ❌ Not Implemented | P0 |

---

## 10. Edge Cases

| Scenario | Behavior | Status | Priority |
|----------|----------|--------|----------|
| Empty replace string | Allow (effectively deletes matches) | ❌ Not Implemented | P0 |
| No matches | Replace buttons disabled | ❌ Not Implemented | P0 |
| File locked during replace | Skip with warning, continue others | ❌ Not Implemented | P0 |
| Replace in open modified file | Apply to in-memory document | ❌ Not Implemented | P0 |
| Replace in closed file | Modify file directly on disk | ❌ Not Implemented | P0 |
| File changed since search | Warn user, suggest re-searching | ❌ Not Implemented | P2 |
| Regex capture groups | Support `$1`, `$2` etc. in replace string | ❌ Not Implemented | P3 |

---

## 11. Implementation Notes

### 11.1 Recommended Architecture

Building on the existing search infrastructure:

```csharp
// New service interface
public interface IReplaceService
{
    Task<Result> ReplaceAllAsync(
        SearchResults searchResults,
        string replaceWith,
        CancellationToken cancellationToken);
        
    Task<Result> ReplaceInFileAsync(
        SearchFileResult fileResult,
        string replaceWith,
        CancellationToken cancellationToken);
        
    Task<Result> ReplaceSingleAsync(
        SearchMatchLine match,
        ResourceKey resource,
        string replaceWith,
        CancellationToken cancellationToken);
}
```

### 11.2 Command Integration

Replace operations should be implemented as commands for undo/redo support:

```csharp
public interface IReplaceAllCommand : IExecutableCommand
{
    SearchResults SearchResults { get; set; }
    string ReplaceWith { get; set; }
}

public interface IReplaceInFileCommand : IExecutableCommand
{
    SearchFileResult FileResult { get; set; }
    string ReplaceWith { get; set; }
}
```

### 11.3 Document Coordination

- **Open Documents**: Modify via `IDocumentsService` to update Monaco editor content
- **Closed Documents**: Direct file system modification via `File.WriteAllText`
- **Validation**: Re-run search after replace to verify changes

---

## 12. UI/UX Considerations

### 12.1 Confirmation Dialog Design

```
┌─────────────────────────────────────────┐
│  Replace All                            │
├─────────────────────────────────────────┤
│                                         │
│  Replace 47 occurrences in 8 files?    │
│                                         │
│  Find:    "dataframe"                   │
│  Replace: "df"                          │
│                                         │
│  ☐ Show preview before replacing       │
│                                         │
│         [Cancel]    [Replace All]       │
└─────────────────────────────────────────┘
```

### 12.2 Progress Indication

- Progress dialog for Replace All operations affecting many files
- Toast notification on completion: "Replaced 47 occurrences in 8 files"
- Option to undo immediately from toast

---

## Summary

| Category | Status | Notes |
|----------|--------|-------|
| **Replace All** | ❌ Not Started | Requires command system integration |
| **Replace All in File** | ❌ Not Started | Simpler scope, good starting point |
| **Undo support** | ❌ Not Started | Critical for safety |
| **Confirmation dialog** | ❌ Not Started | Required for v1 |
| **Error handling & recovery** | ❌ Not Started | Essential for robustness |
| **Keyboard shortcuts** | ❌ Not Started | Nice to have for v1 |
| **Progress feedback** | ❌ Not Started | Important for UX |
| **Modified file indicator** | ❌ Not Started | Helps prevent data loss |
| **Result updates after replace** | ❌ Not Started | Good UX practice |
| **Replace single match** | ❌ Not Started | Future enhancement |
| **Stale file detection** | ❌ Not Started | Future enhancement |
| **State persistence** | ❌ Not Started | Future enhancement |
| **Preview/diff mode** | ❌ Not Started | Future enhancement |
| **Preserve case** | ❌ Not Started | Future enhancement |
| **Regex capture groups** | ❌ Not Started | Requires regex search first |
| **Auto-save option** | ❌ Not Started | Nice to have |

**Development Recommendation**: 
1. **Phase 1**: Implement basic Replace All with confirmation dialog and undo support (P0 features)
2. **Phase 2**: Add Replace in File and keyboard shortcuts (P0-P1 features)
3. **Phase 3**: Add single-match replace and progress indicators (P1-P2 features)
4. **Phase 4**: Consider preview mode and preserve case (P3 features)

**Prerequisite Work Required**:
- ✅ Search system is complete and functional
- ❌ Command system undo/redo for replace operations
- ❌ Coordination with document editor for open files
- ❌ Confirmation dialog component
