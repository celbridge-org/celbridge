# Python Environment

The Python support files install themselves as the application launches, and every console inherits an
environment built from them, so the failures in this area are on disk and in a process environment rather
than on screen. Read the [README](README.md) for the invariants, evidence rules and levels.

No unit suite exercises this. The interpreter, the tool install and the REPL's own launch all happen by
running uv, which the tests do not do, so a change that stops every python console starting passes them
all. That makes level 1 here worth running before a release even when nothing Python-related changed.

## Surfaces

The application's Python support folder, the project's `.celbridge/python` folder, and the environment a
console's shell is started with.

## Cases

| Situation | Action | Expected | Level |
|---|---|---|---|
| A shell console | run `uv` and `uvx`, asking each for its version | both resolve inside the application's support folder and report the same version | 1 |
| A python console | open it, then ask `cel` for something only the application can answer | the REPL starts on an interpreter uv manages for the project, and the answer comes back naming the open project | 1 |
| The support folder, after a launch | list it | the uv binaries sit in a folder of their own, holding nothing else; the wheel and the version marker sit outside it | 2 |
| A shell console | read its environment | the uv cache, interpreter, tool and tool-bin folders all point inside the project, and the uv and wheel paths point at the installed support files | 2 |
| A second project, which has never opened a python console | open a shell console | uv and uvx resolve; `celbridge-py` does not, and that is the expected result | 2 |
| A shell console | create a virtual environment without naming a version, then create one asking for seeded packages | the first takes an interpreter uv manages for the project and never one belonging to the host; the second has a working `pip` | 2 |
| The version marker deleted and the application relaunched | open a shell console | the support folder is rebuilt and uv resolves again | 2 |
| A python console opened a second time in the same project | open it | the REPL starts from the warm cache without going to the network | 2 |
| A shell console already running | open a python console, in a project that has run one before and in a project that has not | its REPL works either way | 3 |
| A console whose own configuration names a different uv cache folder | run uv in that console, then open a python console | the typed uv follows the console's setting; the REPL still resolves from the project's cache | 3 |
| The project's `.celbridge/python` folder deleted while the application runs | open a console of each type | the folder is rebuilt and both work | 3 |
| A project whose folder path contains a space | open a shell console | every folder the environment names arrives intact, and uv resolves | 3 |
| A shell console | install a small tool with uv | it lands inside the project and runs by name with no change to PATH | 3 |
| The application's installed wheel replaced while a python console runs | open a second python console | the second REPL works, the installed tool is left as it was, and a console opened later with none running picks the new wheel up | 3 |

Run the cases in one project, in the order they are listed, and say in the report which project each ran
in. What a console does here depends on what the consoles before it did — the install is decided per
launch, against the state the last launch left — so the order is part of what is under test, and a case
run in a project of its own is a different case. Where a case needs a project of its own it says so.

Every case here is read back rather than seen. A console's startup script can write what it finds to a
file in the project, which the file tools then read: that covers the environment, the resolved paths and
the output of any command, and needs no keyboard at all. The support folder is read directly. The install
writes to the application log, which says whether a reinstall ran and whether it finished.

Have the script write that file so its presence means the case finished — under a temporary name renamed
at the end, or ending with a line the reader waits for. A script that redirects into the file directly
creates it before it has written anything, and a reader waiting for the file to appear gets a half-written
one and reads it as a case that stopped early.

A console added while the project is open has to be registered before it can be opened: refresh the file
listing first. Opening one the application has not seen raises a modal, which holds the command queue
until it is answered and reads for all the world like a hang.

The log says a launch succeeded some time before the console has one. The line naming the startup command
is written when that command is composed, not when it runs, so a console that never starts leaves a log
that reads as healthy. Judge a console by what it produced, not by the launch lines above it.

The cases that force a reinstall change state outside the project. Let a launch rebuild the support
folder, and confirm from the log that it completed before reading anything else. The case that replaces
the wheel changes it too, and that one has to be put back by hand: leaving a foreign wheel there would
follow every later project.

A python console's first launch in a fresh project downloads an interpreter, so allow for the wait and do
not read a cold launch as a failure. Allow tens of seconds, not minutes: a cold launch on a fast
connection is a matter of seconds, and one that has produced nothing after half a minute has failed
rather than slowed down. Waiting far past that buys nothing and costs the run more than every case in
this file put together. The case that asks for a warm cache is the one that would catch a cache the REPL
cannot find, and it only means anything on a second launch.

A console that resolves the wrong uv is not always the application's fault: an interactive shell sources
its profile after the console's environment is applied, and a profile that prepends a folder holding
another uv wins. Read the PATH the console actually has before calling it a defect.

## Platform differences

| | macOS | Windows packaged |
|---|---|---|
| The archive | a tarball carrying a folder to strip and per-entry metadata stubs that must not survive into the binaries' folder | a zip whose binaries are already at the top |
| Binary names | `uv`, `uvx` | `uv.exe`, `uvx.exe` |
| PATH | colon-separated | semicolon-separated, and the variable's name may be spelled in any case — a console that spells it `Path` keeps its entries and gains the uv folders |
| The host's own Python | an Xcode interpreter is usually present, and a bare virtual environment must never pick it | a Store alias may stand in for `python`, and must likewise never be picked |
| Reinstalling while a console runs | files delete while in use | an open file can block the delete, and the failure says so rather than leaving a half-built folder |

## Not covered

The REPL's own behaviour and the `cel` API, which the MCP tools exercise. The contents of the wheel, which
the unit tests cover. Linux, which ships no build.
