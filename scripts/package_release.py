#!/usr/bin/env python3
import hashlib
import json
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PACKAGE_ID = "com.yushimatenjin.vrc-bake-assistant"
PACKAGE_DIR = ROOT / "Packages" / PACKAGE_ID
DIST_DIR = ROOT / "Dist"

manifest_path = PACKAGE_DIR / "package.json"
if not manifest_path.exists():
    raise FileNotFoundError(f"package.json not found: {manifest_path}")

manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
name = manifest["name"]
version = manifest["version"]
zip_name = f"{name}-{version}.zip"
zip_path = DIST_DIR / zip_name

DIST_DIR.mkdir(exist_ok=True)
for old_zip in DIST_DIR.glob(f"{name}-*.zip"):
    old_zip.unlink()

with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as zf:
    for path in sorted(PACKAGE_DIR.rglob("*")):
        if path.is_file():
            zf.write(path, path.relative_to(PACKAGE_DIR).as_posix())

sha256 = hashlib.sha256(zip_path.read_bytes()).hexdigest()
print(f"Wrote {zip_path}")
print(f"SHA256 {sha256}")
