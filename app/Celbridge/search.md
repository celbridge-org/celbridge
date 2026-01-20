# Global Search - Requirements Specification

This document defines the requirements for a global text search system in Celbridge, presented in a Search tab within the Explorer panel.

> **Design Philosophy**: Celbridge is a data workbench for learning Python and data science, not a full IDE. The search feature should be powerful yet simple—optimized for the 90% use case without overwhelming users with options.

---

## 1. Core Features

| Requirement | Description | Priority |
|-------------|-------------|----------|
| **Text Search** | Search for text across all project files | P0 |
| **Case Sensitivity** | Toggle case-sensitive/insensitive matching | P1 |
| **Whole Word** | Match whole words only (not partial matches) | P1 |
| **Regular Expressions** | Support regex pattern matching | P2 |
| **Search Scope** | Limit search to specific folders or open files | P3 |

---

## 2. File Filtering

### 2.1 Default Behavior

| Behavior | Description | Priority |
|----------|-------------|----------|
| **Auto-exclude build artifacts** | Skip `.git`, `bin`, `obj`, `__pycache__`, `.venv`, `node_modules` | P0 |
| **Text files only** | Only search files recognized as text documents | P0 |
| **Skip large files** | Skip files over configurable size limit (default 1MB) | P1 |
| **Binary detection** | Skip files detected as binary | P1 |

### 2.2 User-Configurable Filters

| Filter | Description | Priority |
|--------|-------------|----------|
| **Files to Include** | Glob patterns for files to search (e.g., `*.py`, `src/**/*.json`) | P2 |
| **Files to Exclude** | Glob patterns to exclude (e.g., `*.min.js`, `data/*.csv`) | P2 |
| **Respect .gitignore** | Option to honor `.gitignore` patterns | P3 |

---

## 3. Results Presentation

### 3.1 Results Tree Structure

```
Search: "dataframe" (8 matches in 3 files)
├── 📄 analysis.py (4 matches)
│   ├── 12: df = pd.DataFrame(data)
│   ├── 45: merged_dataframe = ...
│   ├── 78: ...
│   └── 112: ...
├── 📄 utils.py (3 matches)
│   └── ...
└── 📄 README.md (1 match)
    └── ...
```

### 3.2 Result Item Information

| Element | Description | Priority |
|---------|-------------|----------|
| **Summary line** | "X matches in Y files" | P0 |
| **File name** | Name of file containing matches | P0 |
| **Match count** | Number of matches per file | P0 |
| **Line number** | Line number of each match | P0 |
| **Context excerpt** | Text excerpt showing the match | P0 |
| **File icon** | File type icon (consistent with Explorer) | P1 |
| **Match highlighting** | Visual highlight of matched text in excerpt | P1 |
| **File path** | Relative path (for disambiguating same-named files) | P2 |

### 3.3 Result Actions

| Action | Description | Priority |
|--------|-------------|----------|
| **Click to navigate** | Open file and scroll to line | P0 |
| **Collapse/expand file** | Show/hide line matches under a file | P1 |
| **Collapse/expand all** | Collapse or expand all file nodes | P2 |
| **Dismiss result** | Remove a single result from the list | P3 |
| **Copy path** | Copy file path/line to clipboard | P3 |

---

## 4. User Interface

### 4.1 Search Panel Layout

```
┌─────────────────────────────────┐
│ 🔍 [Search..................]   │
│                                 │
│ ☐ Match case  ☐ Whole word      │
│                                 │
│ ▶ Filters (collapsed)           │
├─────────────────────────────────┤
│ 8 matches in 3 files   [Clear]  │
├─────────────────────────────────┤
│ 📄 analysis.py (4)              │
│   12: ...context...             │
│   45: ...context...             │
│ 📄 utils.py (3)                 │
│   8: ...context...              │
└─────────────────────────────────┘
```

### 4.2 Controls

| Control | Type | Default | Priority |
|---------|------|---------|----------|
| Search input | Text box | Empty | P0 |
| Match case | Checkbox | Unchecked | P1 |
| Whole word | Checkbox | Unchecked | P1 |
| Clear results | Button | - | P1 |
| Regex mode | Checkbox/toggle | Unchecked | P2 |
| Filters expander | Collapsible section | Collapsed | P2 |

---

## 5. Keyboard Shortcuts

| Shortcut | Action | Priority |
|----------|--------|----------|
| `Ctrl+Shift+F` | Open Search panel and focus input | P1 |
| `Enter` | Execute search (when in search input) | P0 |
| `Escape` | Clear search / close panel | P1 |
| `F4` / `Shift+F4` | Navigate to next/previous result | P2 |

---

## 6. Behavioral Requirements

### 6.1 Search Execution

| Requirement | Description | Priority |
|-------------|-------------|----------|
| **Debouncing** | 300ms delay after typing before executing search | P0 |
| **Minimum characters** | Require 2+ characters before searching | P0 |
| **Async execution** | Search runs on background thread | P0 |
| **Cancellation** | New search cancels in-progress search | P1 |
| **Progress indicator** | Show "Searching..." during search | P1 |

### 6.2 File Handling

| Requirement | Description | Priority |
|-------------|-------------|----------|
| **Text files only** | Skip binary files automatically | P0 |
| **Graceful errors** | Skip inaccessible/locked files, continue search | P0 |
| **Encoding detection** | Handle UTF-8, UTF-16, common encodings | P1 |
| **Large file warning** | Skip files over size limit with optional warning | P2 |

---

## 7. State Persistence

| State | Persistence | Priority |
|-------|-------------|----------|
| Last search term | Session | P2 |
| Search options (case, whole word) | Workspace settings | P2 |
| Include/exclude patterns | Workspace settings | P3 |
| Results expanded/collapsed | Session | P3 |

---

## 8. Integration Points

| System | Integration | Priority |
|--------|-------------|----------|
| **Explorer Panel** | Search tab alongside Explorer tab | P0 |
| **Document Editor** | Navigate to line when result clicked | P0 |
| **Status Bar** | Show search progress/status | P2 |
| **File Watcher** | Update/invalidate results when files change | P3 |

---

## 9. Edge Cases

| Scenario | Behavior | Priority |
|----------|----------|----------|
| No results found | Display "No results found" message | P0 |
| Search term too short | Display "Enter at least 2 characters" | P0 |
| Invalid regex | Display regex error inline | P2 |
| Very large result set | Cap at 1000 results with "Show more" option | P2 |
| File deleted during search | Skip file, continue search | P1 |

---

## 10. Future Considerations

Features to consider for later releases based on user demand:

- **Search history** - Dropdown of recent searches
- **Saved searches** - Save frequently used search patterns
- **Multi-line search** - Search for patterns spanning multiple lines
- **Search in selection** - Search within selected text only
- **Search indexing** - Background index for instant results in large projects
- **Semantic search** - Symbol-aware search (find references, find usages)

---

## Summary

| Category | Priority | Release Target |
|----------|----------|----------------|
| Basic text search | P0 | v1 |
| Results display & navigation | P0 | v1 |
| Async execution with debouncing | P0 | v1 |
| Case sensitivity toggle | P1 | v1 |
| Whole word toggle | P1 | v1 |
| Progress indicator | P1 | v1 |
| Keyboard shortcut (Ctrl+Shift+F) | P1 | v1 |
| Match highlighting in excerpts | P1 | v1 |
| Regular expressions | P2 | Future |
| Include/exclude filters | P2 | Future |
| Result navigation shortcuts (F4) | P2 | Future |
| State persistence | P2 | Future |
| Search scope options | P3 | Future |
| File watcher integration | P3 | Future |
| Dismiss/copy result actions | P3 | Future |
