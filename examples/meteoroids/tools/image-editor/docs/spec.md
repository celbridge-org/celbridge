# Image Editor — Specification

## Overview

The Image Editor is a Celbridge package for simple whole-image edits (crop, resize, rotate, flip) on image files in the project. It saves the edited image back to its file.

Typical use: a project has screenshots, sprites, icons or photos that need a quick trim, a resize to a target size, or a conversion from PNG to JPEG or WebP. The user shouldn't need an external app for that.

## Goals

- **Common formats.** Open the formats the WebView can decode, and save back in the same format.
- **Simple, whole-image edits.** Crop, resize, rotate, flip. Each edit changes the whole image's geometry. Nothing paints individual pixels.
- **Undoable.** Every edit can be undone and redone until the document is closed.
- **No compounding loss.** Celbridge auto-saves after each edit. Those repeated saves never re-encode an already re-encoded image. Every save encodes once, starting from the original pixels.
- **Untouched unless edited.** Opening and closing an image without editing it never rewrites the file.
- **Metadata survives.** Camera data, colour profiles and other embedded metadata are carried through every save.
- **Native look and feel.** The editor uses the Celbridge client's shared styles and UI components, so it matches the built-in editors in every theme.

## Non-goals

- Pixel editing: brushes, erasers, fills, selections, clone/heal tools.
- Layers, masks, blend modes, text or shape overlays.
- Filters and colour adjustments (brightness, contrast, levels, curves, blur, sharpen).
- Editing animation frames. Animated images open as their first frame (see *Formats*).
- Vector images (SVG), camera RAW, PSD, TIFF, HEIC and AVIF. AVIF isn't in the host's file type catalog, so it isn't a known binary type.
- Viewing or editing metadata. The editor carries metadata through, but has no UI for it.

## Package

- A Celbridge package named `image-editor` that contributes one document editor, with one `[[file-types]]` entry per extension listed under *Formats*.
- Each `[[file-types]]` entry gives `extension` and the required `display-name` (a localization key). These are established types in the host's file type catalog, which provides their icons and wins for established types, so the package declares no icons of its own.
- It becomes the default editor for those extensions with no extra configuration. The host picks the first contribution that supports an extension ahead of the built-in editors, so the package takes over from the built-in image viewer (which has no zoom or other controls). The built-in viewer stays available in the Open With picker.
- No New File template. Images come from outside the project or from other tools, such as the Emoji Sprite utility.
- The editor is a WebView page that uses the Celbridge JS client for the document lifecycle, toasts, view state and localization. The editor manifest declares `binary = true`, so the host passes the document to the editor as base64 and saves the base64 the editor gives it through `document.save()`.
- Decoding and encoding use the browser's built-in image support where possible (`createImageBitmap`, `OffscreenCanvas.convertToBlob`). The browser can't encode GIF or BMP, so the package ships small hand-written encoders for both as plain ESM files and needs no build step.
- All user-facing strings are localized.

## Formats

| Format | Extensions | Open | Save | Notes |
|---|---|---|---|---|
| PNG | `.png` | ✓ | ✓ | Lossless, keeps alpha. |
| JPEG | `.jpg`, `.jpeg` | ✓ | ✓ | Lossy, no alpha. Quality setting. |
| WebP | `.webp` | ✓ | ✓ | Lossy or lossless, keeps alpha. Quality setting. |
| GIF | `.gif` | ✓ | ✓ | Saved as a single frame, 256 colours max. Hand-written encoder (`lib/gif-encoder.js`). |
| BMP | `.bmp` | ✓ | ✓ | Saved as 32-bit BGRA. Small hand-written encoder. |
| ICO | `.ico` | ✓ | — | Opens the largest embedded image. Read-only. |

- Formats marked read-only open with a notice bar that explains why.
- Animated GIF, WebP and PNG (APNG) open as their first frame, read-only, with a notice bar. Saving over them would silently drop the animation.
- The file extension decides the save format, not the file's contents. If the contents don't match the extension (for example a PNG named `.jpg`), the editor still opens it, and the status bar shows the detected format.
- EXIF orientation is applied on load, so photos appear upright. The saved file is written upright, with its orientation tag reset to normal (see *Metadata*).

## Edit model

The document in memory is:

- **source**: the decoded original pixels, exactly as loaded from disk. Never modified.
- **metadata**: the original file's metadata blocks, extracted on load (see *Metadata*). Never modified. Adjusted copies are written on each save.
- **operations**: an ordered list of edits, applied to `source` to produce the current image.
- **encoding**: save options for the file's format (quality for JPEG/WebP, lossless toggle for WebP), taken from defaults when the file opens.

Operations:

| Operation | Parameters |
|---|---|
| `crop` | `x`, `y`, `width`, `height` in pixels of the image it applies to |
| `resize` | `width`, `height`, `resampling` (`smooth` or `pixelated`) |
| `rotate` | `90`, `180` or `270` (clockwise) |
| `flip` | `horizontal` or `vertical` |

- The current image is `source` with each operation applied in order. The editor caches the result so the preview doesn't recompute the whole chain on every repaint.
- Undo and redo move a cursor along the operation list. A new edit after an undo discards the redo tail.
- Rotate and flip operations that cancel out (e.g. four 90° turns, two horizontal flips) are merged, so an image that is back to its original geometry counts as having no edits.
- Saving encodes the current image once, from `source`. After a successful save, the undo history stays available: the user can undo past the save and save again, and the file is re-encoded from `source` again. It is never encoded from the previously saved bytes.
- The operation list lives only in memory. It is not written to a sidecar and does not survive closing the document.

## User experience

The editor fills the tab with three parts: a **toolbar** along the top, the **canvas** in the middle, and a **status bar** along the bottom.

### Toolbar

Left to right:

- **Undo / Redo.**
- **Crop.** Enters crop mode (below).
- **Resize.** Opens the resize panel (below).
- **Rotate left / Rotate right** (90°) and **Flip horizontal / Flip vertical.** These apply immediately.
- **Zoom:** 100%, zoom out, zoom in, and the current zoom percentage. There is no fit button: the image opens fitted, and the user sets the zoom from there.
- Every toolbar control is an icon with a tooltip. No control has a text label.
- Toolbar controls keep their size and spacing. When the tab is too narrow to show the whole toolbar (or crop bar), it clips on the right rather than scrolling or squeezing.
- **Save settings**, only for formats that have options (JPEG quality, WebP quality/lossless). It sets the options used when the file itself is saved. Changing them counts as a change and triggers a save.

### Canvas

- The image is centered. It opens fitted to the view: zoomed out or in to the largest size that shows the whole image, so a small sprite opens large. Until the user zooms or pans, it stays fitted as the view resizes.
- Ctrl+wheel or pinch zooms around the pointer. Zoom range is 1%–3200%.
- Panning: drag the image, scroll with the wheel or touchpad (Shift+wheel scrolls sideways), or use the scrollbars. In crop mode the left button belongs to the crop rectangle, so Space+drag and middle-drag pan there as well.
- Overlay scrollbars appear on an axis only while the image is larger than the view on it. Dragging a thumb scrolls. Clicking the track pages towards the click.
- The image can never be moved off screen. On an axis where it fits the view it stays centred. Where it is larger, it scrolls only until its edge reaches the edge of the view (plus a small margin). This holds after every pan, zoom, edit and window resize.
- Transparent areas show a checkerboard. The checkerboard follows the theme. The image itself is never tinted.
- From 400% zoom upwards, pixels are drawn nearest-neighbour so individual pixels are visible. Below that the preview is smoothed.
- The preview stays sharp at any display scaling, including when the window moves to a display with a different pixel density.

### Crop mode

- A crop rectangle over the image with eight drag handles. Outside the rectangle is dimmed. Dragging inside the rectangle moves it. Dragging on the image outside it draws a new one.
- A crop bar replaces the toolbar and holds:
  - **X, Y, W, H** number fields in image pixels, kept in sync with the rectangle.
  - **Apply** (also Enter) and **Cancel** (also Escape).
- The rectangle always stays inside the image and snaps to whole pixels. The minimum is 1×1.
- Applying adds one `crop` operation. Applying a crop that covers the whole image does nothing.

### Resize panel

A small panel anchored under the toolbar button:

- **Width** and **Height** in pixels, with a **lock aspect ratio** toggle (on by default).
- **Percentage** field as an alternative. Editing it updates width and height.
- **Resampling:** Smooth (default, high-quality downscaling) or Pixelated (nearest-neighbour, for pixel art and sprites).
- The resulting size and the approximate uncompressed memory size are shown.
- **Apply** and **Cancel.** If a number field is invalid or out of range (1–16384), show an inline warning and disable Apply. Partial input must not be applied.
- Smooth downscaling by more than 2× is done in successive halving steps, so the result doesn't alias.

### Status bar

Shows the current pixel dimensions (plus the original dimensions when they differ), the format (and the detected format if it doesn't match the extension), the file size on disk, the zoom level, and the pointer position in image pixels while the pointer is over the image.

The status bar also lists the kinds of metadata the file carries (EXIF, ICC, XMP, text), so the user can see what will be preserved.

### Guidance states

- **Can't decode:** the file is corrupt, empty or not an image. Show a message with the reason and nothing else. The document opens but can't be edited.
- **Read-only format / animated image / read-only document:** show a notice bar. Editing tools are disabled. Zoom and pan still work.
- **Too large:** images with more than 16384 pixels on either side, or more than 100 megapixels, open read-only with a notice that names the limit. This keeps the editor within WebView canvas limits.

## Saving

- There is no explicit save. About a second after the last change, the editor encodes the current image in the file's format using the save settings, attaches the preserved metadata, and saves the bytes as base64 with `document.save`. The host's auto-save drives this: each change calls `notifyChanged()`, and the host calls `onRequestSave` about a second after the last change, and when the tab closes.
- If there are no operations and the save settings are unchanged (the user undid every edit), the editor doesn't encode anything and saves the original bytes unchanged.
- A JPEG save flattens transparency onto white. The first time a JPEG save would drop transparency, a toast says so.
- A GIF save quantizes to at most 256 colours. Fully transparent pixels become the transparent index. Partially transparent pixels are flattened onto white.
- If encoding or writing fails, show an error. The in-memory edits are kept, and the next change tries again.
- Because saves are automatic, a crop or resize is on disk moments after it is applied. The undo history is the user's way back during the session, and source control is the way back after it.

## Metadata

The editor carries the original file's metadata through every save. Metadata is extracted from the raw bytes on load, stored with the document, and inserted into the newly encoded bytes on save. The browser's encoders drop all metadata, so the editor splices it back in itself, using a small hand-written container parser/writer per format. No vendored library is needed.

### What is kept, per format

| Format | Carried through |
|---|---|
| JPEG | APP1 EXIF, APP1 XMP (including extended XMP), APP2 ICC profile (all chunks, in order), APP13 IPTC/Photoshop, COM comments. JFIF (APP0) is regenerated by the encoder. |
| PNG | `eXIf`, `iCCP`, `sRGB`, `gAMA`, `cHRM`, `cICP`, `pHYs`, `tEXt`, `zTXt`, `iTXt`. They are placed before `IDAT`, as the PNG spec requires. |
| WebP | `ICCP`, `EXIF`, `XMP ` chunks. The `VP8X` header is written (or rewritten) with the right feature flags and canvas size. |
| GIF | Comment extensions. Only the first frame is saved, so animation-control extensions (e.g. `NETSCAPE2.0` looping) are dropped. |
| BMP | Resolution (pixels per metre) and an embedded ICC profile in a `BITMAPV5HEADER`. |

Chunks and segments the editor doesn't recognise are dropped, not copied blindly. They may describe pixel data that no longer exists.

### What is adjusted

Metadata that describes the pixels must still be true after an edit:

- **EXIF orientation** is set to 1 (normal), because the saved pixels are already upright.
- **EXIF pixel dimensions** (`PixelXDimension` / `PixelYDimension`) are updated to the saved size.
- **The EXIF thumbnail** (IFD1 and its JPEG data) is removed once the image has been cropped, rotated or flipped, because it would no longer match. A resize alone keeps it.
- **XMP** `tiff:Orientation`, `exif:PixelXDimension` and `exif:PixelYDimension` are updated to match, when present. The rest of the XMP packet is untouched.
- **PNG `tIME`** (last modification time) is set to the save time.
- Everything else (camera, lens, GPS, timestamps, copyright, keywords, text chunks) is copied byte for byte.

### Colour profiles

Pixels are decoded without colour-space conversion (`createImageBitmap` with `colorSpaceConversion: 'none'`), so `source` keeps the values as they are in the file's own colour space. The copied ICC profile therefore still describes the saved pixels correctly. The on-screen preview is drawn with colour management, so it looks the same as in other viewers.

### Malformed metadata

If a metadata block can't be parsed (for example an EXIF block with bad offsets), it can't be adjusted. It is copied unchanged when only the save settings have changed. After any crop, resize, rotate or flip it is dropped, and a notice says so, rather than writing EXIF that contradicts the pixels. A metadata problem never stops the image from opening or saving.

## Host integration

Registered through `client.initializeDocument({ onContent, onRequestSave, onExternalChange, onRequestState, onRestoreState })`:

- **`onContent`**: decode the base64 content and extract metadata. The framework calls `notifyContentLoaded()`. Never save here.
- **`onRequestSave`**: encode and save, as described in *Saving*. Does nothing when there are no unsaved changes.
- **`onExternalChange`**: read the new content with `document.load()`, decode it, reset the history and drop any unsaved changes, then `notifyContentLoaded(ContentLoadedReason.ExternalReload)`. Forward `args.preserveViewState`, so zoom and scroll survive when the host asks. External edits always win, which matches the host's rule for every other editor.
- Every operation, undo, redo and save-settings change calls `notifyChanged()` (see *Saving*). The save writes the encoded image, or the original bytes when there are no edits. It can run while the tab is hidden, so encoding must not depend on the canvas being visible (use `OffscreenCanvas`). Crop mode and the resize panel report nothing until Apply, so dragging a crop handle never writes the file.
- **Writability** comes from `client.viewState.onChanged`, not from a document handler. Treat any `viewState.writable` other than `"Writable"` as read-only: disable the editing tools, keep zoom and pan enabled, and set a module-level `frameworkReadOnly` flag. Every path that reports a change checks that flag first, so a locked file is never auto-saved. The read-only formats, animated images and oversized images set the same flag.
- **View state:** zoom and scroll position go through `onRequestState` / `onRestoreState`, not the file.
- Re-render the UI when the UI language changes.
- Keyboard shortcuts while the canvas has focus: Ctrl+Z / Ctrl+Y (and Ctrl+Shift+Z) for undo/redo, C for crop, R / Shift+R for rotate right / left, Ctrl+0 to fit the view again, Ctrl+1 for 100%, Ctrl+= / Ctrl+- for zoom.

