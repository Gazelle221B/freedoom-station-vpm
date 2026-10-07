# 起動修正

このプロジェクトは `ProjectSettings/ProjectVersion.txt` に指定された Unity **2022.3.22f1** で開きます。

```powershell
.\Tools\Open-Unity.ps1
```

この起動スクリプトは、プロジェクトのUnityバージョンを読み取り、ClientSimの保存競合への修正を適用してからEditorを開きます。Editorが別の場所にある場合は `-EditorPath` で指定できます。

VRChat SDK 3.10.3のClientSimではPlayerObjectの非同期保存が重なり、sharing violationが発生していました。ローカルの埋め込みSDKに書き込みロックを追加し、保存先の取得をメインスレッドで行います。SDKの再インストール後も起動スクリプトから再適用できます。SDKの実装が変わった場合は、適用スクリプトが変更を検出して停止します。

画像共有とテキスト共有の古いスクリプトGUIDを現在のスクリプトへ修正しました。参照先が存在しない旧NameTag UIのスクリプト、QvPenサンプルの旧TagSelector、World2の旧プレイリスト補助コンポーネントと欠落したYamaPlayerインスタンスを除去しました。欠落機能そのものを再実装する変更ではありません。除去前のシーン・Prefabは作業チャットの出力 `original-assets-before-repair.zip` に保存しています。

WorldとWorld2のNetwork ID一覧から、すでに存在しないオブジェクトへの参照も除去しています。存在するオブジェクトのIDは保持しています。

SDKのローカルパッケージは元の方針に従ってGit対象外です。SDK修正の再現手段として `Repair-ClientSimPersistence.ps1` を管理します。
