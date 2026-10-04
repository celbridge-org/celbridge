# ui_find_controls

Finds the application's own controls by automation ID, name or control type, and reports each one's frame and
state as its automation peer describes it. It is for a test harness that drives the application from outside and
needs to know where a control is and what it holds, where the platform's accessibility tree cannot say.

**Test automation builds only.** In a build without test automation, such as an ordinary Release build, the tool
refuses with "available only in builds with test automation".

It searches the main window's content and every open popup, which holds the flyouts, menus and dialogs. It reports
only controls that show: an element with a size whose ancestors are all visible. A web view is a `Pane` of class
`Microsoft.UI.Xaml.Controls.WebView2`. A document's web view takes the document's resource key as its automation ID,
such as `project:notes/today.md`, and the name its tab shows.

On macOS it also reports the parts AppKit draws natively:

- **The menu bar's items.** Each is a `MenuItem` of class `NSMenuItem`, named by its title, whether or not its menu
  is open. Its `isEnabled` and `isChecked` are what the menu would show if it opened now. `isChecked` is true while
  the item shows a mark, and is absent for an item that opens a submenu. An item has no frame in the window, so its
  bounds are all zero.
- **The window's buttons.** Each is a `Button` whose automation ID is its accessibility subrole: `AXCloseButton`,
  `AXMinimizeButton`, and `AXFullScreenButton` or `AXZoomButton`. They sit in the title bar above the content, so
  their `y` is negative. They are not reported while the window is minimized or in full screen.
- **The web views' frames.** A web view's bounds are the frame of its native view. A web view whose native view is
  hidden is not reported. That includes a web view in a background tab, and every web view while a modal dialog is
  open.

On Windows it also reports the caption buttons the Windows App SDK draws. Each is a `Button` whose automation ID
and name are `Minimize`, `Maximize` or `Close`, as UI Automation reports them. `Restore` takes the place of
`Maximize` while the window is maximized. The window's content extends into its title bar, so the buttons sit at
the top right of the content, with a `y` of 0. They are not reported while the window is minimized or in full
screen.

## Parameters

Give at least one. Each one given must equal the control's own value exactly, and the call fails when none is
given.

- `automationId` — the control's automation ID. A control without one is known by its `x:Name`, as UI Automation
  reports it on Windows.
- `name` — the control's automation name, which is usually the text it shows or its tooltip.
- `controlType` — the automation control type, named as UI Automation names it: `Button`, `Edit`, `ListItem`,
  `MenuItem`, `RadioButton`, `TabItem`, `Text` and so on.

## Returns

```json
{
  "controls": [
    {
      "automationId": "bottom-area-toggle-button",
      "name": "Toggle Bottom Panel",
      "controlType": "Button",
      "className": "Button",
      "bounds": {"x": 1842, "y": 4, "width": 32, "height": 32},
      "isEnabled": true
    }
  ],
  "contentWidth": 1920,
  "contentHeight": 948,
  "rasterizationScale": 2
}
```

- `bounds` is in device-independent pixels, measured from the top left of the window's content, which sits below
  the title bar on macOS and takes in the title bar on Windows. Multiply by `rasterizationScale` for physical
  pixels.
- `isChecked` appears only for a control that can be toggled or selected, and `value` only for one that holds a
  value, such as a text field's text.
- An empty `controls` list means nothing matched. It is not an error.

## Gotchas

- Controls come in tree order. An open popup's controls come after the window's, and the parts the platform draws
  natively come last.
- Find a menu bar item by its name. Its automation ID is usually the name of its action, which many items share.
- A control scrolled out of its view still shows, so its bounds can lie outside its scroll viewer.
