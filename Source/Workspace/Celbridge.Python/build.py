#!/usr/bin/env python3
"""Build the celbridge wheel and copy it to the Celbridge.Python Assets folder."""

import os
import shutil
import subprocess
import sys
from pathlib import Path

# Zip entries record when each file was packaged, so two builds of identical sources differ byte for byte,
# and the application's version marker hashes the wheel. Without this pin every build reinstalls the shared
# Python folder. pyproject.toml pins the build backend, the other half. The value itself is arbitrary.
WHEEL_TIMESTAMP = "1704067200"  # 2024-01-01T00:00:00Z


def main():
    # uv supplies the interpreter and the build backend, so building needs no Python on the machine. The
    # project build passes the path of the uv it downloads. A hand-run takes whichever uv is on PATH.
    uv = sys.argv[1] if len(sys.argv) > 1 else "uv"

    root = Path(__file__).parent
    package = root / "packages/celbridge"
    assets = root / "Assets/Python"
    staging = root / "obj/wheel"

    # Assigned rather than defaulted, because an ambient value would move the wheel's hash and with it the
    # marker that decides whether the application reinstalls.
    os.environ["SOURCE_DATE_EPOCH"] = WHEEL_TIMESTAMP

    # setuptools stages the package in build/lib and only ever copies files into it, so a source deleted
    # since the last build would still be packaged from there.
    shutil.rmtree(package / "build", ignore_errors=True)

    # Built into obj first so that a failed build leaves the previous wheel in place. It is the only copy
    # once it stops being committed, and nothing else can restore it.
    shutil.rmtree(staging, ignore_errors=True)
    staging.mkdir(parents=True, exist_ok=True)

    # Flushed because uv writes straight to the same stream, so an unflushed line lands after its output.
    print(f"Building the celbridge wheel with {uv}...", flush=True)
    try:
        # The interpreter is left to uv, which honours the package's own requires-python. The wheel is
        # py3-none-any, so which one it picks does not reach the output.
        subprocess.run(
            [
                uv, "build", "--wheel", "--managed-python", "--no-create-gitignore",
                str(package), "--out-dir", str(staging),
            ],
            check=True,
        )

        # The assets folder holds the wheel beside files the build does not own, so the old wheel is
        # removed by name rather than by clearing the folder.
        assets.mkdir(parents=True, exist_ok=True)
        for old in assets.glob("*.whl"):
            old.unlink(missing_ok=True)
        for built in staging.glob("*.whl"):
            shutil.move(str(built), assets / built.name)
    finally:
        shutil.rmtree(staging, ignore_errors=True)
        shutil.rmtree(package / "build", ignore_errors=True)

    print("\nDone!")
    return 0


if __name__ == "__main__":
    sys.exit(main())
