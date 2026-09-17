---
name: update-third-party-licenses
description: Regenerate THIRD-PARTY-LICENSES.txt, the open source notices file that ships with Celbridge, resolve the license questions it raises, and review the README's open source credits. Use when the user asks to update, regenerate or check third-party licenses, license notices, open source attributions or credits, when preparing a release, or after adding, upgrading or removing a NuGet package, vendored script, font or bundled tool.
---

# Update third-party licenses

`THIRD-PARTY-LICENSES.txt` at the repo root lists every third-party component that ships in Celbridge, with its version, license, source and copyright notice, followed by the full license texts and the third-party notices that components ask to be reproduced. It is packaged with the app, so it is what users and reviewers read.

The file is generated. Never edit it by hand, because the next run overwrites the edit. Change `manifest.toml` instead.

The work splits in two:

- **`generate_licenses.py` does the mechanical part.** It reads the restore output of the application project, keeps the NuGet packages that put files into the build output, reads each package's nuspec from the local NuGet cache, adds the bundled components from the manifest, and writes the file. It reports anything it cannot settle as an issue and writes nothing until the issues are gone.
- **You do the judgement part.** Resolve each issue and record the decision in `manifest.toml`, so the same question is not asked again next release.

The aim is a good-faith, best-effort notices file, not a legal audit. Record only what you have checked, and take anything uncertain to the user rather than guessing.

## 1. Restore

Run on Windows, where the Windows head's packages restore. From the repo root (see `docs/development/building.md` if MSBuild is elsewhere):

```bash
"C:/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" Source/Celbridge/Celbridge.Application.csproj -t:Restore -verbosity:minimal -nologo
```

A restore is needed after `cb clean` or any package change, and costs a few seconds otherwise. Restore is the same for Debug and Release, so there is no configuration to choose.

## 2. Run the script

```bash
python .claude/skills/update-third-party-licenses/generate_licenses.py
```

It needs Python 3.11 or later. It prints the package and component counts, a tally of licenses, then any warnings and issues. Pass `--check` to report without writing.

A warning that a `generated` path does not exist means the build or install step that creates it has not run (the uv download happens in a build, SpreadJS is installed by `cb spreadjs`). Run that step first when preparing a release, so the version checks see what will ship.

## 3. Resolve the issues

Every fix is an edit to `manifest.toml` followed by another run. Give each package decision a `reason` saying what you checked and where. It is the audit trail for whoever runs this next.

| Issue | Resolution |
|---|---|
| No license information, or only a license URL | Find the license in the package's project or repository (its LICENSE file), on its nuget.org page, or among the files in its NuGet cache folder. Record it as `license` in a package decision. If there is none to be found, ask the user. |
| License is not in `accepted_licenses` | Judge it with the guidance below, then record `accept_license = true` with a reason, or take it to the user. |
| No license text for an identifier | Download `text/<identifier>.txt` from the latest release of the SPDX License List data (`github.com/spdx/license-list-data`) and save it unmodified as `license-texts/<identifier>.txt`. Ask the user before downloading. |
| Ships a third-party notices file | Read the notices file in the package folder. Set `notices = "include"` when it covers code built into what the package ships, and `"omit"` when it is repository-wide or lists the project's own build and test dependencies. |
| Looks like third-party code but nothing covers it | Identify the code and add a `[[component]]` whose `paths` include the folder. If the folder is Celbridge's own, add an `[[ignored_path]]` with a reason instead. |
| The manifest version differs from the repository | The vendored code was updated. Update `version`, and check that its license and copyright lines have not changed. |
| A path does not exist | The component moved or was removed. Update `paths`, or delete the component. |
| A decision matches no package | The package is gone. Delete the decision, or the id from it. |

### Judging a license

- MIT, BSD and Apache-2.0 are accepted without review.
- Other permissive licenses (Unicode, MS-PL, SIL OFL for fonts, zlib, ISC) are fine with their text included. Accept them with a reason.
- Weak copyleft (LGPL, MPL, EPL) is acceptable for an unmodified library shipped as separate files. Tell the user the first time a new one appears, because it is a new obligation.
- Strong copyleft (GPL, AGPL), a commercial or proprietary license, or no license at all needs the user's decision before it is recorded.
- Never add an identifier to `accepted_licenses` without the user agreeing, since that silently accepts every future package under it.

### Excluding a package

Set `exclude = true` only for a package that does not reach the Release build output. The restore graph also holds packages that a Release build strips, such as Uno's developer tooling, so confirm by looking for the package's assemblies in `Source/Celbridge/bin/Release/net10.0-windows10.0.22621/win-x64/AppX`.

### Vendored bundles

The script finds vendored code by looking under `Source` for folders named `lib`, `min` or `vendor`, font files, license or notices files, and minified scripts, skipping `bin`, `obj`, `node_modules`, `.venv` and `.celbridge`. It checks versions only where a component has a `version_check`. Libraries bundled inside another bundle (ProseMirror inside the Tiptap script, js-base64 inside the xterm clipboard addon) have no check of their own. When a vendoring script such as `npm run vendor:notes` or `npm run vendor:console` has run since the last release, list the bundled packages with `npm ls --omit=dev --all` in its `build` folder and bring those components up to date.

## 4. Review the README credits

The Credits section of `README.md` thanks a short, hand-picked list of the open source projects Celbridge is built on. It is a courtesy rather than a license requirement, and `THIRD-PARTY-LICENSES.txt` stays the complete list, so the credits only need to name the projects people would expect to find there.

Compare the list with what Celbridge uses now, using the component changes from this run and a search of the code:

- **Suggest adding** a project that a user or contributor would recognise and that powers a visible feature or the app's foundation.
- **Suggest removing** a credited project that Celbridge no longer uses. Search the code as well as the notices file, because some credited projects are not bundled. Python and IPython, for example, are installed per project rather than shipped with the app.
- **Leave out** projects that another entry already covers (SkiaSharp and the Windows App SDK come with Uno Platform), commercial components (SpreadJS has its own sponsor thanks), and features that are off by default or likely to be removed.

Give each project its own bullet with a link to its website or repository and no description, and keep the list short. Put your suggestions to the user rather than editing the list, because what belongs on it is their call, and make the edits once they agree.

## 5. Report

Once the file is written, read `git diff --stat THIRD-PARTY-LICENSES.txt` and skim the changes to the component list. Report to the user:

- the components added, removed or upgraded,
- any license that is new to the file,
- each decision you recorded in the manifest, with its reason,
- your suggested changes to the README credits, or that none are needed,
- anything left for them to decide.

Do not stage or commit. The user reviews the diff before committing.

## License texts

`license-texts/` holds the full text of each license the file uses, one file per exact SPDX identifier (`LGPL-2.1-or-later.txt`, not `LGPL-2.1.txt`). Every file is an unmodified copy of the matching file in the SPDX License List data, taken from release v3.29.0 for the current set. SPDX is the source of truth for these texts, so never edit them or take a license text from anywhere else.

- A component that ships its own license file, such as Cascadia Mono's `OFL.txt`, uses `license_file` instead, because that file states the component's actual terms.
- SPDX's texts keep template copyright lines such as `Copyright (c) <year> <copyright holders>`, while each entry carries the component's real copyright lines. Where a generic text names a specific copyright holder, as the Unicode License does, put the component's own copyright line in its entry or note.

## Manifest reference

Top level:

- `assets_file`: the restore output to read, relative to the repo root.
- `accepted_licenses`: SPDX identifiers that need no review.

`[[package]]`, a decision about NuGet packages. Every decision whose `ids` match a package applies, and where two set the same field the earlier one wins.

- `ids`: a package id or a list of them. `*` wildcards are allowed and matching ignores case.
- `exclude`: the package does not ship, so it is left out of the file.
- `license`: an SPDX expression that replaces missing or wrong metadata.
- `accept_license`: a license outside `accepted_licenses`, or a license file of the package's own, has been reviewed.
- `notices`: `"include"` or `"omit"` for a package that ships a third-party notices file.
- `copyright`, `source`: replace the nuspec's copyright line or project link.
- `note`: a line printed with the package's entry.
- `reason`: why the decision was made. It is not printed.

`[[component]]`, a bundled component that no package manager reports:

- `name`, `source`, `copyright` (a string or a list).
- `license`: an SPDX expression. Write a license without an SPDX identifier as `LicenseRef-Words-Of-The-Name`, which is printed as plain words.
- `version`, and a `[component.version_check]` table with a `file` glob and a `pattern` whose first group captures the version.
- `paths`: the repository folders the component occupies, which the vendored-code check treats as covered.
- `generated`: the paths are created by a build or install step, so a missing path is a warning rather than an issue.
- `license_file`: the component's own license text, printed verbatim.
- `notices_file`: third-party notices to reproduce.
- `runtime_pack`: a NuGet runtime pack id. The version and notices come from the newest cached pack for the app's .NET version.
- `note`: a line printed with the entry.

`[[ignored_path]]`: a `path` the vendored-code check skips, with a `reason`.
