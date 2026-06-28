#!/usr/bin/env python3
import hashlib
import json
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PACKAGE_ID = "com.yushimatenjin.vrc-bake-assistant"
PACKAGE_DIR = ROOT / "Packages" / PACKAGE_ID
WEBSITE_DIR = ROOT / "Website"
DIST_DIR = ROOT / "Dist"
WEBSITE_PACKAGES_DIR = WEBSITE_DIR / "packages"
GITHUB_OWNER = "yushimatenjin"
REPO_NAME = "vrc-bake-assistant"

manifest = json.loads((PACKAGE_DIR / "package.json").read_text(encoding="utf-8"))
name = manifest["name"]
version = manifest["version"]
zip_name = f"{name}-{version}.zip"
zip_path = DIST_DIR / zip_name

# GitHub Pagesにzipも一緒に公開します。
WEBSITE_PACKAGES_DIR.mkdir(parents=True, exist_ok=True)

# 旧zipは残すと混乱しやすいので、Website/packages内は現在バージョンだけにします。
for old_zip in WEBSITE_PACKAGES_DIR.glob(f"{name}-*.zip"):
    old_zip.unlink()

if zip_path.exists():
    published_zip = WEBSITE_PACKAGES_DIR / zip_name
    shutil.copy2(zip_path, published_zip)
    # vpm.jsonに書くSHAは「実際にWebsiteへ置くzip」から計算します。
    zip_sha256 = hashlib.sha256(published_zip.read_bytes()).hexdigest()
else:
    zip_sha256 = "REPLACE_WITH_SHA256_AFTER_BUILDING_ZIP"

package_url = f"https://{GITHUB_OWNER}.github.io/{REPO_NAME}/packages/{zip_name}"
manifest["url"] = package_url
manifest["changelogUrl"] = f"https://github.com/{GITHUB_OWNER}/{REPO_NAME}/blob/main/Packages/{PACKAGE_ID}/CHANGELOG.md"
manifest["documentationUrl"] = f"https://{GITHUB_OWNER}.github.io/{REPO_NAME}/"

listing_manifest = dict(manifest)
listing_manifest["zipSHA256"] = zip_sha256

vpm = {
    "name": "YushimaTenjin VPM Repository",
    "id": "com.yushimatenjin.vpm",
    "url": f"https://{GITHUB_OWNER}.github.io/{REPO_NAME}/vpm.json",
    "author": {
        "name": "yushimatenjin",
        "url": "https://github.com/yushimatenjin"
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
vpm_text = json.dumps(vpm, ensure_ascii=False, indent=2) + "\n"
(WEBSITE_DIR / "vpm.json").write_text(vpm_text, encoding="utf-8")
(WEBSITE_DIR / "index.json").write_text(vpm_text, encoding="utf-8")
print(f"Updated {WEBSITE_DIR / 'vpm.json'}")
print(f"Updated {WEBSITE_DIR / 'index.json'}")
if zip_path.exists():
    print(f"Copied {zip_path} -> {WEBSITE_PACKAGES_DIR / zip_name}")
    print(f"Package URL {package_url}")
    print(f"SHA256 {zip_sha256}")
