# 新ワールド(NewWorld)を開いてテストする

このドキュメントは、新しい VRChat ワールド用のシーン `Assets/NewWorld/Scenes/NewWorld.unity` を開いてテストする手順です。既存ワールド(`Assets/Scenes/World.unity` など)は変更されません。

## 手順

1. リポジトリ直下(Unity プロジェクトのルート)で実行します:

   ```powershell
   .\Tools\Open-NewWorld.ps1
   ```

   Unity **2022.3.22f1** でプロジェクトが開きます。起動時に `NewWorldPreparation.Prepare` が自動実行され、新シーンの作成・設定・オープンまで行われます。プロジェクトがすでに開いている場合は、起動スクリプトの案内に従い Unity メニュー **Tools > New World > Prepare** を実行してください。

2. シーンが開いたら **Play** ボタンでテストします(ClientSim):
   - スポーン地点 (0, 1, 0) に出現する
   - 40m × 40m の床(上面 y=0)の上に立てる
   - y=-20 より下へ落ちるとリスポーンする
   - コンソールにエラーが出ない
   - 注: ClientSim の保存エラーの既知問題と修正は `Tools/STARTUP-REPAIR.md` を参照

3. Unity メニュー **Tools > New World > Validate** を実行し、セットアップ内容のチェックが通ることを確認します。

4. (任意) ローカルでの追加確認に、VRChat SDK の **Build & Test** を実行できます。アップロード(公開)はワールド名などのメタデータが決まってから行います。テーマ未定の間もローカルでのテストは可能です。

## ファイルとスターター設定

- `Tools/Open-NewWorld.ps1` — この作業用の起動スクリプト。Unity を開き、`NewWorldPreparation.Prepare` を実行します
- `Assets/NewWorld/Scenes/NewWorld.unity` — 新ワールドのシーン
- `Assets/NewWorld/`(Materials / Prefabs / Scripts / Audio / Textures / Editor)— 今後アートなどを追加するためのフォルダ
- スターター設定(`Prepare` が作成する想定):
  - 床: 40m × 40m、上面が y=0
  - Directional Light
  - プレビュー用カメラ(ゲームプレイは VRChat 側のカメラを使用)
  - スポーン地点 (0, 1, 0)、リスポーン高さ y=-20(VRC Scene Descriptor)
  - **PipelineManager の Blueprint ID は空のまま** — 初回アップロード時に「新しいワールド」として作成され、既存ワールドは上書きされません
  - 素材はニュートラル(標準マテリアル)。アート・テーマは今後決定します(この手順ではどのコンセプトにも決定しません)

## 補足

- 上記の動作は自動セットアップの想定仕様です。**Unity で実際に実行・検証するまでは成功と断定しません**。検証結果はこのドキュメントの末尾に実行後に追記されます。
- 環境は既存のままです: SDK 3.10.3・Unity エディタ 2022.3.22f1 のアップグレード、新規依存パッケージの追加は行いません。

## 参考

- VRChat 公式「Creating Your First World」— VRC Scene Descriptor・スポーン設定・テストの公式ガイド:
  https://creators.vrchat.com/worlds/creating-your-first-world/

# 準備時の確認結果

`feat/new-world-20261001` で新規シーンを作成し、Unity 2022.3.22f1 の既存Editorで検証しました。Descriptor 1件、Spawn 1件、Missing Script 0件、Blueprint ID 空、既存シーンへの参照 0件です。ClientSimを25秒再生し、ローカルプレイヤーの床上スポーンと実行時エラー0件を確認しました。既存3シーンおよびEditorBuildSettingsはファイルハッシュが作業前と一致しました。

`Prepare` は作成済みシーンを開くだけなので、以降の編集内容やBlueprint IDを再初期化しません。`Validate` はスターターの初期構成を調べる検証です。床のサイズなどを設計変更した後は、その検証条件も合わせて更新してください。SDKのBuild & Testは今回未実施です。
