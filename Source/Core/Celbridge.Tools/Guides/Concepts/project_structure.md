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

The project file stores project-level configuration as TOML. Host-level declarations are flat keys on the single `[celbridge]` table; `[[contribution]]` entries record how the project deviates from each editor's discovered defaults. All changes apply on project reload.

```toml
[celbridge]
celbridge-version = "0.4.0"
project-version   = "0.1.0"
disabled-packages = ["acme.unused"]                            # opt a discovered package out
editor-associations = { ".png" = "acme.pixel-editor.pixel" }   # pin an extension to one editor

[celbridge.resources]                                          # file policy (ignore-file, add, remove, lock)
ignore-file = ".gitignore"

[[contribution]]                                               # an override of an editor's defaults
package      = "acme.pixel-editor"
contribution = "pixel"
grid-size    = 16                                              # config keys, checked against the editor's descriptors
```

- **Activation is opt-out**: discovery scans bundled modules and the project tree, and every discovered package is active by default. Listing a package in `disabled-packages` turns it and all its contributions off. (The exception is a contribution its manifest marks `optional`, which stays inert until a `[[contribution]]` entry enables it.) The built-in editors (code editor, Markdown, File Viewer, spreadsheet) are always active and cannot be disabled.
- **Contributions**: a `[[contribution]]` entry names an editor by `package` and `contribution` and records this project's overrides of its defaults. Any non-reserved key is configuration, type-checked against the editor's descriptors; a default-active editor running with default config needs no entry. `disabled = true` turns off a contribution its package marked *recommended*; `enabled = true` turns on one marked *optional*. There is exactly one editor per contribution — a project cannot declare several, nor override an editor's title, icon, or tooltip.
- **Editor resolution**: per-file sidecar override, then `editor-associations` (longest matching extension suffix), then the first supporting contribution in discovery order, then the built-in editors in their pinned order. Editors are referenced as `package.contribution` (e.g. `acme.pixel-editor.pixel`).
- **Reserved file types**: Celbridge's own formats (the `.celbridge` project file, `package.toml`, `*.editor.toml`) carry a role the application depends on, so they are not the user's to reassign. A reserved type resolves to its own editor ahead of anything claiming the same extension, an `editor-associations` entry naming one is reported and ignored, and choosing another editor from Open With opens the file for a look without recording a sidecar. They carry no `.cel` sidecar at all, so they take no tags either, and they stay visible and writable however restrictive the `[resources]` rules are. The File Types page leaves them out.
- **Project data folder**: `data-folder` names a folder under `.celbridge/` that this project's local data is written under. It is a single folder name, not a path. Unset, the usual case, that data sits at the root of `.celbridge/`; set, it moves under the named folder, which is how two `.celbridge` files in one project folder avoid sharing state. The `temp:`, `logs:` and `utils:` roots follow it.
- A malformed entry is skipped with a console banner and the rest of the file applies; a TOML syntax error fails loudly and the project opens with nothing active.

Agents may edit the file with the ordinary file tools; changes take effect when the project reloads.
