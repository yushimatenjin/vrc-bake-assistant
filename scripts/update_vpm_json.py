#!/usr/bin/env python3
import json
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PACKAGE_ID = "com.yushimatenjin.vrc-bake-assistant"
PACKAGE_DIR = ROOT / "Packages" / PACKAGE_ID
WEBSITE_DIR = ROOT / "Website"
DIST_DIR = ROOT / "Dist"
GITHUB_OWNER = "yushimatenjin"
REPO_NAME = "vrc-bake-assistant"
DISTRIBUTION_REPO_NAME = "yushimatenjin-vpm"
DISTRIBUTION_BASE_URL = f"https://{GITHUB_OWNER}.github.io/{DISTRIBUTION_REPO_NAME}"

manifest = json.loads((PACKAGE_DIR / "package.json").read_text(encoding="utf-8"))
name = manifest["name"]
version = manifest["version"]
zip_name = f"{name}-{version}.zip"
zip_path = DIST_DIR / zip_name

manifest["url"] = f"{DISTRIBUTION_BASE_URL}/packages/{zip_name}"
manifest["changelogUrl"] = f"https://github.com/{GITHUB_OWNER}/{REPO_NAME}/blob/main/Packages/{name}/CHANGELOG.md"
manifest["documentationUrl"] = f"{DISTRIBUTION_BASE_URL}/#{name}"
manifest.pop("zipSHA256", None)

listing_manifest = dict(manifest)
listing_manifest.pop("zipSHA256", None)

vpm = {
    "name": "YushimaTenjin VPM Repository",
    "id": "com.yushimatenjin.vpm",
    "url": f"{DISTRIBUTION_BASE_URL}/vpm.json",
    "author": {
        "name": "yushimatenjin",
        "url": f"https://github.com/{GITHUB_OWNER}"
    },
    "packages": {
        name: {
            "versions": {
                version: listing_manifest
            }
        }
    }
}

WEBSITE_DIR.mkdir(exist_ok=True)
packages_dir = WEBSITE_DIR / "packages"
packages_dir.mkdir(exist_ok=True)

# 古いzipを残すとALCOM/VCCの確認で混乱するため、同じPackage IDのzipは掃除します。
for old_zip in packages_dir.glob(f"{name}-*.zip"):
    old_zip.unlink()

if zip_path.exists():
    shutil.copy2(zip_path, packages_dir / zip_name)
else:
    print(f"WARNING: package zip not found: {zip_path}")

vpm_text = json.dumps(vpm, ensure_ascii=False, indent=2) + "\n"
(WEBSITE_DIR / "vpm.json").write_text(vpm_text, encoding="utf-8")
(WEBSITE_DIR / "index.json").write_text(vpm_text, encoding="utf-8")
print(f"Updated {WEBSITE_DIR / 'vpm.json'}")
print(f"Updated {WEBSITE_DIR / 'index.json'}")
if zip_path.exists():
    print(f"Copied package zip to {packages_dir / zip_name}")
