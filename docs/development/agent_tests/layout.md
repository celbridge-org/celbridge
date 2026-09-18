# Layout

The arrangement of the workspace: which areas are showing, the Focus and Presentation modes, Reset Layout,
and the controls that change them. The areas are the Utility Panel and the three document areas, Main,
Bottom and Side. Read the [README](README.md) for the invariants, evidence rules and levels.

## Surfaces

The title bar's area buttons and layout menu, the View menu, each collapsible area's close button, the
Utility buttons and the ones document shortcuts add, double-clicking a document tab, and `document_open`.

## Cases

| Situation | Action | Expected | Level |
|---|---|---|---|
| A new project made from the Empty template | open it | the Utility Panel and Main show, and Bottom and Side are collapsed | 1 |
| Bottom collapsed | show it with the title bar's Bottom button, then open the View menu | Bottom shows, and the View menu marks it as showing | 1 |
| Bottom collapsed, with a shortcut into Bottom whose document is closed | click the shortcut's button | Bottom shows with the document active, and the keyboard is in the document | 2 |
| Bottom and Side showing, a Bottom tab active | double-click the tab, then double-click it again | Focus shows the Bottom area alone, and leaving it brings back every area as it was | 2 |
| Focus showing the Bottom area | open another Bottom document from its shortcut | the document opens beside the others, Focus stays on, and `app_get_state` reports `bottom` alone on screen | 2 |
| Focus showing the Bottom area | open a Side document from its shortcut | Focus ends, and the normal layout returns with Side showing the document | 2 |
| Tabs in Bottom and none in Side, Bottom hidden and Side shown by hand, the window maximised | Reset Layout | Bottom shows, Side collapses, and the window is restored from maximised | 2 |
| An area hidden by hand | reload the project | the area is still hidden | 2 |
| Presentation on | open a Side document with `document_open` without activating it | Presentation stays on and nothing on screen changes | 2 |
| Focus showing the Bottom area, documents open in other areas | close Bottom's last tab | another document is on screen, and no empty area is left in view | 3 |
| Presentation on | open a Side document with `document_open` and activate it | Presentation ends and Side shows the document | 3 |
| Side showing | collapse it with its own close button | the title bar's Side button and the View menu both show it hidden | 3 |
| The Utility Panel showing Explorer, the keyboard in the tree | click Explorer's Utility button, then click it again | the first click collapses the panel and moves the keyboard to the active document, and the second brings the panel back on Explorer | 3 |
| An area split into two sections | close the last tab in one of them | the area folds back to a single section | 3 |
| Bottom showing | choose each Bottom alignment in turn, then reload the project | Bottom runs under the areas each alignment names, and the last choice survives the reload | 3 |
| Focus on | turn full screen on, leave Focus, then turn full screen off | each change leaves the other alone: the window stays full screen when Focus ends, and the layout stays normal when full screen ends | 3 |

The cases need document shortcuts into Bottom and Side. A `[[shortcut]]` entry in the project file names a
resource and the `area` it opens in, and adds a Utility button that opens it. Two into Bottom and one into
Side cover every case.

Read the areas back rather than trusting the screen: `app_get_state` reports which areas are on screen, and
`document_get_state` which sections. In Focus and Presentation the title bar's area buttons and the View
menu show every area off, even while the mode fills the screen with Bottom or Side. They describe the
normal layout the mode returns to rather than what is on screen, so that is correct.

## Not covered

Area sizes: dragging the splitters, the sizes a reload restores, and the minimum sizes the areas keep.
Synthetic drags are unreliable, and unit tests cover the minimums.

The Utility Panel's own content, Explorer and Search, belongs to the [Workspace](workspace.md) plan.

## Platform

On macOS the layout menu has no full screen toggle. Full screen is the native one, reached from the View
menu or the window's title bar, so the full screen case runs by that route there.
