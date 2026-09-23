# Notification Centre

The badge beside the Project Switcher and the list it opens hold every notification the application raises
while a project is loaded. The list opens over the documents and the console, so its buttons are the
application's own controls drawn above hosted web surfaces. Read the [README](README.md) for the
invariants, evidence rules and levels.

## Surfaces

The badge, its tooltip and count, and the list: its rows, each row's action and dismiss buttons, and Clear
All.

## Cases

| Situation | Action | Expected | Level |
|---|---|---|---|
| A project whose load found issues, with nothing else pending | click the badge, then the row's View Report | the list opens holding the load's condition alone, then the load report opens and the condition stays pending | 1 |
| An event and the load's condition pending, the list reaching over a hosted web surface | click the event's dismiss button | the event leaves the list, the condition stays, the list stays open on what remains, and the surface underneath neither receives the click nor takes focus | 1 |
| Several notifications pending, the list reaching over a hosted web surface | click a row's action | the document the action names opens, the list closes, the notification is still pending, and the surface the list was covering takes no focus | 2 |
| No condition pending, only events | click Clear All | the list closes, the badge goes with it, and nothing is left open over the documents | 2 |
| The load's condition and several events pending | click Clear All | the events leave, the condition stays, and the list stays open | 2 |
| The badge collapsed | raise a notification | the badge appears with a count of one, and its tooltip reads the notification's line | 2 |
| The badge showing | raise several different notifications in quick succession | the count rises by the number raised | 2 |
| The list open, keyboard on a row's dismiss button | press Space | the row goes and the keyboard lands on the next row's dismiss button, still inside the list | 3 |
| The list open | click outside it, on a document | the list closes and the document takes the click as it would with no list open | 3 |
| An editor raising the same notification repeatedly | open the list | one row, stating how many times it happened | 3 |
| Packaged Windows head, badge collapsed | raise notifications until the count reaches two digits, then Clear All | the badge takes clicks at every width, and once it has gone the spot it occupied drags the window | 3 |

The list is not tall enough to reach the console at a normal window size, so "over a hosted web surface"
means a document: the load report the first case opens, or a code or spreadsheet document. On macOS those
are native views above the application's own drawing, which is the arrangement these cases exist to test.

A web surface under the list that takes a click also reports focus in the app log, so check the log as well
as the screen for every case that clicks over one. That is the failure this surface exists to avoid, and on
macOS it can be present while the screen looks right.

A load finds issues when the project file carries an entry the parser has to skip, such as a feature flag
set to something other than true or false. An event needs a failure: deleting a file another process holds
open fails on Windows, and an editor raises one through the `dialog.showNotification` call of the client it imports,
which `webview_eval` can make from inside a contribution document.

## By hand

The badge's arrival animation. An agent samples the screen far too slowly to catch it, and a run that
tries reports whichever still frame it happened to get.

- With the badge collapsed, raise one notification: it should flash **once** as it appears.
- With the badge showing, raise several notifications within a second or so: **one** flash for the lot,
  not one per notification.
- Clear All with only events pending: the list should close first and the badge collapse after it, rather
  than the badge vanishing out from under an open list.

## Not covered

The wording of individual notifications, and whether each resource operation raises one. Both are covered
by unit tests.

## Platform

The title bar drag region is only carved out on the packaged Windows head, so the last case applies there
alone. The cases about a click reaching the surface under the list matter most on macOS, where a hosted
web view is a native view above the application's own drawing.
