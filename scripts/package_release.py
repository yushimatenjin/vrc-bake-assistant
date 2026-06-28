#!/usr/bin/env python3
import hashlib
import json
import stat
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PACKAGE_ID = "com.yushimatenjin.vrc-bake-assistant"
PACKAGE_DIR = ROOT / "Packages" / PACKAGE_ID
DIST_DIR = ROOT / "Dist"

# ZIPのmtimeが変わるとSHA256も変わるため、固定時刻で再現性のあるzipを作ります。
# ZIP仕様上の最小日付は1980年です。
FIXED_ZIP_TIME = (1980, 1, 1, 0, 0, 0)

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
        if not path.is_file():
            continue
        rel = path.relative_to(PACKAGE_DIR).as_posix()
        info = zipfile.ZipInfo(rel, FIXED_ZIP_TIME)
        info.compress_type = zipfile.ZIP_DEFLATED
        # 通常ファイル 0644。実行権限差分でSHAが変わるのを避けます。
        info.external_attr = (stat.S_IFREG | 0o644) << 16
        data = path.read_bytes()
        zf.writestr(info, data)

sha256 = hashlib.sha256(zip_path.read_bytes()).hexdigest()
print(f"Wrote {zip_path}")
print(f"SHA256 {sha256}")
