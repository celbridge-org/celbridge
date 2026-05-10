# Project structure

A Celbridge project is a folder on disk containing a `.celbridge` project file plus user content. The project file's parent folder is the **content root**: every resource key is relative to it.

A typical layout:

```
my-project/
  my-project.celbridge      project file
  readme.md                 user content (top level)
  Scripts/                  user folder
    hello.py
  Data/
    sales.xlsx
  packages/                 (optional) user-authored packages
    counter-widget/
      package.toml
      index.html
```

## Where tools operate

- **`file_*`** — read, write, search, and inspect any file or folder under the content root.
- **`explorer_*`** — create, move, rename, delete project resources (drives the same operations a user can perform in the explorer panel).
- **`document_*`** — open files in the editor area as tabs and inspect tab state.
- **`spreadsheet_*`** — target `.xlsx` workbooks; bypass the editor when reading and modifying cell data.
- **`webview_*`** — drive WebView devtools against an open contribution editor or HTML viewer document.
- **`package_*`** — scaffold, publish, and install packages from the registry.

The `.celbridge` file itself is not a resource agents normally edit; it stores project-level configuration and is managed by the application.
