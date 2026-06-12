# package_publish

Zips the contents of `packages/{packageName}/` and publishes the result to the workshop as a new version of the package. Versions are immutable and numbered by the workshop in publish order; publishing never overwrites an earlier version. The first publish of a new name registers the package on the workshop.

Validates the package layout and manifest before uploading. By default surfaces a confirmation dialog before publishing; pass `confirmWithUser: false` only when the user has explicitly asked for unattended operation.

## Parameters

### resource

Resource key of the package folder. Must start with `packages/` and the folder name must equal `packageName`. Both checks are explicit so a typo cannot publish the wrong folder under a different name.

### packageName

Lowercase alphanumeric with single hyphen separators, 1-64 characters. Must match the folder name segment of `resource` and the manifest's `name` field.

### confirmWithUser

When `true` (default), shows a confirmation dialog before uploading. Leave at the default unless the user has asked for an unattended run.

## Validation

Before uploading, the tool verifies that:

- `resource` is inside `packages/` and the folder segment equals `packageName`.
- The folder exists on disk.
- A `package.toml` file is present at the folder root.
- The manifest is valid TOML and contains a `[package]` section whose `name` field equals `packageName`.

If any check fails, no upload is attempted.

## Returns

A JSON object:

- `packageName` (string) — echoed package name.
- `version` (int) — the version number the workshop assigned to this publish.
- `entries` (int) — number of files included in the uploaded zip.
- `size` (long) — uploaded zip size in bytes.

## Gotchas

- Symlinks and other reparse points inside the package folder are skipped, not followed.
- Publishing always creates a new version; there is no way to replace or delete an existing version through the tools.
