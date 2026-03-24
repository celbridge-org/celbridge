"""Run all Python tests across all packages."""

import subprocess
import sys
from pathlib import Path

packages_folder = Path(__file__).parent / "packages"

test_packages = [
    package
    for package in sorted(packages_folder.iterdir())
    if package.is_dir() and (package / "pyproject.toml").exists()
]

if not test_packages:
    print("No packages found with pyproject.toml")
    sys.exit(1)

failed_packages = []

for package in test_packages:
    tests_folder = package / "tests"
    if not tests_folder.exists():
        continue

    print(f"\n{'=' * 60}")
    print(f"Running tests for: {package.name}")
    print(f"{'=' * 60}\n")

    result = subprocess.run(
        [sys.executable, "-m", "pytest", "tests", "-v"],
        cwd=str(package),
    )

    if result.returncode != 0:
        failed_packages.append(package.name)

print(f"\n{'=' * 60}")

if failed_packages:
    print(f"FAILED: {', '.join(failed_packages)}")
    sys.exit(1)
else:
    print(f"All tests passed ({len(test_packages)} package(s))")
    sys.exit(0)
