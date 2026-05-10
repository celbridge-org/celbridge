# package

The `package` namespace builds, installs, archives, and publishes Celbridge packages. A package is the unit of distributable functionality (a custom document editor, an asset library, a reusable Python module). Each package follows a folder layout with a manifest at the root.

## Must-knows

- **Publishing and installing are interactive by default.** `package_publish` and `package_install` confirm with the user before mutating the registry or the project. Pass `confirmWithUser: false` only for unattended flows the user has consented to. See `silent_vs_interactive`.
- **`package_install` requires a loaded project.** Installing without a project loaded fails fast.
- **Archives are produced under the project's content folder.** `package_archive` writes a `.celpkg` next to the source folder unless an explicit destination is given. `package_unarchive` is the inverse.
- **Packages are not Python packages.** Despite some tooling overlap, this namespace is for Celbridge's own package format. To see the project's Python dependencies, read the `.celbridge` project file (`[project].dependencies`).

## Tools

- `package_create` — scaffold a new package from a template at a chosen folder.
- `package_list` — list installed packages in the current project.
- `package_install` — install a package from a `.celpkg` archive into the current project.
- `package_archive` — archive a package folder into a `.celpkg` file.
- `package_unarchive` — extract a `.celpkg` archive into a folder.
- `package_publish` — publish a package to a configured remote registry. Interactive by default.
