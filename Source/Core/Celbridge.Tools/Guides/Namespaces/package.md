# package

The `package` namespace installs, inspects, publishes, archives, and curates Celbridge packages. A package is the unit of distributable functionality (a custom document editor, an asset library, a reusable Python module). Each package follows a folder layout with a `package.toml` manifest at the root.

## Must-knows

- **Publishing and installing are interactive by default.** `package_publish` and `package_install` confirm with the user before mutating the workshop or the project. Pass `confirmWithUser: false` only for unattended flows the user has consented to. See `silent_vs_interactive`.
- **`package_install` requires a loaded project.** Installing without a project loaded fails fast.
- **Install anywhere, but only `project:` loads.** A package installs into a `{packageName}` subfolder of the destination you choose (default `packages/`). Copies installed to non-loading roots such as `temp:` are inert reference data for comparison and merge workflows.
- **The package name comes from the manifest.** `package_publish` reads the name from `[package].name`; there is no folder-name rule and no separate name argument.
- **Aliases are non-destructive.** `package_set_alias` and `package_remove_alias` only repoint or detach a label; they never touch version content. Destructive administration (tombstone, delete) is deliberately not exposed.
- **Packages are not Python packages.** Despite some tooling overlap, this namespace is for Celbridge's own package format. To see the project's Python dependencies, read the `.celbridge` project file (`[project].dependencies`).
- **There is no create tool.** A package is a folder with a `package.toml` manifest; scaffold one by writing the manifest with the file tools. See `packages_overview` for the manifest schema.

## Tools

- `package_list` — list the packages published to the connected workshop.
- `package_info` — inspect one package's versions and aliases.
- `package_install` — download and extract a version (or alias) into a destination folder. Interactive by default.
- `package_publish` — publish a new version from a package folder; name read from the manifest. Interactive by default.
- `package_set_alias` — create or move an alias (e.g. `stable`) to a version.
- `package_remove_alias` — remove an alias; the version it pointed at is unaffected.
- `package_archive` — archive a folder into a zip file.
- `package_unarchive` — extract a zip archive into a folder.
