# app_find_controls

Finds the application's own controls by automation ID, name or control type, and reports each one's frame and
state as its automation peer describes it. It is for a test harness that drives the application from outside and
needs to know where a control is and what it holds, where the platform's accessibility tree cannot say.

**Debug-only.** In a release build the tool refuses with "available in debug builds only".

It searches the main window's content and every open popup, which holds the flyouts, menus and dialogs. It reports
only controls that show: an element with a size whose ancestors are all visible. It runs on the UI thread rather
than through the command queue, so it answers while a modal dialog holds that queue.

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
  the window's own title bar where the platform draws one. Multiply by `rasterizationScale` for physical pixels.
- `isChecked` appears only for a control that can be toggled or selected, and `value` only for one that holds a
  value, such as a text field's text.
- An empty `controls` list means nothing matched. It is not an error.

## Gotchas

- Controls come in tree order, and an open popup's controls come after the window's.
- A control scrolled out of its view still shows, so its bounds can lie outside its scroll viewer.
