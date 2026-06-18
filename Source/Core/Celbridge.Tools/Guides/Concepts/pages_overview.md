# Pages

A **page** is a folder of static web content (HTML, JavaScript, CSS, assets) published to the workshop and served at a public URL. Pages are a decoupled subsystem: they have no relationship to packages, and publishing or unpublishing a page never touches a package.

## Manifest (`pages.toml`)

A page folder must contain a `pages.toml` at its root naming the path the site is served at:

```toml
[publish]
path = "my-site/home"
```

The path is multi-segment and becomes a subpath of the served URL. The page ZIP's root is the served site: everything in the folder is published verbatim except `pages.toml` itself, which the workshop reads to learn the path.

## Publish-only by design

There is **no pull or install of a page** — this is intentional, not a missing feature. A page is a deploy target: rendered static content served at a public URL, replaceable at any time. The `page` tools are publish, list, inspect, and unpublish only; the served site is the rendered output, not the original bundle, and the workshop keeps no recoverable copy of what you uploaded.

The consequence to plan around: **a page published from a folder that is later lost cannot be retrieved.** If you need a versioned, content-addressed, recoverable, and pullable site, wrap the content in a **package** and publish that — the package is the versioned artifact, and the page is just the deployment of its content. Keeping the source folder under version control (or as a package) is the recommended safeguard.

## Workflow

| Tool | What it does |
|---|---|
| `page_list()` | List all pages published to the workshop |
| `page_info("my-site/home")` | Inspect one published page (served URL, publisher, content hash) |
| `page_publish("pages/site", confirmWithUser)` | Zip a folder and publish it as a page; the served path comes from `pages.toml` |
| `page_unpublish("my-site/home", confirmWithUser)` | Remove a page's served content |

`page_publish` takes a folder resource key (or the `pages.toml` key), defaulting to `pages/` in the project root; the source can live under any readable root, including a `temp:` staging area. The served path always comes from the manifest, independent of the local folder name.

The publisher recorded on a page is the **Author** set in Workshop settings (Settings page). `page_publish` fails if no Author is configured.

## Confirmation prompts

`page_publish` and `page_unpublish` are outward-facing and confirm by default. Both accept `confirmWithUser` (default `true`); pass `false` only when the user has explicitly asked for unattended operation. They are held to a more lenient bar than the package admin tools (`package_delete`, `package_unpublish`, which always prompt with no opt-out), because a page is re-publishable static content rather than irreversible version history.
