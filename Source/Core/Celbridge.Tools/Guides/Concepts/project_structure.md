# Project structure

A Celbridge project is a folder on disk containing a `.celbridge` project file plus user content. The project file's parent folder is the **content root**: every resource key is relative to it. A folder contains at most one project file, and Celbridge refuses to open or create a project in a folder that contains another. Celbridge keeps its own state for the project outside the project folder, in a data folder that belongs to the project folder, so renaming the project file keeps that state. Tools reach it only through the `temp:`, `logs:` and `utils:` roots (see `resource_keys`).

A typical layout:

```
my-project/
  my-project.celbridge      project file
  readme.md                 user content (top level)
  Scripts/                  user folder
    hello.py
  Data/
    sales.xlsx
  packages/                 (optional) the package folder, one package per subfolder
    counter-widget/
      package.toml
      index.html
```

## Where tools operate

- **`file_*`** — read, write, search, and inspect any file or folder under the content root.
- **`explorer_*`** — create, move, rename, delete project resources (drives the same operations a user can perform in the explorer panel).
- **`document_*`** — open files in the editor area as tabs and inspect tab state.
- **`spreadsheet_*`** — target `.xlsx` workbooks; bypass the editor when reading and modifying cell data.
- **`webview_*`** — drive WebView devtools against an open contribution editor document, or the page the HTML editor previews.
- **`workshop_*`** — publish packages and pages to a workshop, and install packages from one.

## The `.celbridge` project file

The project file stores project-level configuration as TOML. Host-level declarations are keys on the `[celbridge]` table and its `[celbridge.explorer]`, `[celbridge.search]`, `[celbridge.downloads]` and `[celbridge.packages]` sub-tables. `[[contribution]]` entries record how the project deviates from each editor's discovered defaults. All changes apply on project reload.

```toml
[celbridge]
celbridge-version = "1.3.0"
project-version   = "0.1.0"
editor-associations = { ".png" = "acme.pixel-editor.pixel" }   # pin an extension to one editor

[celbridge.explorer]                                           # ergonomics only; neither list gates access
exclude = [".gitignore", "__pycache__"]                        # not drawn in the Explorer tree

[celbridge.search]
exclude = ["node_modules", "bin", "obj"]                       # skipped by search and tag queries

[celbridge.downloads]
folder = "assets/incoming"                                     # where files downloaded from web pages land

[celbridge.packages]
folders = ["vendor/packages"]                                  # package folders in addition to packages/
disabled = ["acme.unused"]                                     # opt a discovered package out

[[contribution]]                                               # an override of an editor's defaults
package      = "acme.pixel-editor"
contribution = "pixel"
grid-size    = 16                                              # config keys, checked against the editor's descriptors

[[shortcut]]                                                   # a file opened from a Utility Rail button, on load, or both
resource     = "readme.md"
icon         = "bs-book"                                       # optional; defaults to a pin icon
area         = "bottom"                                        # optional; main, bottom or side, default main
open-on-load = true                                            # optional; default false
```

- **Package folders**: a package is a direct subfolder of a package folder, with a `package.toml` at its root. `packages/` is always a package folder, and `folders` lists more, each as a folder path from the project root. Discovery lists each package folder and looks nowhere else. A manifest anywhere else is an ordinary file, including one nested inside a package. A listed folder is refused when it is the project root, a folder inside `.git`, a package, or a folder inside a package. The load report names each refused folder. A missing listed folder adds no packages. `workshop_install_package` installs only into a package folder.
- **Activation is opt-out**: discovery reads the bundled modules and the package folders, and every discovered package is active by default. Listing a package in `disabled` turns the package and all its contributions off. (The exception is a contribution its manifest marks `optional`, which stays inert until a `[[contribution]]` entry enables it.) The built-in editors (code editor, Markdown, HTML, File Viewer, spreadsheet, Report Viewer) are always active and cannot be disabled.
- **Contributions**: a `[[contribution]]` entry names an editor by `package` and `contribution` and records this project's overrides of its defaults. Any non-reserved key is configuration, type-checked against the editor's descriptors; a default-active editor running with default config needs no entry. `disabled = true` turns off a contribution its package marked *recommended*; `enabled = true` turns on one marked *optional*. There is exactly one editor per contribution — a project cannot declare several, nor override an editor's title, icon, or tooltip.
- **Document shortcuts**: a `[[shortcut]]` entry opens `resource` as a document, in whichever editor that file's extension resolves to. It adds a button to the Utility Rail unless `rail-button = false` (the Show Utility button toggle in Project Settings), and `open-on-load = true` (Open when project loads) also opens the document every time the project loads, even if it was closed in the last session. Entry order is rail order, and the buttons sit with the utilities the project's packages contribute, ahead of the gap that pins the built-in Project Settings and Community buttons at the end of the rail. `area` (`main`, `bottom` or `side`, default `main`) picks where the document lands, and applies only while it is not already open, so a document restored from the last session keeps its place. An entry whose `resource` is not a resource key is skipped.
- **Editor resolution**: per-file sidecar override, then `editor-associations` (longest matching extension suffix), then the first supporting contribution in discovery order, then the built-in editors in their pinned order. Editors are referenced as `package.contribution` (e.g. `acme.pixel-editor.pixel`).
- **Reserved file types**: Celbridge's own formats (the `.celbridge` project file, `package.toml`, `*.editor.toml`) carry a role the application depends on, so they are not the user's to reassign. A reserved type resolves to its own editor ahead of anything claiming the same extension, an `editor-associations` entry naming one is reported and ignored, and choosing another editor from Open With opens the file for a look without recording a sidecar. They carry no `.cel` sidecar at all, so they take no tags either. The File Types page leaves them out.
- **Lists**: the Explorer's list, `[celbridge.explorer] exclude`, and the search list, `[celbridge.search] exclude`, are ergonomic settings, not access control. The Explorer's list names what the Explorer leaves out of its tree. The search list names what search and tag queries skip. The Search panel's Skip excluded files checkbox and the tools' `applySearchExclude` flag switch the search list off for a search. A search in an excluded folder searches the whole folder. Each list affects only what its name says. A folder on the search list still shows in the Explorer. The Explorer also leaves out `.cel` sidecar files. The Show Hidden Files toolbar button shows what the Explorer leaves out, dimmed. Revealing a hidden resource in the Explorer turns Show Hidden Files on. A resource matched by either list stays readable and writable by every tool. Both lists follow `.gitignore`. A pattern without a slash matches at any depth, and a leading `/` makes a pattern relative to the project folder. A trailing `/` matches folders only. A pattern that matches a folder takes everything beneath it. A line that starts with `!` puts a matched path back in, and a line that starts with `#` is a comment. Letter case is exact. `?` and a class match one character, even a character outside ASCII. Git matches one byte of UTF-8 instead, so in git `caf?.txt` misses `café.txt`. Some lines read differently in `.gitignore` from what their writer meant, like `src\bin` or `*.{js,ts}`. Loading the project reports and drops each such line. A missing table or key is an empty list. New projects start with the Explorer's list `[".gitignore", ".DS_Store", "Thumbs.db", "desktop.ini", "__pycache__"]`, so macOS and Windows system files stay out of the tree wherever they appear. New projects also start with the search list `["node_modules", "bin", "obj", "dist", ".venv", "__pycache__"]`. These are ordinary entries in the project file, not built-in rules. Removing an entry from the Explorer's list brings those files back into the tree. Removing an entry from the search list brings them back into search. The `.git` folder is reserved, so every read, write and listing of it is refused, whatever the configuration says. Celbridge never follows a symbolic link or junction. Links are left out of every listing, and a key that is a link or passes through one is refused.
- **Downloads**: `[celbridge.downloads] folder` is a path from the project root. Files downloaded from web pages are saved there. Without the key, downloads go to `downloads`. The first download creates that folder, and new projects' `.gitignore` leaves it out of git.
- A malformed entry is skipped with a console banner and the rest of the file applies; a TOML syntax error fails loudly and the project opens with nothing active.

Agents may edit the file with the ordinary file tools; changes take effect when the project reloads.
