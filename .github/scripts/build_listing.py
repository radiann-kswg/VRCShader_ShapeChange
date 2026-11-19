#!/usr/bin/env python3
"""GitHub のリリースから、VPM のリスティング（index.json）と案内ページ（index.html）を作る。

    python3 .github/scripts/build_listing.py <出力フォルダー>

Release ワークフローから呼ぶ。GITHUB_REPOSITORY（owner/repo）と、gh コマンドの認証（GH_TOKEN）を使う。
リリースに付いた zip の中の package.json をそのまま版の情報にし、zip の URL と SHA-256 を足す。
"""
from __future__ import annotations

import hashlib
import html
import io
import json
import os
import subprocess
import sys
import zipfile
from pathlib import Path
from urllib.parse import quote

LISTING_NAME = "ShapeChange Shader (RadianN_kswg)"
LISTING_ID = "net.numbertales-radiann.shapechange.listing"
LISTING_AUTHOR = "RadianN_kswg"


def entry(zip_bytes: bytes, url: str) -> dict:
    """zip の中の package.json に、ダウンロード先と SHA-256 を足した 1 版ぶんの情報。"""
    with zipfile.ZipFile(io.BytesIO(zip_bytes)) as archive:
        manifest = json.loads(archive.read("package.json"))
    manifest["url"] = url
    manifest["zipSHA256"] = hashlib.sha256(zip_bytes).hexdigest()
    return manifest


def listing(repo: str, entries: list[dict]) -> dict:
    owner, name = repo.split("/")
    packages: dict[str, dict] = {}
    for manifest in entries:
        packages.setdefault(manifest["name"], {"versions": {}})["versions"][manifest["version"]] = manifest
    return {
        "name": LISTING_NAME,
        "id": LISTING_ID,
        "url": f"https://{owner.lower()}.github.io/{name}/index.json",
        "author": LISTING_AUTHOR,
        "packages": packages,
    }


def page(data: dict, repo: str) -> str:
    url = data["url"]
    rows = "".join(
        f"<li><code>{html.escape(name)}</code> — {html.escape(', '.join(sorted(package['versions'], reverse=True)))}</li>"
        for name, package in data["packages"].items()
    )
    return f"""<!doctype html>
<html lang="ja">
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{html.escape(data['name'])}</title>
<style>body{{font-family:system-ui,sans-serif;max-width:40rem;margin:3rem auto;padding:0 1rem;line-height:1.7}}
a.add{{display:inline-block;padding:.6rem 1.2rem;border-radius:.4rem;background:#2b6cb0;color:#fff;text-decoration:none}}
code{{word-break:break-all}}</style>
<h1>{html.escape(data['name'])}</h1>
<p><a class="add" href="vcc://vpm/addRepo?url={quote(url, safe='')}">VCC / ALCOM に追加する (Add to VCC / ALCOM)</a></p>
<p>ボタンが開かないときは、VCC または ALCOM のリポジトリ追加に次の URL を入れてください。<br><code>{html.escape(url)}</code></p>
<ul>{rows}</ul>
<p><a href="https://github.com/{html.escape(repo)}">GitHub: {html.escape(repo)}</a></p>
</html>
"""


def gh(*args: str) -> bytes:
    return subprocess.run(["gh", *args], check=True, capture_output=True).stdout


def main(argv: list[str]) -> int:
    if len(argv) != 1:
        print(__doc__, file=sys.stderr)
        return 2
    repo = os.environ["GITHUB_REPOSITORY"]
    # shortcut: 1 ページ（100 件）まで。リリースが 100 件を超えたらページ送りを足す
    releases = json.loads(gh("api", f"repos/{repo}/releases?per_page=100"))
    entries = []
    for release in releases:
        if release["draft"]:
            continue
        for asset in release["assets"]:
            if asset["name"].endswith(".zip"):
                data = gh("release", "download", release["tag_name"], "-R", repo, "-p", asset["name"], "-O", "-")
                entries.append(entry(data, asset["browser_download_url"]))
    out = Path(argv[0])
    out.mkdir(parents=True, exist_ok=True)
    data = listing(repo, entries)
    (out / "index.json").write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (out / "index.html").write_text(page(data, repo), encoding="utf-8")
    print(f"{len(entries)} 版を載せました: {data['url']}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
