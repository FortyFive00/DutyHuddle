"""Writes repo.json (the Dalamud custom repository file) from the manifest produced by the build.

Usage: python tools/release/make_repo_json.py
Run after `dotnet build DutyHuddle.sln -c Release`.
"""
import json
import pathlib
import time

ROOT = pathlib.Path(__file__).resolve().parents[2]
REPO = "FortyFive00/DutyHuddle"
MANIFEST = ROOT / "src" / "DutyHuddle" / "bin" / "Release" / "DutyHuddle" / "DutyHuddle.json"
DOWNLOAD = f"https://github.com/{REPO}/releases/latest/download/latest.zip"

manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
manifest.update(
    {
        "RepoUrl": f"https://github.com/{REPO}",
        "IconUrl": f"https://raw.githubusercontent.com/{REPO}/main/src/DutyHuddle/images/icon.png",
        "DownloadLinkInstall": DOWNLOAD,
        "DownloadLinkUpdate": DOWNLOAD,
        "DownloadLinkTesting": DOWNLOAD,
        "IsTestingExclusive": False,
        "LastUpdate": int(time.time()),
    }
)

(ROOT / "repo.json").write_text(json.dumps([manifest], indent=2) + "\n", encoding="utf-8")
print(f"repo.json written for version {manifest['AssemblyVersion']}")
