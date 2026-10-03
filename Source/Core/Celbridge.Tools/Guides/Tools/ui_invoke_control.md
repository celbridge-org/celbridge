# ui_invoke_control

Performs the default action of one of the application's own controls, the way assistive technology does. It is for
a test harness that needs a step done whose input route is not what the test is about, such as showing an area
before the step under test.

**Debug-only.** In a release build the tool refuses with "available in debug builds only".

It takes the same `automationId`, `name` and `controlType` as `ui_find_controls`, and acts on the first showing,
enabled control that matches and has a default action. On macOS that includes the menu bar's items and the window's
buttons, and on Windows the caption buttons.

## Returns

The control it acted on, described as `ui_find_controls` describes it, and the action it performed:

```json
{
  "control": {"automationId": "side-area-toggle-button", "name": "Toggle Right Panel", "controlType": "Button", ...},
  "action": "Invoke"
}
```

The action is the first the control supports, in this order: `Invoke` presses a button or a menu item, `Toggle`
flips a toggle, `Expand` opens a submenu or a drop-down, and `Select` selects an item in a list.

A menu bar item's action is `Invoke`, which chooses the item as a click on it would. An item that opens a submenu
has no default action. A window button's action, or a caption button's, is `Invoke`, which presses it.

## Gotchas

- The call fails when no showing, enabled control matches, or none that matches has a default action.
- The call does not wait for what the action starts. A modal dialog the action opens then holds the command queue.
- Pressing the window's close button closes the window, which quits the application.
