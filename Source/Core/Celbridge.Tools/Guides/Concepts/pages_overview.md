# Pages

A **page** is a folder of static web content (HTML, JavaScript, CSS, assets) published to the workshop and served at a public URL. Pages are decoupled from packages: publishing or unpublishing a page never touches a package.

## Manifest (`pages.toml`)

A page folder must contain a `pages.toml` at its root naming the path the site is served at:

```toml
[publish]
path = "my-site/home"
```

The path is multi-segment and becomes a subpath of the served URL. The page ZIP's root is the served site: everything in the folder is published verbatim except `pages.toml` itself.

## Publish-only by design

There is **no pull or install of a page**. This is intentional, not a missing feature. A page is a deploy target: rendered static content served at a public URL, replaceable at any time. The page tools publish, list, inspect, and unpublish only. The workshop serves the files from the uploaded bundle but offers no way to download them, and replacing or unpublishing a page deletes its bundle.

The consequence to plan around: **a page published from a folder that is later lost cannot be retrieved.** If you need a versioned, content-addressed, recoverable, and pullable site, wrap the content in a **package** and publish that. The package is the versioned artifact, and the page is just the deployment of its content. Keeping the source folder under version control, or as a package, is the recommended safeguard.

## Workflow

| Tool | What it does |
|---|---|
| `workshop_list_pages()` | List all pages published to the workshop |
| `workshop_get_page_info("my-site/home")` | Inspect one published page (served URL, publisher, content hash) |
| `workshop_publish_page("pages/site", confirmWithUser)` | Zip a folder and publish it as a page, at the served path `pages.toml` names |
| `workshop_unpublish_page("my-site/home", confirmWithUser)` | Remove a page's served content |

`workshop_publish_page` takes a folder resource key (or the `pages.toml` key), defaulting to `pages/` in the project root. The source can live under any readable root, including a `temp:` staging area. The served path always comes from the manifest, independent of the local folder name.

The publisher recorded on a page is the **Author** set in Workshop settings. `workshop_publish_page` fails if no Author is set.

## Confirmation prompts

`workshop_publish_page` and `workshop_unpublish_page` are outward-facing and confirm by default. Both accept `confirmWithUser` (default `true`). Pass `false` only when the user has explicitly asked for unattended operation. They are held to a more lenient bar than the package administration tools, `workshop_delete_package` and `workshop_unpublish_package`, which always prompt with no opt-out, because a page is re-publishable static content rather than irreversible version history.
