# Project structure

A Celbridge project is a folder on disk. The folder contains a project file (extension `.celbridge`) plus any number of user content files and subfolders. The project file's parent folder is the **content root**: every resource key is relative to it.

A typical project layout might look like:

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

- **`file_*` tools** read, write, search, and inspect any file or folder under the content root. Use `file_get_tree("")` to list the top level.
- **`explorer_*` tools** create, move, rename, and delete project resources. They drive the same operations a user can perform in the explorer panel.
- **`document_*` tools** open files in the editor area as tabs and inspect tab state.
- **`spreadsheet_*` tools** target `.xlsx` workbooks specifically and bypass the editor when reading and modifying cell data.
- **`webview_*` tools** drive WebView devtools against an open contribution editor or HTML viewer document.
- **`package_*` tools** scaffold, publish, and install packages from the registry.

The project file itself (the `.celbridge` file) is not a resource agents normally edit; it stores project-level configuration and is managed by the application.
