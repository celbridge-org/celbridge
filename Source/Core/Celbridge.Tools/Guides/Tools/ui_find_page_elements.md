# ui_find_page_elements

Finds elements in an open document's page and reports each one's frame in the window, in the same coordinates
`ui_find_controls` gives a control, with its state. It is for a test harness that presses a page element with
real input, or reads where an element is and what it holds. To read a page without placing it in the window,
`webview_query` is enough.

**Test automation builds only.** In a build without test automation, such as an ordinary Release build, the tool
refuses with "available only in builds with test automation".

## Parameters

- `resource` — the resource key of a document whose page has a tool bridge. A `.webview` document has none.
- `selector`, `role` and `text` — the lookup's mode, exactly one per call, as `webview_query` takes them. `name`
  narrows a `role` lookup to accessible names that contain it.
- `frame` — the frame to search, as the `webview` tools name it. Empty searches the page's content frame, or the
  page itself when it marks none, and `top` names the page itself.
- `maxResults` — the most elements to return. Default 20.

## Returns

```json
{
  "frame": "top",
  "totalMatches": 1,
  "elements": [
    {
      "tag": "button",
      "selector": "#preview-reload-button",
      "role": "button",
      "accessibleName": "Reload Preview",
      "isVisible": true,
      "bounds": {"x": 1610, "y": 96, "width": 28, "height": 28},
      "isInView": true,
      "text": "",
      "isDisabled": false,
      "isFocused": false
    }
  ],
  "webViewBounds": {"x": 340, "y": 88, "width": 1580, "height": 860},
  "devicePixelRatio": 2,
  "contentWidth": 1920,
  "contentHeight": 948,
  "rasterizationScale": 2
}
```

- `bounds` and `webViewBounds` are device-independent pixels from the top left of the window's content.
  `webViewBounds` is the frame `ui_find_controls` reports for the document's web view. A CSS pixel in the page spans
  `devicePixelRatio / rasterizationScale` device-independent pixels, which is more than 1 when Windows' text size is
  raised.
- `isInView` is true when the element's center lies inside every frame that holds it and inside the web view. An
  element scrolled out of view keeps its true position, so its `bounds` can lie outside `webViewBounds`.
- `value` appears only for a text field, a text area or a select element. `isChecked` appears only for a check box,
  a radio button, an option, or an element with an `aria-checked`, `aria-pressed` or `aria-selected` state.
- `isFocused` is true while the element holds the keyboard: it is its document's active element and the page has
  focus.

## Gotchas

- The call fails when the document's web view is not showing, such as in a background tab. On macOS that includes
  every web view while a modal dialog is open.
- Another element can cover one that is in view. The tool does not test what a press at its center would reach.
