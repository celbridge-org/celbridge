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

## The `.celbridge` project file

The project file stores project-level configuration as TOML. Host-level declarations are flat keys on the single `[celbridge]` table; every other top-level table declares an editor instance. All changes apply on project reload.

```toml
[celbridge]
celbridge-version = "0.4.0"
project-version   = "0.1.0"
packages = ["acme.pixel-editor"]                 # activation: unlisted packages are inert
editor-associations = { ".png" = "pixel-art" }   # associate an extension with a specific editor

[celbridge.resources]                            # file policy (ignore-file, add, remove, lock)
ignore-file = ".gitignore"

[pixel-art]                                      # an editor instance; the table name is its id
package      = "acme.pixel-editor"
contribution = "pixel"
grid-size    = 16                                # config keys, checked against the editor's descriptors
```

- **Activation**: discovery scans bundled modules and the project tree, but a discovered package that is not listed in `packages` registers nothing. The built-in packages (code editor, Markdown, File Viewer) are always active and need no entry.
- **Instances**: `package` and `contribution` identify the editor; its declared type (`document` or `utility`) determines what the instance is. Optional `title`, `icon`, and `tooltip` are literal display overrides. Remaining keys are the instance's configuration. Declaration order is significant: rail order for utility instances, editor precedence for document instances.
- **Editor resolution**: per-file sidecar override, then `editor-associations` (longest matching extension suffix), then the first supporting instance in declaration order, then the built-in editors in host order.
- **Interim sections**: `[project]` carries the Python keys (`requires-python`, `dependencies`) and `[[shortcut]]` declares title-bar shortcut buttons, until both move to console instance config.
- A malformed entry is skipped with a console banner and the rest of the file applies; a TOML syntax error fails loudly and the project opens with nothing active.

Agents may edit the file with the ordinary file tools; changes take effect when the project reloads.
