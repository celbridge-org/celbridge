# package_publish

Zips a package folder and publishes it to the connected workshop as a new version. The package name is read from the manifest, so the source can live anywhere and need not sit under `packages/`. Versions are immutable and numbered by the workshop in publish order; publishing never overwrites an earlier version. The first publish of a new name registers the package on the workshop.

By default a confirmation dialog is shown before publishing; pass `confirmWithUser: false` only when the user has explicitly asked for unattended operation.

## Parameters

### resource

Resource key of the package's `package.toml` manifest (its containing folder is also accepted). The folder that holds the manifest is what gets zipped and uploaded. Because this is a resource key, any readable root works — including assembling a package under `temp:package-staging` and publishing it from there without ever installing it into `project:`.

### summary

Optional. A concise paragraph describing the change, capped at 512 characters. It feeds the version metadata and the workshop history, so write it like a commit message — what changed and why, not an inventory of files. An over-long summary is rejected (never truncated) so you can rewrite it.

### confirmWithUser

When `true` (default), shows a confirmation dialog before uploading. Leave at the default unless the user has asked for an unattended run.

## Validation

Before uploading, the tool verifies that:

- `resource` resolves to a `package.toml` manifest (or a folder containing one).
- The manifest is valid TOML with a `[package]` section whose `name` is a valid package name.
- The `summary`, if given, is within the 512-character cap.

If any check fails, no upload is attempted.

## Returns

A JSON object:

- `packageName` (string) — the name read from the manifest.
- `version` (int) — the version number the workshop assigned to this publish.
- `entries` (int) — number of files included in the uploaded zip.
- `size` (long) — uploaded zip size in bytes.

## HISTORY.md

After a successful publish, the tool writes a fresh `HISTORY.md` beside the manifest recording the version just assigned (one `# <version>` section per version, newest first). This makes the source folder match what a consumer who installs that version receives, and lets `package_status` report the right version for it. The file itself is excluded from the upload (matched case-insensitively) — the workshop stays authoritative for publish history.

## Gotchas

- Symlinks and other reparse points inside the package folder are skipped, not followed.
- Publishing always creates a new version; there is no way to replace or delete an existing version through the tools.
- The workshop reads the publisher from the manifest's `author` field; set it before the first publish.
