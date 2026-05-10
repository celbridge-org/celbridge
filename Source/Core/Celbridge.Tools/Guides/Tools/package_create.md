# package_create

Creates a new package in the project's `packages/` folder. The resulting folder is `packages/{packageName}/` with a stub `package.toml` containing `[package]` (id, name, version) and an empty `[contributes]` section. Use this as the first step when authoring a new package.

The call fails if `packages/{packageName}` already exists — there is no overwrite option, by design. Delete or rename the existing folder first if you really mean to start over.

## packageName

Lowercase alphanumeric and hyphens, 1-214 characters (e.g. `"my-widget"`). Uppercase letters, underscores, dots, and other characters are rejected.

## Returns

A JSON object:

- `packageName` (string) — echoed package name.
- `resource` (string) — resource key of the new package folder, e.g. `"packages/my-widget"`.
- `manifestPath` (string) — resource key of the created manifest, e.g. `"packages/my-widget/package.toml"`.
