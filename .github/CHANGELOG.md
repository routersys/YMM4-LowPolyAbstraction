# v1.0.0 - ローポリ化 for YMM4

YukkuriMovieMaker4向けのローポリ化エフェクトプラグインの初回リリースです。
素材のエッジを拘束点とみなし、特徴フロー場を重みとした重心ボロノイのLloyd緩和で頂点を最適化し、ボロノイ双対の三角メッシュで素材をローポリ調に抽象化して描画します。
三角形の配置はシードから決定論的に決まり、色の誤差が大きい三角形を細分化して細部へ密度を寄せます。
計算はComputeSharpの計算シェーダーがDirect3D 12で実行し、YMM4のDirect3D 11側とは共有テクスチャおよび共有フェンスで接続します。
8言語のリソース構成のUIを備えます。

---

## 新機能

### 1. ローポリ化の計算パイプライン

`LowPolyAbstractionPipeline`は、解析、構造、描画の3段階の計算シェーダーを`ComputeContext`へ記録して実行します。作業格子とサイトのバッファーは計算領域の大きさと品質に応じて確保し、サイズが変わらないフレームでは再利用します。処理の流れは次のとおりです。

1. `AnalyzeShader`が、現在のフレームを作業解像度へ箱型平均で縮小し、輝度、色、内容ハッシュ、シルエットのバウンディングボックスを求めます。
2. `EdgeShader`が、輝度とアルファのSobel勾配からエッジ強度を求め、`EdgeDistanceSeedShader`と`JumpFloodPixelPassShader`がエッジまでの距離場をジャンプフラッドで求めます。`WeightShader`は距離の三角波から特徴フロー場の重みを作ります。
3. `EdgeBestShader`と`EdgeEmitShader`がエッジ上の拘束点を、`InteriorCountShader`と`InteriorEmitShader`が内部点を、ブロック走査による圧縮で決定論的な順序のまま配置します。
4. `VoronoiScatterShader`と`JumpFloodSitePassShader`がサイトのボロノイ割り当てを求め、`CentroidAccumulateShader`と`UpdateSitesShader`が重み付き重心へのLloyd緩和を反復します。
5. `CornerCountShader`と`TriangleEmitShader`が割り当て図の双対として三角形を抽出し、`IncidenceBuildShader`がサイトごとの接続リストを構築します。
6. `TriangleColorPassShader`と`FinalizeTrianglesShader`が三角形の色と輝度分散を集計し、`RefineCountShader`と`RefineEmitShader`が高誤差三角形の重心へサイトを挿入して、緩和と三角化を反復します。
7. `SiteColorAccumulateShader`と`SiteColorFinalizeShader`が頂点色を集計し、`RenderShader`が画素ごとに包含三角形を求めて塗り、輪郭線、彩度、明度ゆらぎを適用します。

| シェーダー | 役割 |
|---|---|
| `AnalyzeShader` | 縮小、輝度、内容ハッシュ、バウンディングボックスを求める |
| `EdgeShader` | Sobel勾配からエッジ強度を求める |
| `EdgeDistanceSeedShader` / `JumpFloodPixelPassShader` | エッジまでの距離場を求める |
| `WeightShader` | 特徴フロー場の重みを作る |
| `EdgeBestShader` / `EdgeEmitShader` | エッジ上の拘束点を配置する |
| `InteriorCountShader` / `InteriorEmitShader` | 内部点を層化ジッターで配置する |
| `BlockCountShader` / `BlockPrefixShader` | ブロック走査で稠密な添字を確定する |
| `VoronoiClearShader` / `VoronoiScatterShader` / `JumpFloodSitePassShader` | ボロノイ割り当てを求める |
| `CentroidAccumulateShader` / `UpdateSitesShader` | 重み付き重心へのLloyd緩和を行う |
| `CornerCountShader` / `TriangleEmitShader` | ボロノイ双対の三角形を抽出する |
| `IncidenceBuildShader` | サイトごとの接続三角形を記録する |
| `TriangleColorPassShader` / `FinalizeTrianglesShader` | 三角形の色と輝度分散を集計する |
| `RefineCountShader` / `RefineEmitShader` | 高誤差三角形の重心へサイトを挿入する |
| `SiteColorAccumulateShader` / `SiteColorFinalizeShader` | 頂点色を集計する |
| `RenderShader` | 三角メッシュを描画する |

### 2. 特徴フロー場つき重み付きCVTと誤差駆動の細分化

頂点の最適化は、Gai and Wangの論文「Artistic Low Poly rendering for images」の特徴フロー場つき重心ボロノイに基づきます。エッジまでの距離`d`とレーン幅`m`から三角波の重みを作り、`exp(-d / 8m)`の減衰を掛けて、頂点をエッジに平行なレーンへ整列させます。レーン幅はGai and Wangのサンプル間隔`0.02(W+H)`の半分です。エッジ上の拘束点は固定し、内部点だけを重心へ動かします。

重心の集計は、重みを`64`倍で量子化した整数を、下位と上位の2つの32bit整数へ桁上がり付きの原子加算で足し込みます。加算順序に依存しない整数演算だけを使うため、並列実行でも結果は決定論的です。

細分化は、Lawonn and Güntherの論文「Stylized Image Triangulation」の高誤差三角形分割に基づきます。三角形ごとに輝度の平均と二乗平均から分散を求め、閾値を超えた三角形の重心へ新しいサイトを挿入し、Lloyd緩和2回と再三角化を品質に応じた回数だけ反復します。閾値は細分化のパラメータから`0.02`〜`0.0004`へ割り当てます。挿入の上限は目標サイト数の1.5倍です。

三角形の塗り色は、輝度の平均±1.2σの帯域内の画素だけを使う刈り込み平均で決めます。明部と暗部が混ざった三角形でも中間色に濁りません。

### 3. ボロノイ双対からの決定論的な三角形抽出

三角形は、ボロノイ割り当て図の2×2画素角で3つまたは4つの相異なるサイトが接する箇所の双対として抽出します。4つの場合は対角で2つへ分けます。抽出した三角形の添字は、ブロック走査による圧縮で角の位置順に確定するため、並列実行でも順序が揺れません。

画素の包含判定は、最近傍サイトの接続三角形だけを重心座標で調べます。辺上の画素が複数の三角形に該当する場合は最小添字を採用し、判定を決定論的に保ちます。

### 4. 可視範囲への出力矩形の最小化

`AnalyzeShader`がシルエットのバウンディングボックスを原子的最小値と最大値で集計し、`Simulate`の読み戻しでキャッシュします。描画はこの矩形に固定余白8画素を加え、4画素境界へそろえた範囲だけを計算します。三角形はシルエットの内側のサイトから作られるため、出力はこの矩形に収まります。

### 5. 構造キャッシュ

三角メッシュを決める入力が変わらないフレームでは、構造の計算を再利用します。

- 素材の内容は、作業格子の輝度とアルファの量子化値のハッシュの総和とXORの2値へ集約し、16個の整数の読み戻しで前フレームと比較します。
- 内容のハッシュ、計算領域の大きさ、品質、シード、細かさ、輪郭忠実度、細分化が一致する場合は、構造段階を実行しません。
- グラデーション、輪郭線、彩度、明度ゆらぎは描画専用のパラメータで、変更してもメッシュを再計算しません。構造と描画パラメータと出力矩形がすべて変わらないフレームでは、描画段階も実行せず、前フレームの出力テクスチャを使用します。

### 6. Direct3D 11・Direct3D 12相互運用

`LowPolyAbstractionGpuInterop`は、YMM4のDirect3D 11・Direct2D側と、ComputeSharpのDirect3D 12側を接続します。ComputeSharpの`GraphicsDevice`は、YMM4が使うDXGIアダプターのLUIDと一致するものを選びます。

入力と出力は、ComputeSharpで確保した共有テクスチャをDirect3D 11のテクスチャとして開き、Direct2Dのビットマップとして扱います。入力のテクスチャは素材の大きさで確保し、出力のテクスチャは可視範囲の矩形を収める容量で確保して拡大時だけ作り直します。両デバイスの同期は、Direct3D 12のフェンスを共有フェンスとしてDirect3D 11側で開いて行います。

`BeginCompute`は、Direct3D 11のコマンドを送出したうえでDirect3D 12側を待機させ、`EndCompute`は、Direct3D 12側の完了をDirect3D 11側で待ちます。

Direct3D 12デバイスの取得や共有リソースの作成に失敗した場合は、`TryCreate`が`null`を返し、エフェクトを適用せず入力映像を表示します。

### 7. カスタムシェーダーによる合成

`LowPolyAbstractionCustomEffect`は、`[CustomEffect(2)]`の2入力エフェクトです。入力0は元映像、入力1は描画したローポリ画です。ピクセルシェーダー`LowPolyAbstraction.hlsl`の`main`は、`amount`が0以下のとき元映像をそのまま返し、そうでないときはローポリ画のRGBをアルファでクランプし、`poly + source * (1 - poly.a)`のアルファ合成の結果と元映像を`amount`で線形補間します。

定数バッファーは`Amount`と3つの詰め物で16バイトです。`MapInputRectsToOutputRect`は2つの入力矩形の和集合を出力矩形とします。

### 8. エフェクト定義とパラメータ

`LowPolyAbstractionEffect`は、YMM4の映像エフェクトとして宣言されます。

`[VideoEffect]`属性は以下のパラメーターで宣言されます。

- 表示名: `Texts.LowPolyAbstraction`（ローカライズキー、日本語では「ローポリ化」）
- カテゴリー: `VideoEffectCategories.Decoration`・`VideoEffectCategories.Filtering`
- 検索タグ: `TagLowPoly`・`TagTriangle`・`TagPolygon`
- `IsAviUtlSupported = false`によりAviUtl向けEXO出力は非対応
- `ResourceType = typeof(Texts)`でローカライズリソースを指定

公開プロパティは以下のとおりです。基本項目は「基本」グループ、三角形項目は「三角形」グループ、描画項目は「描画」グループに属します。

| プロパティ | 型 | デフォルト | 内部範囲 | アニメーション |
|---|---|---|---|---|
| `Amount` | `Animation` | 100 | 0〜100 | あり |
| `Quality` | `LowPolyAbstractionQuality` | `High` | — | なし |
| `Detail` | `Animation` | 60 | 0〜100 | あり |
| `Fidelity` | `Animation` | 70 | 0〜100 | あり |
| `Refine` | `Animation` | 50 | 0〜100 | あり |
| `Seed` | `int` | 0 | 0〜int.MaxValue | なし |
| `Gradient` | `Animation` | 30 | 0〜100 | あり |
| `Wireframe` | `Animation` | 0 | 0〜100 | あり |
| `Saturation` | `Animation` | 30 | 0〜100 | あり |
| `Jitter` | `Animation` | 25 | 0〜100 | あり |

`GetAnimatables`は`Amount`・`Detail`・`Fidelity`・`Refine`・`Gradient`・`Wireframe`・`Saturation`・`Jitter`を返します。`Seed`は負値を代入すると0へ丸めます。

`CreateExoVideoFilters`は空のシーケンスを返します。`CreateVideoEffect`は映像処理用のインスタンスを生成します。エフェクトを最初に生成したときに、更新確認を一度だけ開始します。

### 9. フレームごとの更新

各フレームでYMM4の`EffectDescription`からフレーム位置、アイテム長、FPSを取得し、アニメーション値を評価します。値をパイプラインが前提とする範囲へ制限してから転送します。

| パラメータ | 変換 |
|---|---|
| `Amount` | `value / 100` をカスタムシェーダーの`Amount`へ |
| `Detail` | `value / 100` を0〜1へクランプし、目標サイト数へ |
| `Fidelity` | `value / 100` を0〜1へクランプし、エッジのサンプル間隔と重みの強さへ |
| `Refine` | `value / 100` を0〜1へクランプし、分散の閾値へ |
| `Gradient` | `value / 100` を0〜1へクランプ |
| `Wireframe` | `value / 100` を0〜1へクランプし、最大1.5画素の線幅へ |
| `Saturation` | `value / 100` を0〜1へクランプし、最大0.6の彩度強調へ |
| `Jitter` | `value / 100` を0〜1へクランプし、最大0.08の明度変動へ |
| `Seed` | 0以上へクランプ |

強さが0以下のときは、ローポリ画を描画せず入力映像をそのまま出力します。入力の範囲が有限でない場合や、計算領域の長辺が8192画素を超える場合も、入力映像を表示します。

### 10. 品質設定

品質は、作業解像度、頂点数の基準、Lloyd緩和の反復回数、細分化の反復回数をまとめて切り替えます。

| 品質 | 作業解像度 | 頂点数の基準 | 緩和反復 | 細分化反復 |
|---|---:|---:|---:|---:|
| 標準 | 1024 | 4096 | 6回 | 1回 |
| 高品質 | 1536 | 8192 | 8回 | 2回 |
| 最高品質 | 2048 | 16384 | 10回 | 3回 |

作業解像度は計算領域の長辺の画素数です。短辺は計算領域の縦横比に合わせ、最小16画素とします。

### 11. ローカライズ

`Texts`クラスは`[AutoGenLocalizer]`属性を持つ`partial`クラスとして宣言されます。
`YukkuriMovieMaker.Generator`のソースジェネレーターが`Texts.csv`を処理し、各ロケールのリソースファイルを自動生成します。

対応リソース: 日本語（`ja-jp`）・英語（`en-us`）・中国語簡体字（`zh-cn`）・中国語繁体字（`zh-tw`）・韓国語（`ko-kr`）・スペイン語（`es-es`）・アラビア語（`ar-sa`）・インドネシア語（`id-id`）

主なローカライズキーは以下のとおりです。

| キー | ja-jp |
|---|---|
| `LowPolyAbstraction` | ローポリ化 |
| `BasicGroup` | 基本 |
| `StructureGroup` | 三角形 |
| `AppearanceGroup` | 描画 |
| `Amount` | 強さ |
| `Quality` | 品質 |
| `Detail` | 細かさ |
| `Fidelity` | 輪郭忠実度 |
| `Refine` | 細分化 |
| `Seed` | シード |
| `Gradient` | グラデーション |
| `Wireframe` | 輪郭線 |
| `Saturation` | 彩度 |
| `Jitter` | 明度ゆらぎ |
| `QualityBalanced` | 標準 |
| `QualityHigh` | 高品質 |
| `QualityUltra` | 最高品質 |
| `TagLowPoly` | ローポリ |
| `TagTriangle` | 三角形 |
| `TagPolygon` | ポリゴン |
| `UpdateAvailableMessage` | 新しいバージョン {0} が公開されています。 |
