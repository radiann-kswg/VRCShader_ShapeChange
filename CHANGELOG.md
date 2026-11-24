# Changelog

## 1.0.1

- 旧 `.unitypackage` と VPM 版の `ShapeChange.Editor` が重複して Unity 2022 のコンパイルが止まる問題を修正。VPM が旧版の配布ファイル 29 件を既定の配置から移行する。
- 移行後も `Assets` 側に残るフォルダーと衝突しないよう、配布側の 3 フォルダーに専用の GUID を割り当てた。既存マテリアルが参照するシェーダーの GUID は維持した。

## 1.0.0

- 最初の公開版。16 形状（立方体、直方体、頭付き立方体、膨体、真球、頭付き球体、円柱 2 種、コイン、人型平面 4 種、縦平面、板、反物）への変形シェーダーと、セットアップ・検証用の Editor ツール。
- VPM パッケージとして配布する。シェーダーは `Packages/net.numbertales-radiann.shapechange` に入り、セットアップで作るデータは `Assets/ShapeChangeShader/Shaders/Generated` に保存する。
