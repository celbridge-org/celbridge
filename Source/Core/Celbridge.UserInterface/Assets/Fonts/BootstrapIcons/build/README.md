# Bootstrap Icons

Bootstrap Icons v1.12.1, MIT licensed, from https://github.com/twbs/icons.

One release feeds seven vendored files across two projects. `vendor.js` generates two of them and checks
the other five against the release.

`Celbridge.UserInterface/Assets/Fonts/BootstrapIcons/`

- `bootstrap-icons.ttf` — the font the native `Icon` control loads through `ms-appx`. **Generated**, never
  edited by hand: upstream publishes `woff` and `woff2` only and WinUI can load neither, so `vendor.js`
  decompresses the release `woff2` back to the TrueType font it was built from.
- `icon-glyphs.json` — the name to codepoint map `IconService` resolves `bs-` names through. The release's
  own `font/bootstrap-icons.json`, reformatted to hex codepoint strings and re-ordered. Copied by hand.
- `icon-keywords.json` — the search keywords the icon picker matches alongside the name. **Generated**,
  never edited by hand. Records the release it was generated from.
- `LICENSE` — the upstream licence, verbatim.

`Celbridge.WebHost/Web/bootstrap-icons/`

- `bootstrap-icons.css` — the stylesheet served to WebView content. A **local fork** of the release
  `font/bootstrap-icons.css`: reformatted, the `woff` fallback and the cache-busting query strings removed,
  and the base rule moved off `::before` onto the element. Regenerating it from upstream would lose those
  edits, so it is updated by hand.
- `fonts/bootstrap-icons.woff2` — the release web font, verbatim. Also the source the native font is
  generated from.
- `fonts/BOOTSTRAP-ICONS-LICENSE` — the upstream licence, verbatim.

The keywords are the `categories` and `tags` each icon declares in the front matter of its documentation
page, which is what the Bootstrap Icons site searches. That data is not part of the npm package, so
`vendor.js` reads it from the release tarball. The generated file covers the icons the bundled glyph map
carries, so documentation moving ahead of the bundled font cannot introduce a name the font is unable to
draw.

## Refreshing the generated files

From this folder:

```
npm install
npm run vendor
```

Or from `Source`, once the dependency is installed, as `npm run vendor:icons`.

The script downloads the pinned release and checks the hand-copied files against it before writing
anything: the glyph map against the release map (as data, since the vendored copy is reformatted), both
licences and the web font byte for byte, and the stylesheet for a rule drawing every icon in the glyph map.
It refuses to write while any of those disagree, which is what stops a half-finished upgrade shipping.

## Upgrading the release

1. Bump `upstreamTag` in `vendor.js` and `BundledBootstrapIconsVersion` in `IconCatalogCoverageTests`.
2. Copy the release `font/bootstrap-icons.json` over `icon-glyphs.json`, reformatting to hex codepoint
   strings, and the release `font/fonts/bootstrap-icons.woff2` and `LICENSE` over their vendored copies.
3. Port any upstream stylesheet changes into the forked `bootstrap-icons.css` by hand.
4. Run `npm run vendor`. It reports whichever of the above is unfinished, and regenerates the font and the
   keywords once they all pass.
5. Check the icons still draw, on the packaged Windows head and on Skia. The generated font is not byte
   identical to one produced by another converter, so this is the step no check here can stand in for.
