# package_publish

Zips the contents of `packages/{packageName}/` and uploads the result to the remote package registry as `{packageName}.zip`. Validates the package layout and manifest before uploading; an existing entry with the same name on the registry is overwritten by the upload.

By default the call surfaces a confirmation dialog before publishing; pass `confirmWithUser: false` only when the user has explicitly asked for unattended operation.

## Parameters

### resource

Resource key of the package folder. Must be inside the `packages/` folder (i.e. start with `packages/`), and the folder name must equal `packageName`. Both checks are explicit so a typo can't publish the wrong folder under a different name.

### packageName

Lowercase alphanumeric and hyphens, 1-214 characters. Must match the folder name segment of `resource`.

### confirmWithUser

When `true` (default), shows a confirmation dialog before uploading. When `false`, runs silently. Leave at the default unless the user has asked for an unattended run — see `silent_vs_interactive`.

## Validation

Before uploading, the tool verifies that:

- `resource` is inside `packages/` and the folder segment equals `packageName`.
- The folder exists on disk.
- A `package.toml` file is present at the folder root.
- The manifest is valid TOML and contains a `[package]` section with non-empty `id` and `name` fields.

If any check fails, no upload is attempted.

## Returns

A JSON object:

- `packageName` (string) — echoed package name.
- `entries` (int) — number of files included in the uploaded zip.
- `size` (long) — uploaded zip size in bytes.

## Gotchas

- Symlinks and other reparse points inside the package folder are skipped, not followed.
- Publishing replaces any existing registry entry with the same file name; there is no version check on the registry side.

## See also

- `package_install` — the download counterpart.
- `package_list` — discover what is already published.
- `packages_overview` — manifest schema and the registry workflow.
- `silent_vs_interactive` — when `confirmWithUser` should be `false`.
