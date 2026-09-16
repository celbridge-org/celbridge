# workshop

The `workshop` namespace publishes packages and pages to the connected workshop, installs packages from it, and curates package aliases. A package is a folder with a `package.toml` manifest (see `packages_overview`), and a page is a folder of static web content with a `pages.toml` manifest (see `pages_overview`). The workshop is experimental, and nothing else in Celbridge depends on it. To list the packages a project loaded, use `app_list_packages`, which needs no connection.

## Must-knows

- **Every tool here needs a Workshop connection.** The user adds one in the Workshop section of Settings, with the workshop's URL and a Workshop Key. Without a connection, each tool fails before contacting the workshop, with a message that says where to add one. Ask the user to add the connection rather than retrying.
- **A publish records the Author set in Workshop settings.** `workshop_publish_package` and `workshop_publish_page` fail when no Author is set. A manifest has no author field.
- **Publishing and installing confirm by default.** `workshop_publish_package`, `workshop_install_package`, `workshop_publish_page` and `workshop_unpublish_page` ask the user first. Pass `confirmWithUser: false` only for unattended flows the user has agreed to. See `silent_vs_interactive`.
- **The irreversible package tools always confirm.** `workshop_delete_package` removes one workshop version and `workshop_unpublish_package` removes every one, and neither has a `confirmWithUser` opt-out.
- **A package version is not a workshop version.** The package version is the `package-version` a manifest declares. A workshop version is the number the workshop assigns each publish, and the `workshopVersion` parameters and fields hold one. See `workshop_versions`.
- **Package code cannot call these tools.** The host withholds the whole namespace from package editors, so a package cannot act with the user's Workshop Key. The tools work from Python and the MCP transport.

## Tools

**Packages.**

- `workshop_list_packages` — list the packages on the workshop.
- `workshop_get_package_info` — inspect a package's workshop versions and aliases.
- `workshop_install_package` — install a workshop version or alias into a destination folder.
- `workshop_publish_package` — publish a package folder as a new workshop version.
- `workshop_delete_package` — delete one workshop version permanently.
- `workshop_unpublish_package` — remove a package and every workshop version.

**Aliases.**

- `workshop_set_package_alias` — point an alias at a workshop version.
- `workshop_remove_package_alias` — remove an alias.

**Pages.**

- `workshop_list_pages` — list the pages published to the workshop.
- `workshop_get_page_info` — inspect one published page.
- `workshop_publish_page` — publish a folder as a page, at the path its `pages.toml` names.
- `workshop_unpublish_page` — remove a page's served content.
