# workshop_unpublish_package

Removes a whole package and all its workshop versions from the workshop. It is the package counterpart of `workshop_unpublish_page`. Where `workshop_delete_package` removes one workshop version, `workshop_unpublish_package` removes the package itself and every workshop version it holds.

This is destructive administration and **always prompts for confirmation**. There is no `confirmWithUser` opt-out, and the content is not recoverable through the workshop afterward.

## Parameters

### packageName

The name as published on the workshop (lowercase alphanumeric with single hyphen separators, 1-64 characters).

## Returns

A JSON object echoing `packageName` and `unpublished: true`.

## Gotchas

- **This removes every workshop version, not just the latest.** To remove a single workshop version and keep the rest, use `workshop_delete_package`.
- **Irreversible through the workshop.** Durability rests on consumers vendoring the content they depend on, not on the workshop retaining it.
- **The package itself is removed, not just emptied.** It disappears from `workshop_list_packages`, `workshop_get_package_info` fails as for an unknown name, and a later publish under the same name starts a new package at workshop version 1.
- Unpublishing a package does not touch any page, because pages and packages are separate.
