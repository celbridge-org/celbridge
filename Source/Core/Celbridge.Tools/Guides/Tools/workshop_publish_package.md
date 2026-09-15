# workshop_publish_package

Zips a package folder and publishes it to the connected workshop as a new workshop version. The package name is read from the manifest, so the source can live anywhere and need not sit under `packages/`. Workshop versions are immutable and numbered by the workshop in publish order, so publishing never overwrites an earlier one. The first publish of a new name registers the package on the workshop.

By default a confirmation dialog is shown before publishing. Pass `confirmWithUser: false` only when the user has explicitly asked for unattended operation.

## Parameters

### resource

Resource key of the package's `package.toml` manifest (its containing folder is also accepted). The folder that holds the manifest is what gets zipped and uploaded. Because this is a resource key, any readable root works, including assembling a package under `temp:package-staging` and publishing it from there without ever installing it into `project:`.

### summary

Optional. A concise paragraph describing the change, capped at 512 characters. It feeds the workshop version's metadata and the workshop history, so write it like a commit message, saying what changed and why rather than listing files. An over-long summary is rejected, never truncated, so you can rewrite it.

### confirmWithUser

When `true` (default), shows a confirmation dialog before uploading. Leave at the default unless the user has asked for an unattended run.

## Validation

Before uploading, the tool verifies that:

- An **Author** is set in Workshop settings (it is recorded as the publisher).
- `resource` resolves to a `package.toml` manifest (or a folder containing one).
- The manifest is valid TOML with a `[package]` section whose `name` is a valid package name.
- Any `package-version` in that section is a three-part version such as `1.2.0`, because a malformed one would stop the package loading once installed.
- The `summary`, if given, is within the 512-character cap.

If any check fails, no upload is attempted.

## Returns

A JSON object:

- `packageName` (string) — the name read from the manifest.
- `workshopVersion` (int) — the workshop version the workshop assigned to this publish. It is unrelated to the manifest's `package-version`.
- `entries` (int) — number of files included in the uploaded zip.
- `size` (long) — uploaded zip size in bytes.
- `warning` (string) — an advisory note, or the empty string when there is none. It is currently set when this folder is published from a stale base (see Concurrent publishing). Branch on a non-empty value rather than on key presence.

## HISTORY.md

After a successful publish, the tool writes a fresh `HISTORY.md` beside the manifest recording the workshop version just assigned, with one `# name@version` section per workshop version, newest first, each with a compact metadata line. This makes the source folder match what a consumer who installs that workshop version receives. The file itself is excluded from the upload (matched case-insensitively), and the workshop stays authoritative for publish history.

## Concurrent publishing

The workshop is a shared rendezvous point with no concurrency guard, so two people starting from the same workshop version and both publishing produce siblings that the linear history presents as a sequence. As a guardrail, if the source folder was installed from a workshop version older than the latest, another version landed after this folder was installed and this publish may overwrite or diverge from it. When this is detected:

- With `confirmWithUser: true` (default), the confirmation prompt spells out the staleness. It names the installed and latest workshop versions and asks you to continue, so you give informed consent rather than discovering the clash afterward.
- With `confirmWithUser: false`, the publish still proceeds, because publishing is append-only and the other workshop version is not destroyed. The result's `warning` field reports the clash so an agent can react.

Either way, consider reinstalling the latest workshop version and re-applying your changes before publishing. The check only fires for same-package iteration. A folder installed from a different package (a rename or fork) is not flagged.

The check needs the install record (`HISTORY.md`) to read which workshop version the folder came from. A folder with **no** record, such as a package authored in place, is a normal case and is not flagged. But a record that is **present yet unreadable or malformed** means the check could not run, so it is surfaced the same way, as a confirmation note plus the result `warning`. The publish still proceeds, but you are told the stale-base check was skipped.

## Gotchas

- Symlinks and other reparse points inside the package folder are skipped, not followed.
- Publishing always creates a new workshop version and never overwrites an earlier one. To remove one workshop version, use `workshop_delete_package`. To remove a whole package, use `workshop_unpublish_package`.
- The publisher recorded on the workshop version is the **Author** set in Workshop settings, not a manifest field. Publishing fails with a clear message (and an alert, when interactive) if no Author is set.
