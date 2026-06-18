# page

The `page` namespace publishes, lists, inspects, and unpublishes static web pages on the connected workshop. A page is a folder of static content (HTML, JavaScript, CSS, assets) served at a public URL, with a `pages.toml` manifest at its root naming the served path. See `pages_overview` for the manifest and the publish-only model.

## Must-knows

- **Pages are publish-only.** There is no pull or install of a page, by design. The served site is the rendered output, not the original bundle, and the workshop keeps no recoverable copy. If you need a versioned, pullable site, wrap the content in a package and publish that — the package is the versioned artifact, the page is its deployment.
- **The served path comes from the manifest.** `page_publish` reads `[publish].path` from `pages.toml`; the local folder name does not matter and there is no separate path argument.
- **Pages are decoupled from packages.** Publishing or unpublishing a page never touches a package, and vice versa.
- **Publishing and unpublishing confirm by default.** `page_publish` and `page_unpublish` are outward-facing and prompt before acting; pass `confirmWithUser: false` only for unattended flows the user has consented to. See `silent_vs_interactive`.
- **`page_publish` requires a loaded project.** The source is a project folder, so a project must be open.

## Tools

- `page_list` — list the pages published to the workshop.
- `page_info` — inspect one published page (served URL, publisher, content hash).
- `page_publish` — zip a folder and publish it as a page; served path read from `pages.toml`. Interactive by default.
- `page_unpublish` — remove a page's served content. Interactive by default.
