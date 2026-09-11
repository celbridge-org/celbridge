# package

The `package` namespace covers Celbridge packages — the unit of distributable functionality (a custom document editor, an asset library, a reusable Python module). A package is a folder with a `package.toml` manifest at its root, discovered on project load.

## Must-knows

- **`package_status` is the installed-package map.** It reports each project package's name, version, and folder, plus any load failures such as a duplicate-name fault. Use it to decide where to put a package and to diagnose why one is not loading.
- **Only packages under `project:` load.** A package folder copied to a non-loading root such as `temp:` is inert reference data, useful for comparison and merge workflows.
- **Packages are not Python packages.** Despite some tooling overlap, this namespace is for Celbridge's own package format. Python dependencies are declared per console, in a `.console` file's `[session.python].dependencies` array.
- **There is no create tool.** A package is a folder with a `package.toml` manifest; scaffold one by writing the manifest with the file tools. See `packages_overview` for the manifest shape.
- **`package_archive` and `package_unarchive` are general-purpose zip tools.** They are named for this namespace but have nothing to do with the package format, and work on any folder or archive in the project tree.

## Tools

- `package_status` — report the project's installed packages (name, version, folder) and any load failures.
- `package_archive` — archive a folder into a zip file.
- `package_unarchive` — extract a zip archive into a folder.

Some builds expose further tools in this namespace. Those carry their own guide, which attaches on first use.
