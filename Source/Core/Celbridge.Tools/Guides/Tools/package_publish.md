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

- An **Author** is set in Workshop settings (it is recorded as the version's publisher).
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
- `warning` (string or null) — an advisory note, or `null`. Currently set when this folder was published from a stale base (see Concurrent publishing).

## HISTORY.md

After a successful publish, the tool writes a fresh `HISTORY.md` beside the manifest recording the version just assigned (one `# name@version` section per version, newest first, each with a compact metadata line — see `packages_overview`). This makes the source folder match what a consumer who installs that version receives, and lets `package_status` report the right version for it. The file itself is excluded from the upload (matched case-insensitively) — the workshop stays authoritative for publish history.

## Concurrent publishing

The workshop is a shared rendezvous point with no concurrency guard, so two people starting from the same version and both publishing produce siblings that the linear history presents as a sequence. As a guardrail, if the source folder was installed from a version older than the workshop's current latest, another version landed after this folder was installed and this publish may overwrite or diverge from it. When this is detected:

- With `confirmWithUser: true` (default), the confirmation prompt spells out the staleness — it names the installed and latest versions and asks you to continue — so you give informed consent rather than discovering the clash afterward.
- With `confirmWithUser: false`, the publish still proceeds (publishing is append-only — the other version is not destroyed), and the result's `warning` field reports the clash so an agent can react.

Either way, consider reinstalling the latest version and re-applying your changes before publishing. The check only fires for same-package iteration; a folder installed from a different package (a rename or fork) is not flagged.

The check needs the install record (`HISTORY.md`) to read which version the folder came from. A folder with **no** record — a package authored in place — is a normal case and is not flagged. But a record that is **present yet unreadable or malformed** means the check could not run, so it is surfaced the same way (confirmation note plus result `warning`): the publish still proceeds, but you are told the stale-base check was skipped.

## Gotchas

- Symlinks and other reparse points inside the package folder are skipped, not followed.
- Publishing always creates a new version and never overwrites an earlier one. To remove a version, use `package_delete`; to remove a whole package, `package_unpublish`.
- The publisher recorded on the version is the **Author** set in Workshop settings (Settings page), not a manifest field. Publishing fails with a clear message (and an alert, when interactive) if no Author is configured.
