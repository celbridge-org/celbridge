#!/usr/bin/env python3
"""Build Celbridge packages and copy wheels to Celbridge.Python Assets folder."""

import shutil
import subprocess
import sys
from pathlib import Path

# The lowest version the celbridge package declares in pyproject.toml. pip refuses to build the wheel
# with anything older, and the interpreter running this script is not always the newest one installed
# (on macOS it is usually the system Python), so a newer one is looked up when this one is too old.
MINIMUM_PYTHON = (3, 10)

CANDIDATE_INTERPRETERS = [
    "python3.13",
    "python3.12",
    "python3.11",
    "python3.10",
    "python3",
    "python",
]


def is_supported_interpreter(interpreter):
    """Whether the interpreter at the given path is new enough to build the package."""
    major, minor = MINIMUM_PYTHON
    check = f"import sys; sys.exit(0 if sys.version_info >= ({major}, {minor}) else 1)"

    try:
        result = subprocess.run([interpreter, "-c", check], capture_output=True)
    except OSError:
        return False

    return result.returncode == 0


def find_interpreter():
    """Return the path of an interpreter new enough to build the package, or None if there is none."""
    if sys.version_info >= MINIMUM_PYTHON:
        return sys.executable

    for name in CANDIDATE_INTERPRETERS:
        interpreter = shutil.which(name)
        if interpreter is None:
            continue

        if is_supported_interpreter(interpreter):
            return interpreter

    return None


def build_wheel(interpreter, pkg_dir):
    """Build a wheel for the given package."""
    dist = pkg_dir / "dist"
    shutil.rmtree(dist, ignore_errors=True)
    shutil.rmtree(pkg_dir / "build", ignore_errors=True)

    subprocess.run(
        [interpreter, "-m", "pip", "wheel", "--no-deps", str(pkg_dir), "-w", str(dist)],
        check=True,
    )
    return list(dist.glob("*.whl"))[0]


def main():
    interpreter = find_interpreter()
    if interpreter is None:
        major, minor = MINIMUM_PYTHON
        print(
            f"No Python {major}.{minor} or later found on PATH, which the celbridge package requires "
            f"to build. Running Python is {sys.version.split()[0]}.",
            file=sys.stderr,
        )
        return 1

    root = Path(__file__).parent
    packages = [root / "packages/celbridge"]
    assets = root / "Assets/Python"

    # Flushed because pip writes straight to the same stream, so an unflushed line lands after its output.
    print(f"Building wheels with {interpreter}...", flush=True)
    wheels = [build_wheel(interpreter, pkg) for pkg in packages]
    
    print(f"\nCopying to {assets.name}/...")
    assets.mkdir(parents=True, exist_ok=True)
    for old in assets.glob("*.whl"):
        old.unlink()
    
    for whl in wheels:
        shutil.copy2(whl, assets)
        print(f"  {whl.name}")

    # Clean up build artifacts
    for pkg in packages:
        shutil.rmtree(pkg / "dist", ignore_errors=True)
        shutil.rmtree(pkg / "build", ignore_errors=True)
    
    print("\nDone!")
    return 0


if __name__ == "__main__":
    sys.exit(main())
