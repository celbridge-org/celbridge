# package_unpublish

Removes a whole package and all its versions from the workshop. This is the package counterpart of `page_unpublish`: where `package_delete` removes one version, `package_unpublish` removes the package itself and every version it holds.

This is destructive administration and **always prompts for confirmation**: there is no `confirmWithUser` opt-out. Version content is not recoverable through the workshop afterward.

## Parameters

### packageName

The name as published on the workshop (lowercase alphanumeric with single hyphen separators, 1-64 characters).

## Returns

A JSON object echoing `packageName` and `unpublished: true`.

## Gotchas

- **This removes every version, not just the latest.** To remove a single version and keep the rest, use `package_delete`.
- **Irreversible through the workshop.** Durability rests on consumers vendoring the content they depend on, not on the workshop retaining it. See `packages_overview`.
- Unpublishing a package does not touch any page; pages are a separate, decoupled subsystem.
