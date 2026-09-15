# Workshop versions

A package on the workshop is a container of immutable workshop versions, numbered 1, 2, 3 and so on in publish order. The workshop assigns each number when a version is published, and never reuses one. A workshop version is not the package version, the `package-version` a manifest declares, which the publisher sets and the workshop passes through unchanged. Every parameter and result field in the workshop tools that holds a workshop number is named for it: `workshopVersion`, `latestWorkshopVersion`, `workshopVersions` and `workshopVersionCount`.

## Aliases

Named aliases such as `latest` and `stable` point at workshop versions. The workshop manages `latest`, and the publisher defines the rest. Curating aliases is non-destructive: `workshop_set_package_alias` and `workshop_remove_package_alias` only repoint or detach a label, and never touch version content. `workshop_get_package_info` returns every workshop version and alias.

## Deleted versions

`workshop_delete_package` deletes one workshop version, which removes its content bytes permanently. The number, date and content hash are retained, so the number is never reused and a vendored copy stays verifiable, but the bytes are gone. Durability rests on consumers vendoring what they depend on, not on the workshop keeping every version available. `workshop_unpublish_package` removes a package and every workshop version.

## HISTORY.md

Installing a package writes its workshop history to a generated `HISTORY.md` beside the manifest, newest first, and `workshop_publish_package` writes the same file for the workshop version it assigns. The file is metadata about the workshop rather than package content. It is excluded from uploads, and the workshop stays authoritative. Only the workshop tools read it back: `workshop_install_package` names the installed workshop version when it replaces a folder, and `workshop_publish_package` checks whether the folder is behind the latest workshop version. It has no effect on the package version.

Each entry is shaped for grep and fragment reasoning:

```
# my-widget@5

[time: 2026-06-13T15:14:50Z, author: Acme, hash: eb1ddd1ce6a9]

Merge the credits change into the rolled-back content.
```

A `name@version` heading makes a quoted entry self-describing. One bracketed metadata line follows, with the full UTC `time`, the `author`, and a 12-character `hash` fingerprint, then the free-text summary. A deleted workshop version adds `deleted: true` and renders `[package_deleted]` as its body, so a gap in the numbering is explained rather than silent. The full content hash stays authoritative in `workshop_get_package_info`, and the short one is for cheap cross-checking of a summary's claims against the actual bytes.
