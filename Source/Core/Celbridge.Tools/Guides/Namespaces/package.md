# package

The `package` namespace holds the workshop tools for Celbridge packages: they publish packages to a workshop and install them from one. A package is a folder with a `package.toml` manifest at its root, discovered on project load. `app_list_packages` reports the project's packages, and `explorer_archive` and `explorer_unarchive` zip and extract folders.

## Must-knows

- **Every tool here needs a Workshop connection, and package code cannot call them.** The `workshop` guide covers the connection, the Author a publish records, and the confirmation rules.
- **Only packages under `project:` load.** A package folder installed to a non-loading root such as `temp:` is inert reference data, useful for comparison and merge workflows.
- **Packages are not Python packages.** Despite some tooling overlap, this namespace is for Celbridge's own package format. Python dependencies are declared per console, in a `.console` file's `[session.python].dependencies` array.
- **There is no create tool.** A package is a folder with a `package.toml` manifest. Scaffold one by writing the manifest with the file tools. See `packages_overview` for the manifest shape.

## Tools

- `package_list` — list the packages available in the workshop.
- `package_info` — inspect a package's versions and aliases.
- `package_install` — download a version or alias into a destination folder.
- `package_publish` — publish a package folder as a new version.
- `package_set_alias`, `package_remove_alias` — point an alias at a version, or remove it.
- `package_delete` — delete one version permanently.
- `package_unpublish` — remove a package and every version.
