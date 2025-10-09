#!/usr/bin/env python3
"""Build Celbridge packages and copy wheels to Celbridge.Python Assets folder."""

import shutil
import subprocess
import uuid
from pathlib import Path


def build_wheel(pkg_dir):
    """Build a wheel for the given package."""
    dist = pkg_dir / "dist"
    shutil.rmtree(dist, ignore_errors=True)
    shutil.rmtree(pkg_dir / "build", ignore_errors=True)
    
    subprocess.run(
        ["python", "-m", "pip", "wheel", "--no-deps", str(pkg_dir), "-w", str(dist)],
        check=True, capture_output=True
    )
    return list(dist.glob("*.whl"))[0]


def main():
    root = Path(__file__).parent
    packages = [root / "packages/celbridge", root / "packages/celbridge_host"]
    assets = root.parent / "app/Workspace/Celbridge.Python/Assets/Python"
    
    print("🔨 Building wheels...")
    wheels = [build_wheel(pkg) for pkg in packages]
    
    print(f"\n📋 Copying to {assets.name}/...")
    assets.mkdir(parents=True, exist_ok=True)
    for old in assets.glob("*.whl"):
        old.unlink()
    
    for whl in wheels:
        shutil.copy2(whl, assets)
        print(f"  ✅ {whl.name}")
    
    # Write build version file with GUID
    build_id = str(uuid.uuid4())
    version_file = assets / "build_version.txt"
    version_file.write_text(build_id, encoding="utf-8")
    print(f"  📝 build_version.txt ({build_id})")
    
    # Clean up build artifacts
    for pkg in packages:
        shutil.rmtree(pkg / "dist", ignore_errors=True)
        shutil.rmtree(pkg / "build", ignore_errors=True)
    
    print("\n✨ Done!")


if __name__ == "__main__":
    main()
