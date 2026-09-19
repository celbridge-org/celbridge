# Agent Tests

Test plans written for a coding agent to execute by driving the real application, for behaviour that only
appears in a running app on a specific platform: keyboard routing, focus, native menus, and the
interaction between hosted web surfaces and the host.

These are not part of CI. CI has no display and no way to press a key, and the failures these plans catch
are exactly the ones that survive a green unit test run. A human asks an agent to run one, reads the
report, and decides what to do about it.

Each plan covers one area of the application and is a single file. How to build, launch, drive and
inspect the app is not written down here: it is discoverable from the project, and a copy kept in prose
goes stale faster than anyone notices.

| Plan | Area |
|---|---|
| [Code Editor](code_editor.md) | Markdown and code documents, their preview and find bars |
| [Spreadsheet](spreadsheet.md) | The grid, its cell editor, and the Designer's chrome and dialogs |
| [Console](console.md) | The terminal and its settings form |
| [Notes](notes.md) | Rich text notes |
| [Notification Centre](notification_centre.md) | The notification badge and the list it opens over the documents |
| [Web Documents](web_documents.md) | `.webview` and `.html` documents |
| [Workspace](workspace.md) | Explorer, Search, dialogs, menus and project settings |
| [Layout](layout.md) | The areas on screen, Focus and Presentation, and Reset Layout |
| [Python Environment](python_environment.md) | The uv install, the Celbridge wheel, and the environment consoles inherit |

## Effort levels

Every case carries a level, and running at level N means running every case at N and below. The level is
also the case's value ranking, so each plan lists its cases in level order and the broadest, most-used
paths come first.

| Level | Scope | Size |
|---|---|---|
| 1 | Smoke: is this area working at all | one or two cases |
| 2 | The everyday paths, and the places defects have actually appeared | five to eight cases, 5-10 minutes |
| 3 | Lesser-used controls, unusual sequences, and combinations that have never been exercised | the whole plan |

Ask for the level that matches the moment: 2 before a routine commit, 3 before a release or after touching
input, focus or menu routing. Level 1 earns its keep across areas rather than within one — most of a short
run's cost is fixed setup, so running level 1 on a single plan saves little, while running it on every plan
sanity-checks the whole application for about the cost of one level 2.

The level sets how much is covered, never how carefully. A level 1 case is held to the same standard of
evidence as a level 3 one.

Run the whole selected level rather than stopping at the first failure. One bug per run is a poor return
on an expensive test.

## Invariants

The rules every plan is testing. They are stated once here so that no plan restates them and they cannot
drift apart.

**An edit verb acts on the control that holds the keyboard, and on nothing else.** Most documents contain
several places to type — an editor and its find bar, a terminal and its settings form, a grid and its
dialogs. Every case therefore has two halves: the focused control received the edit, **and** the surface
behind it did not. Checking only the first half passes while the bug is present, which is how several of
these shipped.

**Caret motion uses the platform's own chords.** A text surface has to answer the chords the platform's
users actually press, not only the ones its toolkit came with. On macOS that means Command+Left and
Command+Right for the ends of a line, and Command+Up and Command+Down for the ends of the document; End
and Home exist but are bound to scrolling, so a caret that stays put on those two is correct there and a
caret that stays put on the Command chords is not. A surface that hosts a web page inherits this from the
page; the application's own fields have to be checked, because they are where it has broken.

**Tab belongs to whoever holds the keyboard.** In a form it moves to the next field. In a surface that
acts on Tab itself it does that instead: the cell advances, the line indents, the shell completes.

**The same verb reached two ways agrees with itself.** A keyboard shortcut, the application's Edit menu
and a context menu item are three routes to one outcome. Where they disagree, at least one is routing
wrongly, so spot-check a second route whenever a case looks suspicious.

## Running a plan

Work in a throwaway project created for the run, holding one document of each type the plan needs, made
through the application's own new-document flow rather than copied from anywhere that matters. Plans type
into documents and change settings.

Anything changed outside the project — the setting that decides which project opens, window state, the
clipboard, and anything a plan deliberately mutates in the application's own data folder — is yours to put
back, and the app should not be left running.

Driving the app needs the computer-use tools, which require the user's permission at the start of the
session. Real key presses are the point: a shortcut delivered any other way tests a path a user never
takes. Everything else — opening documents, reading page state, inspecting the log — has cheaper and more
reliable routes that the project's own tooling provides.

Two things a run reliably trips over:

**Escape may not arrive.** Desktop automation reports success for Escape and can deliver nothing, because
computer use keeps Escape as its own stop key: an Escape it sees stops the run rather than reaching the
app. Several
cases turn on it, so send it with `app_simulate_input` instead and treat a missing Escape as a limit of the
harness, not a defect — unless an equivalent route shows the app is at fault.

**A modal dialog holds the command queue.** Every queued tool waits until the dialog is answered, so a run
that raises one unexpectedly appears to hang. Answer it — `Escape` cancels and `Return` accepts through
`app_simulate_input`, which runs outside the queue — or schedule `app_answer_dialog` before the step that
raises it.

## Evidence

Most of the cost of a run is spent telling a real failure from a bad observation. Three rules, each of
which has produced a wrong verdict before:

**Read state back; do not trust the screen.** A hosted web view can present a stale frame while its DOM is
perfectly healthy, so a screenshot can show the previous layout, the wrong theme, or a control that is not
really there. Assert on values read out of the page, the contents of a file, the clipboard, or the app
log. Use a screenshot to find something, not to prove something.

The application's own chrome — find bar, address bar, Search field, dialog fields — is the awkward case:
it is neither in the accessibility tree nor reachable by `webview_eval`. Read one of those fields by
putting a sentinel on the clipboard, then selecting all and copying **in the field**: the clipboard now
holds the field's text, and a sentinel that survived means the copy never happened. The menu bar is
readable directly — asking to press a menu item reports whether it is enabled, which beats reading a
greyed label off a screenshot.

**Confirm the precondition separately from the result.** A shortcut that "does nothing" usually means the
click before it did not land, the surface never took focus, or the clipboard held something other than
what you expected. Establish and verify the starting state, then act, then read the result. When a case
fails, check the precondition again before reporting it.

An action that appears to have done nothing deserves a screenshot before it is written down as a
negative. Some read tools answer while a dialog is open, so they report the state from before the action
and read as "nothing happened" when what actually happened is a dialog waiting for an answer.

**Distinguish "the app is wrong" from "the input never arrived".** Synthetic input is not perfectly
reliable on every head: clicks can register as hover, and a control that ignores one can be perfectly
healthy. Before reporting a negative, show that an equivalent action through a different route does work.

Move the pointer onto a control and press it in two separate calls, never as one batched sequence. A
move and a press sent together do not activate some controls on a hosted page, and they fail
selectively — a formatting button in a toolbar answers while the button beside it, which opens a
popover, does not, and the dead one can be shown receiving a full trusted click sequence. Settling the
pointer first makes both work. This is the most productive source of false failures a run has, because
the evidence for the false one looks conclusive.

**An outcome you can only see while it happens is not an agent's to check.** A flash, a fade, the order
two things disappear in: an agent samples the screen far too slowly to catch any of them, and several
identical captures of an animation that already finished read as proof it never ran. Plans keep those
checks in a section of their own for a person to run (see [Writing a plan](#writing-a-plan)); a run
neither attempts them nor counts them as cases it could not run.

## The report

Write a summary to the scratch project folder and give the user its path. Name the plan, the level run and
the platform, so two runs can be compared. Record per case the verdict and the observation it rests on, so
a reader can tell a checked pass from an assumed one. Cases that could not be run are their own outcome
and must say why; a plan that only lists passes is not a result. Note anything that behaved correctly but
looked wrong, and anything the plan does not cover.

Report failures rather than filtering them against known issues. Deciding what is already known is the
reader's job, and a list of known failures kept in these files would be stale within a week.

End with any improvements the run suggests to the plan itself: a case whose expected outcome turned out to
be ambiguous, a gap the run walked past, a case that no longer earns its place, or setup that was harder
than it needed to be. Only genuinely useful ones — an empty section is a better result than a padded one.
The report is usually the input to the next piece of work, so a finding and the change to the plan that
would have caught it sooner both belong in it.

## The reply

The report is the record. The reply at the end of a run is for deciding what happens next, so it should
be quick to read and quick to answer. It has two parts:

1. **Failures.** For each case that failed or could not be run, a short summary: what was expected, what
   happened, and the evidence, in a line or two. When every case passed, say so in one line.
2. **Proposed changes.** A numbered list, one change per item, so each can be approved or declined by its
   number. It covers fixes for what failed, corrections to documentation the run contradicted, and
   improvements to the plan. Each item says what it changes, where, and why, in a sentence or two. Make
   none of them until they are approved.

Then give the report's path. Passes, setup notes and everything else stay in the report. The exception is
a change outside the project that the run could not put back, which belongs in the reply too.

## Writing a plan

One file per area, listing the surfaces in scope and the cases as a table of *situation, action, expected
outcome, level*, where the outcome is something observable. Write the expectation, not the mechanism: a
plan should survive the implementation changing underneath it. List the cases in level order, so the table
reads as the priority list the levels already make it.

Prefer cases that have failed before, and pick a representative set rather than an exhaustive one — a rich
third-party surface has more controls than anyone will ever check, and a plan that tries to name them all
is both unfinishable and out of date. Add specific cases later when a bug report justifies one.

Every case in the table is an agent's to run. An expectation an agent cannot observe — an animation, or
the order two changes land in — goes in a **By hand** section instead, as a line saying what to do and
what to look for. Keeping them out of the table is what stops a run spending its budget photographing an
animation and reporting the still it caught.

State what the plan does not cover. Where platforms are expected to differ, say so and say why, so a
difference found on one platform is not mistaken for a defect.
