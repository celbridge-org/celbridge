# Python Environment

Celbridge runs Python through [uv](https://docs.astral.sh/uv/). Nothing is taken from the machine: uv
itself, the interpreter, the `celbridge` package and every dependency are installed by the application
into its own folders. This document explains what is installed where and why, the three places Python
code is loaded from at runtime, what makes an edited Python source reach a running REPL, and where to
look when it does not.

For the `celbridge` package itself — its layout, tests and wheel build — see the
[module README](../../Source/Workspace/Celbridge.Python/README.md).

## The rule the design serves

**There is one install, and one thing that decides whether it runs.** The application installs uv, the
wheel and the `celbridge-py` tool into a single folder, gated by a version marker holding the app version
and a hash of the bundled wheel. A console does no install work; it builds an environment and launches.

Two corollaries follow, and most of the layout below exists to serve them:

- **The marker must mean "the code changed"**, never "somebody built". That is why the wheel build pins
  `SOURCE_DATE_EPOCH` and the setuptools version — without them two builds of identical sources differ,
  and every build would reinstall the shared folder.
- **A reinstall must be cheap**, because a developer triggers one on every Python source change. That is
  why the things a reinstall does not describe — uv's download cache, and the interpreters uv manages —
  live outside the folder it deletes.

## Where things live

| Location | Holds | Lifetime |
|---|---|---|
| `<app data>/Python/` | `bin/` (uv, uvx), the wheel, `uv_tools/` (the celbridge-py environment), `uv_bin/` (the celbridge-py command), `installed_version.txt` | Deleted and rebuilt whenever the marker mismatches |
| `<app data>/PythonCache/` | `uv_cache/`, `uv_python_installs/` | Never deleted by an install, and never safe to delete by hand. Shared by the tool and by every project |
| `<project>/.celbridge/python/` | `ipython/` (the profile), `uv_tools/` and `uv_bin/` (tools the **user** installs in this project) | Belongs to the project; safe to delete at any time |

`<app data>` is `ApplicationData.Current.LocalFolder` on packaged Windows, which the OS removes on
uninstall, and `~/Library/Application Support/Celbridge/` elsewhere.

The cache and the interpreter store are shared rather than per-project. They hold downloaded artifacts
addressed by content and by interpreter version — nothing a project owns — and each REPL still gets its
own environment from the inner `uv run`, so what a project imports is unaffected. Per-project copies were
how these were once kept safe from a reinstall that deleted the whole folder. The folder split does that
now, at a measured 50 MB of interpreter and 99–256 MB of cache saved per project.

Shared does not mean disposable. The installed `celbridge-py` runs on an interpreter in
`uv_python_installs/`, and every REPL launch resolves its environment out of `uv_cache/`, so emptying
either breaks a working install. The install checks that interpreter before treating a matching marker as
current, and reinstalls when it has gone.

`UV_TOOL_DIR` and `UV_TOOL_BIN_DIR` still point into the project. They are the user's: a `uv tool install`
typed in a console lands in the project rather than on the machine.

## Reclaiming the disk

Every full reinstall leaves behind a copy of the environment the REPL imports, at roughly 50 MB each, and
nothing reclaims them. The install rewrites the wheel into the support folder, which moves its timestamp,
so the next python console resolves a fresh cache archive and the archive it replaced stays. Identical
bytes do not save it: a marker deleted by hand costs a copy as surely as a rebuilt wheel does. The cost
also arrives late — the reinstall itself adds a couple of hundred kilobytes, and the next python console
pays the rest.

A republish costs nothing here, because it leaves the wheel where it is.

An upgrade costs one copy, which nobody notices. A wheel rebuilt during development costs one each, so a
development machine accumulates them fastest.

Removing the application does not clear them on the Skia heads, where the folder is ordinary user data.
The packaged Windows head is the exception: its folder belongs to the MSIX package, which the OS deletes
on uninstall.

To reclaim the space, close Celbridge and delete the cache:

```
rm -rf ~/Library/Application\ Support/Celbridge/PythonCache/uv_cache
```

The next python console rebuilds what it needs in a few seconds. Leave `uv_python_installs` beside it
alone unless the disk is desperate, because it holds the interpreters and they are the slower download.
Deleting that is recoverable too — an install that finds its interpreter gone republishes the tool on the
next launch — but it costs more to undo.

`uv cache prune` is not a lighter alternative. Despite the name it empties the cache rather than trimming
it.

## The three places Python code loads from

This is the part that surprises people, and the reason "I changed a script and nothing happened" is
usually a question about *which* of these was stale.

1. **The tool environment**, `Python/uv_tools/celbridge/`. Refreshed by the install. A python console
   passes launch options, which make `__main__.py` re-exec through uv, so this environment only runs the
   shim. A bare `celbridge-py` typed in a shell console passes none, does not re-exec, and runs IPython
   and the `celbridge` package from here.
2. **The uv cache archive**, `PythonCache/uv_cache/archive-v0/<id>/`. This is what the REPL actually
   imports. The shim re-execs `uv run --with <wheel>`, and uv revalidates that wheel and builds a new
   archive whenever the file's timestamp moves — not when its bytes do, which is why a reinstall that
   rewrites an identical wheel still sends the next console to a fresh archive. A running REPL reports its
   own module path here, not in the tool environment.
3. **The installed wheel**, `Python/celbridge-<version>.whl`. The source both of the above are built
   from, and the file the marker hashes.

A console finds the first two through the shared environment: `CELBRIDGE_UV` names the uv to re-exec
through, `CELBRIDGE_WHEEL` the wheel to inject, and `CELBRIDGE_UV_CACHE_DIR` the cache to hold it to —
passed as an explicit `--cache-dir`, so it outranks any `UV_CACHE_DIR` a console or shell profile sets.

## The development cycle

Edit a Python source, build, relaunch, open a console. Three links have to hold, and the middle one is
the only one the application owns:

| Link | What carries it |
|---|---|
| The rebuilt wheel reaches the app folder | the marker mismatches, so the install runs |
| The shim is rebuilt from it | that same install |
| The REPL imports the new code | uv revalidating `--with <wheel>` into a fresh cache archive |

Measured on macOS: 11 s to build, 3 s to install, and the new code is in the REPL — confirmed through a
sentinel value and three distinct cache archive ids across three iterations. A first install on a machine
takes about 9.8 s because it downloads an interpreter and the tool's packages; every later one is about
3 s because those stay in `PythonCache`.

## Console PATH precedence

`BuildConsolePath` prepends, in order, the project's tool bin folder, then the application's tool bin
folder, then the application's uv bin folder — so the application's folders are searched first.

That order is deliberate. A project opened by an earlier release still holds the `celbridge-py` its
per-project install published, and the stale shim would otherwise be found ahead of the installed one.
More generally, the application's own commands should not be shadowable by project content.

An interactive shell sources its profile *after* this is applied, so a profile that prepends a folder
holding another uv still wins. A console resolving the wrong uv is not automatically a defect — read the
PATH the console actually has first.

## The build

Both bundled assets are generated rather than committed, and both are gitignored: the uv release archive
under `Assets/UV/` and the wheel under `Assets/Python/`. `Celbridge.Python.Assets` produces them.
`build.py` runs `uv build` using the uv that project downloads, so building needs no Python on the
machine. A failed build fails the build.

That is a separate project because `Celbridge.Python` multi-targets and its inner builds run in parallel,
which had several of them downloading and extracting uv, and running `build.py`, over the same files at
the same time. `Celbridge.Python.Assets` has one target framework, so it runs once however many
frameworks reference it — the same shape as `Celbridge.Templates`.

A build that wants the assembly and not the assets opts out with `-p:SkipPythonAssets=true`, which CI's
unit test job passes because no test reads either asset. The build then warns that its output cannot run
Python. Anything that has to launch leaves the property unset, and a missing asset fails the build rather
than surfacing at the first console launch.

## Investigating

Everything here is read back rather than seen; none of it needs a keyboard.

- **The install** writes to the application log. `Python reinstall required` names which half of the
  marker mismatched, and `celbridge tool installed successfully in <n>ms` says it finished. A launch line
  is written when the command is *composed*, not when it runs, so a console that never started can leave
  a log that reads as healthy — judge a console by what it produced.
- **Whether a REPL reached the network** is in the log too. `celbridge-py` measures the cache before it
  bootstraps and reports what it found, which arrives as `Console '<resource>' reported: python-probe
  mode=offline ms=<n>`. `offline` means every package resolved without touching the network, so a launch
  that went online says so rather than leaving the question to a stopwatch.
- **The environment** is read by giving a console a startup script that writes what it finds to a file in
  the project. Write it under a temporary name and rename it at the end, so its presence means the case
  finished rather than started.
- **The folders** are read directly, and are usually enough on their own: a project holding a `uv_cache`
  or `uv_python_installs` means something is still scoping them per-project.

The [Python Environment agent test plan](agent_tests/python_environment.md) covers this area case by
case. No unit suite reaches it — the interpreter, the tool install and the REPL's launch all happen by
running uv — so a change that stops every python console starting passes the whole .NET suite.
