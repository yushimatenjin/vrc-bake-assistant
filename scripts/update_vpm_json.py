#!/usr/bin/env python3
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PACKAGE_ID = "com.yushimatenjin.vrc-bake-assistant"
PACKAGE_DIR = ROOT / "Packages" / PACKAGE_ID
WEBSITE_DIR = ROOT / "Website"
DIST_DIR = ROOT / "Dist"
GITHUB_OWNER = "yushimatenjin"
REPO_NAME = "vrc-bake-assistant"

manifest = json.loads((PACKAGE_DIR / "package.json").read_text(encoding="utf-8"))
name = manifest["name"]
version = manifest["version"]
zip_name = f"{name}-{version}.zip"
zip_path = DIST_DIR / zip_name
release_url = f"https://github.com/{GITHUB_OWNER}/{REPO_NAME}/releases/download/{version}/{zip_name}"
manifest["url"] = release_url
manifest["changelogUrl"] = f"https://github.com/{GITHUB_OWNER}/{REPO_NAME}/releases/tag/{version}"
manifest["documentationUrl"] = f"https://{GITHUB_OWNER}.github.io/{REPO_NAME}/"

listing_manifest = dict(manifest)
if zip_path.exists():
    listing_manifest["zipSHA256"] = hashlib.sha256(zip_path.read_bytes()).hexdigest()
else:
    listing_manifest["zipSHA256"] = "REPLACE_WITH_SHA256_AFTER_BUILDING_ZIP"

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
# index.json も置くと、VPMコミュニティでよく見る URL 形式にも対応しやすいです。
(WEBSITE_DIR / "index.json").write_text(vpm_text, encoding="utf-8")
print(f"Updated {WEBSITE_DIR / 'vpm.json'}")
print(f"Updated {WEBSITE_DIR / 'index.json'}")
