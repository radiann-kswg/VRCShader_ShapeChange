#!/usr/bin/env python3
"""開発用リポジトリ（VRCShader_ShapeChange-Develop。Assets 配置の Unity プロジェクト）から、配布リストのファイルをこのパッケージへ写す。

    py .github/scripts/sync_from_dev.py <開発用リポジトリのルート>

写すのは、開発側の Exports/ShapeChangeShader.manifest.txt（Tools > ShapeChange > Export Shader Only の
明示リスト）にあるファイルとその .meta だけ。モデル、生成物、検証記録、調査資料はリストに無いので入らない。
パッケージ配置で場所が変わる記述は PATCHES で書き換える。置換前の文字列がちょうど 1 か所に
一致しなければ、開発側の書き方が変わったとみなして止まる。
"""
from __future__ import annotations

import sys
from pathlib import Path

PACKAGE = "net.numbertales-radiann.shapechange"
DEV_DIR = "Assets/ShapeChangeShader/Shaders"
PKG_DIR = f"Packages/{PACKAGE}"
# README は公開用をこのリポジトリで持つ。Export は unitypackage を書き出す開発用のメニュー
SKIP = {"README.md", "Editor/ShapeChangeExport.cs"}
SUFFIXES = {".shader", ".hlsl", ".lilcontainer", ".lilblock", ".cs", ".asmdef"}

# (ファイル, 置換前, 置換後)。生成物の置き場（ShapeChangeSetup.Folder）は Assets のまま残す
PATCHES = [
    ("ShapeChangeInsert.lilblock",
     f'#include "{DEV_DIR}/ShapeChange.hlsl"',
     f'#include "{PKG_DIR}/ShapeChange.hlsl"'),
    ("FurTwoPass/lilCustomShaderDatas.lilblock",
     f'"{DEV_DIR}/FurTwoPass/ShapeChangeFurPass.hlsl"',
     f'"{PKG_DIR}/FurTwoPass/ShapeChangeFurPass.hlsl"'),
    ("Editor/ShapeChangeSetup.cs",
     f'public const string Folder = "{DEV_DIR}";',
     f'public const string Folder = "{DEV_DIR}";\n        public const string ShaderFolder = "{PKG_DIR}";'),
    # Assets/ShapeChangeShader/Shaders がまだ無いプロジェクトでも Generated を作れるようにする
    ("Editor/ShapeChangeSetup.cs",
     'if (!AssetDatabase.IsValidFolder(Folder+"/Generated")) AssetDatabase.CreateFolder(Folder,"Generated");',
     'if (!AssetDatabase.IsValidFolder(Folder+"/Generated")) { Directory.CreateDirectory(Folder+"/Generated"); AssetDatabase.Refresh(); }'),
    ("Editor/ShapeChangeValidation.cs",
     "new[]{ShapeChangeSetup.Folder}",
     "new[]{ShapeChangeSetup.ShaderFolder}"),
]


def listed_files(dev: Path) -> list[str]:
    manifest = dev / "Exports" / "ShapeChangeShader.manifest.txt"
    prefix = DEV_DIR + "/"
    lines = manifest.read_text(encoding="utf-8").splitlines()
    rels = [line.strip()[len(prefix):] for line in lines if line.strip().startswith(prefix)]
    if not rels:
        raise SystemExit(f"[ERROR] 配布リストを読めません: {manifest}")
    return [rel for rel in rels if rel not in SKIP]


def main(argv: list[str]) -> int:
    if len(argv) != 1:
        print(__doc__, file=sys.stderr)
        return 2
    dev = Path(argv[0]).resolve()
    package = Path(__file__).resolve().parents[2]
    source = dev / DEV_DIR
    rels = listed_files(dev)

    for rel in rels:
        if Path(rel).suffix not in SUFFIXES:
            raise SystemExit(f"[ERROR] 配布リストに想定外の種類のファイルがあります: {rel}")
        # ファイルと .meta、途中のフォルダーの .meta（Unity の GUID を開発側と同じに保つ）
        for item in [rel, rel + ".meta", *(str(parent) + ".meta" for parent in Path(rel).parents if str(parent) != ".")]:
            target = package / item
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes((source / item).read_bytes())

    for rel, old, new in PATCHES:
        path = package / rel
        text = path.read_bytes().decode("utf-8")
        newline = "\r\n" if "\r\n" in text else "\n"
        if text.count(old) != 1:
            raise SystemExit(f"[ERROR] {rel}: 置換前の記述が {text.count(old)} か所あります（1 か所のはず）: {old}")
        path.write_bytes(text.replace(old, new.replace("\n", newline)).encode("utf-8"))

    stale = sorted(
        path.relative_to(package).as_posix()
        for path in package.rglob("*")
        if path.is_file() and path.suffix in SUFFIXES and ".github" not in path.parts
        and path.relative_to(package).as_posix() not in rels
    )
    print(f"写したファイル: {len(rels)} 件（.meta を除く）、書き換え: {len(PATCHES)} か所")
    if stale:
        print("配布リストに無いファイルが残っています。不要なら削除してください:")
        for rel in stale:
            print(f"  {rel}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
