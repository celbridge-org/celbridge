# package_create

Creates a new package in the project's `packages/` folder. The resulting folder is `packages/{packageName}/` and contains a stub `package.toml` with `[package]` (id, name, version) and an empty `[contributes]` section. Use this as the first step when authoring a new package; from there add editor manifests, web content, or other files before publishing.

The call fails if a folder already exists at `packages/{packageName}` — there is no overwrite option, by design. Delete or rename the existing folder first if you really mean to start over.

## Parameters

### packageName

Lowercase alphanumeric and hyphens, 1-214 characters (e.g. `"my-widget"`). Must be a valid package id; uppercase letters, underscores, dots, and other characters are rejected.

## Returns

A JSON object:

- `packageName` (string) — echoed package name.
- `resource` (string) — resource key of the new package folder, e.g. `"packages/my-widget"`.
- `manifestPath` (string) — resource key of the created manifest, e.g. `"packages/my-widget/package.toml"`.

## See also

- `packages_overview` — manifest schema and registry workflow.
- `package_publish` — upload a finished package to the remote registry.
