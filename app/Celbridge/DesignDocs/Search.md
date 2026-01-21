# Global Search - Requirements Specification

This document defines the requirements for a global text search system in Celbridge, presented in a Search tab within the Explorer panel.

> **Design Philosophy**: Celbridge is a data workbench for learning Python and data science, not a full IDE. The search feature should be powerful yet simple—optimized for the 90% use case without overwhelming users with options.

> **Implementation Status**: v1 features are implemented and functional.

---

## 1. Core Features

| Requirement | Description | Status | Priority |
|-------------|-------------|--------|----------|
| **Text Search** | Search for text across all project files | ✅ Implemented | P0 |
| **Case Sensitivity** | Toggle case-sensitive/insensitive matching | ✅ Implemented | P1 |
| **Whole Word** | Match whole words only (not partial matches) | ✅ Implemented | P1 |
| **Regular Expressions** | Support regex pattern matching | ❌ Not Implemented | P2 |
| **Search Scope** | Limit search to specific folders or open files | ❌ Not Implemented | P3 |

---

## 2. File Filtering

### 2.1 Default Behavior

| Behavior | Description | Status | Priority |
|----------|-------------|--------|----------|
| **Auto-exclude metadata** | Skip `.webapp`, `.celbridge` files | ✅ Implemented | P0 |
| **Auto-exclude binary** | Skip common binary extensions (exe, dll, images, archives, etc.) | ✅ Implemented | P0 |
| **Text files only** | Only search files recognized as text documents | ✅ Implemented | P0 |
| **Skip large files** | Skip files over 1MB | ✅ Implemented | P1 |
| **Binary detection** | Skip files containing null characters | ✅ Implemented | P1 |

### 2.2 User-Configurable Filters

| Filter | Description | Status | Priority |
|--------|-------------|--------|----------|
| **Files to Include** | Glob patterns for files to search (e.g., `*.py`, `src/**/*.json`) | ❌ Not Implemented | P2 |
| **Files to Exclude** | Glob patterns to exclude (e.g., `*.min.js`, `data/*.csv`) | ❌ Not Implemented | P2 |
| **Respect .gitignore** | Option to honor `.gitignore` patterns | ❌ Not Implemented | P3 |

---

## 3. Results Presentation

### 3.1 Results Tree Structure

**Implemented Structure:**
```
Search: "dataframe" (8 matches in 3 files)
├── 📄 analysis.py (4)
│   ├── 12: df = pd.DataFrame(data)
│   ├── 45: merged_dataframe = ...
│   ├── 78: ...
│   └── 112: ...
├── 📄 utils.py (3)
│   └── ...
└── 📄 README.md (1)
    └── ...
```

### 3.2 Result Item Information

| Element | Description | Status | Priority |
|---------|-------------|--------|----------|
| **Summary line** | "X matches in Y files" | ✅ Implemented | P0 |
| **File name** | Name of file containing matches | ✅ Implemented | P0 |
| **Match count** | Number of matches per file (badge) | ✅ Implemented | P0 |
| **Line number** | Line number of each match | ✅ Implemented | P0 |
| **Context excerpt** | Text excerpt showing the match | ✅ Implemented | P0 |
| **File icon** | File type icon (consistent with Explorer) | ✅ Implemented | P1 |
| **Match highlighting** | Visual highlight of matched text in excerpt | ✅ Implemented | P1 |
| **File path** | Relative path (shown in tooltip) | ✅ Implemented | P2 |

### 3.3 Result Actions

| Action | Description | Status | Priority |
|--------|-------------|--------|----------|
| **Click to navigate** | Open file and scroll to line | ✅ Implemented | P0 |
| **Collapse/expand file** | Show/hide line matches under a file | ✅ Implemented | P1 |
| **Collapse/expand all** | Collapse or expand all file nodes | ❌ Not Implemented | P2 |
| **Dismiss result** | Remove a single result from the list | ❌ Not Implemented | P3 |
| **Copy path** | Copy file path/line to clipboard | ❌ Not Implemented | P3 |

---

## 4. User Interface

### 4.1 Current Search Panel Layout

```
┌─────────────────────────────────┐
│ SEARCH                          │
├─────────────────────────────────┤
│ [Search..................] 🔍  │
│                        Aa  ab   │  (Match case, Whole word toggles)
├─────────────────────────────────┤
│ 8 matches in 3 files            │
├─────────────────────────────────┤
│ 📄 analysis.py (4)              │
│   12: ...context...             │
│   45: ...context...             │
│ 📄 utils.py (3)                 │
│   8: ...context...              │
└─────────────────────────────────┘
```

### 4.2 Controls

| Control | Type | Implementation Notes | Status | Priority |
|---------|------|---------------------|--------|----------|
| Search input | Text box | Auto-triggers search with debouncing | ✅ Implemented | P0 |
| Match case | Toggle button | Icon: Aa (U+E8E9) | ✅ Implemented | P1 |
| Whole word | Toggle button | Text: "ab" | ✅ Implemented | P1 |
| Clear results | Button (Esc key) | Clears search text and results | ✅ Implemented | P1 |
| Search button | Button | Manual search trigger | ✅ Implemented | P1 |
| Progress indicator | ProgressRing | Shows during search | ✅ Implemented | P1 |
| Regex mode | Checkbox/toggle | Not yet implemented | ❌ Not Implemented | P2 |
| Filters expander | Collapsible section | Not yet implemented | ❌ Not Implemented | P2 |

---

## 5. Keyboard Shortcuts

| Shortcut | Action | Status | Priority |
|----------|--------|--------|----------|
| `Ctrl+Shift+F` | Open Search panel and focus input | ❌ Not Implemented | P1 |
| `Enter` | Execute search (when in search input) | ⚠️ Auto-search (debounced) | P0 |
| `Escape` | Clear search / close panel | ✅ Implemented (clear only) | P1 |
| `F4` / `Shift+F4` | Navigate to next/previous result | ❌ Not Implemented | P2 |

---

## 6. Behavioral Requirements

### 6.1 Search Execution

| Requirement | Description | Status | Priority |
|-------------|-------------|--------|----------|
| **Debouncing** | 300ms delay after typing before executing search | ✅ Implemented | P0 |
| **Minimum characters** | No minimum enforced - searches any length | ⚠️ Partial | P0 |
| **Async execution** | Search runs on background thread (`Task.Run`) | ✅ Implemented | P0 |
| **Cancellation** | New search cancels in-progress search | ✅ Implemented | P1 |
| **Progress indicator** | Shows ProgressRing during search | ✅ Implemented | P1 |
| **Max results** | Caps at 1000 matches | ✅ Implemented | P2 |

### 6.2 File Handling

| Requirement | Description | Status | Priority |
|-------------|-------------|--------|----------|
| **Text files only** | Skip binary files automatically | ✅ Implemented | P0 |
| **Graceful errors** | Skip inaccessible/locked files, continue search | ✅ Implemented | P0 |
| **Encoding detection** | Handles UTF-8 (default `File.ReadAllText`) | ⚠️ Basic support | P1 |
| **Large file warning** | Skips files over 1MB silently | ✅ Implemented | P2 |

---

## 7. State Persistence

| State | Persistence | Implementation | Status | Priority |
|-------|-------------|----------------|--------|----------|
| Search options (case, whole word) | Editor settings (global) | Stored in `IEditorSettings` | ✅ Implemented | P2 |
| Last search term | Session | Not persisted | ❌ Not Implemented | P2 |
| Include/exclude patterns | Workspace settings | Not implemented | ❌ Not Implemented | P3 |
| Results expanded/collapsed | Session | Not persisted | ❌ Not Implemented | P3 |

**Design Decision**: Search options (Match Case, Whole Word) are stored in **Editor Settings** rather than Workspace Settings. This provides a consistent search experience across all projects, matching industry-standard IDE behavior (VS Code, Visual Studio, etc.).

---

## 8. Integration Points

| System | Integration | Status | Priority |
|--------|-------------|--------|----------|
| **Explorer Panel** | Search tab alongside Explorer tab | ✅ Implemented | P0 |
| **Document Editor** | Navigate to line when result clicked | ✅ Implemented | P0 |
| **Resource Registry** | Uses registry for file enumeration | ✅ Implemented | P0 |
| **File Icons** | Uses `IFileIconService` for file type icons | ✅ Implemented | P1 |
| **Status Bar** | Show search progress/status | ❌ Not Implemented | P2 |
| **File Watcher** | Update/invalidate results when files change | ❌ Not Implemented | P3 |

---

## 9. Edge Cases

| Scenario | Behavior | Status | Priority |
|----------|----------|--------|----------|
| No results found | Display "No results found" message | ✅ Implemented | P0 |
| Search term too short | No minimum enforced | ⚠️ Could improve UX | P0 |
| Invalid regex | N/A (regex not yet implemented) | ❌ N/A | P2 |
| Very large result set | Caps at 1000 results, sets flag | ✅ Implemented | P2 |
| File deleted during search | Skipped gracefully | ✅ Implemented | P1 |
| Binary file content | Detects null characters, skips file | ✅ Implemented | P1 |

---

## 10. Architecture & Implementation Details

### 10.1 Service Architecture

- **`SearchService`**: Core search logic, file filtering, text matching
  - **`FileFilter`**: Determines which files to search (extensions, size limits)
  - **`TextMatcher`**: Performs line-by-line text matching (case-sensitive, whole-word)
  - **`SearchResultFormatter`**: Formats context lines for display

### 10.2 Data Models

```csharp
public record SearchResults(
    string SearchTerm,
    List<SearchFileResult> FileResults,
    int TotalMatches,
    int TotalFiles,
    bool WasCancelled,
    bool ReachedMaxResults);

public record SearchFileResult(
    ResourceKey Resource,
    string FileName,
    string RelativePath,
    List<SearchMatchLine> Matches);

public record SearchMatchLine(
    int LineNumber,
    string LineText,
    int MatchStart,
    int MatchLength,
    int OriginalMatchStart);
```

### 10.3 Settings Integration

```csharp
// IEditorSettings.cs
public interface IEditorSettings
{
    bool SearchMatchCase { get; set; }
    bool SearchWholeWord { get; set; }
}
```

---

## 11. Future Considerations

Features to consider for later releases based on user demand:

- **Search history** - Dropdown of recent searches (P2)
- **Saved searches** - Save frequently used search patterns (P3)
- **Multi-line search** - Search for patterns spanning multiple lines (P3)
- **Search in selection** - Search within selected text only (P3)
- **Search indexing** - Background index for instant results in large projects (P3)
- **Semantic search** - Symbol-aware search (find references, find usages) (P3)
- **Keyboard shortcut (Ctrl+Shift+F)** - Open search panel with shortcut (P1)
- **Minimum character requirement** - Require 2-3 characters before searching (P1)

---

## Summary

| Category | Status | Notes |
|----------|--------|-------|
| **Basic text search** | ✅ Complete | Fully functional with debouncing |
| **Results display & navigation** | ✅ Complete | File tree with expand/collapse, click-to-navigate |
| **Async execution** | ✅ Complete | Cancellable background search |
| **Match Case toggle** | ✅ Complete | Persisted in Editor Settings |
| **Whole Word toggle** | ✅ Complete | Persisted in Editor Settings |
| **Progress indicator** | ✅ Complete | ProgressRing overlay on search box |
| **File filtering** | ✅ Complete | Auto-excludes binaries, metadata, large files |
| **Match highlighting** | ✅ Complete | Highlighted matched text in results |
| **Keyboard shortcuts** | ⚠️ Partial | Escape clears; Ctrl+Shift+F not implemented |
| **Regular expressions** | ❌ Not Started | Future feature |
| **Include/exclude filters** | ❌ Not Started | Future feature |
| **State persistence** | ✅ Partial | Options persisted; search term not persisted |

**Release Status**: v1 core features are complete and functional. The search system provides a solid foundation for text search across project files with all essential P0 and P1 features implemented.
