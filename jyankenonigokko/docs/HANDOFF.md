# マジックハンド 引き継ぎメモ

会話のコンテキストが切れても作業を再開できるようにするための記録。
「何がどうなっているか」だけでなく「なぜそうしたか」「どこで詰まったか」を残す。

最終更新: 2026-08-23（§29まで）

---

## 0. 30秒で掴む

- **この文書の運用ルール**: 作業を1件終えるたびに必ずこのファイルを更新する（後回しにしない）。
  作業依頼のプロンプトも、コンテキストが切れる前後の引き継ぎも、**すべてこの1ファイルに集約する**。
  別ファイル（`docs/PROMPT_*.md` や `docs/superpowers/specs/*.md` のような個別のプロンプト/引き継ぎ
  ファイル）は2026-08-22に廃止・削除済み。新しく作らないこと
- **複数の端末で並行して作業されている**: このプロジェクトは別PC（ユーザー環境: `kodai`、
  コミット著者名`LILILILILILILIL\kodai`）でも同時にClaude Codeが動いていることがある
  （2026-08-22に実際に発生した。§7-12〜7-16と§11〜14は別セッションが並行して積んだ内容で、
  `CreateHandVisual`の`addOutline`パラメータのように、片方が書いた関数を
  もう片方が拡張している箇所もある）。**作業前に必ず `git status` と `git log --oneline -10`
  を確認し、この文書やコード中のコメントが指す内容が実際のファイルと一致しているか
  確かめること**。特にコミットのタイムスタンプは端末間で時計がズレている可能性があり、
  新しさの判断に使えない場合がある（`git log`の表示順＝親子関係の方を信用する）
- **何を作っているか**: 2人対戦の3Dじゃんけん鬼ごっこ。勝てる手で相手に触ると+1点
- **どこを触るか**: シーンは全部 `Assets/_Game/Editor/MagicHandSceneBuilder.cs` から生成される。
  **Unity上で手作業しても次の生成で消える**
- **作業の型**: コード修正 → アセット更新 → **新しいアセンブリを確認** → `BuildScene()` → 再生モードで実測
- **判断の型**: 見た目は必ず画像に描き出して確認する。数値だけで決めない（§10）
- **一番踏みやすい罠**: 古いアセンブリでビルドが走り「成功したのに変わらない」（§3）。
  もう一つ、`BuildScene()` はリフレクション経由だと呼び出し自体が握りつぶされることがある。
  `public static` なので `MagicHand.EditorTools.MagicHandSceneBuilder.BuildScene();` と直接呼び、
  シーンファイルの更新日時か実際のフィールド値で「本当に走ったか」を必ず確認する（§7-11）

---

## 1. プロジェクト概要

- Unity 6000.3.14f1 / URP / Input System 1.19.0
- 場所: `C:\gameB9\jyankenonigokko`
- 2人対戦（ローカル2P）の3Dじゃんけん鬼ごっこ。競技場で戦う2人の魔法使い
- 勝てる手で相手に接触すると +1点。あいこは互いに弾かれ、負けている側だけ加速。制限時間内の得点勝負

### 仕様の経緯について
初期の設計・実装プロンプト（基本システム設計→生成プロンプト→改善第2弾→準備ルーム追加、
いずれも `docs/superpowers/specs/` にあった）と、HUD/アイテム改善の実装プロンプト
（`docs/PROMPT_HUD_AND_ITEMS.md` にあった）は、内容がすべてこの文書の各節（特に§6〜§7-11）に
反映・更新済みのため、2026-08-22に削除した。**初期プロンプトの数値や設計方針は多くが後から
変わっている**（例: カメラはCinemachineでなく自作の `ThirdPersonCameraRig`、アイテムの湧き数や
持続時間も §6 の値が最新）。今の仕様の根拠は常にこの文書の該当節を見ること。過去の意思決定の
経緯そのもの（「なぜ変えたか」）は各節の本文に理由として書き残す方針にしている

---

## 2. 最重要：シーンはコードから生成する

**シーンを手で編集しても、次の生成で全部消える。** 変更は必ずビルダーのコードへ入れること。

- 生成元: `Assets/_Game/Editor/MagicHandSceneBuilder.cs`
- 実行: Unityメニュー `MagicHand > Build Playable Scene`
- 出力: `Assets/_Game/Scenes/MainScene.unity`（毎回まるごと作り直す）

アイテムのScriptableObject、マテリアル、プレハブ、AnimatorControllerもこのビルダーが生成する。

### 生成物が「既にあれば作り直さない」点に注意
`LoadOrCreate` 系は既存アセットがあれば再利用する。定義を変えたのに反映されないときは、
該当アセット（例: `_Game/Animations/PlayerAnimator.controller`）を削除してから再生成する。

---

## 3. MCP経由で作業するときの手順（重要な落とし穴）

Synaptic AI Pro のHTTPサーバ（`http://localhost:8086`）経由でUnityを操作している。

### 必ず守る順番
1. Playモードを抜ける（`EditorApplication.isPlaying = false`）
2. `unity_force_refresh_assets` でコンパイル
3. **リフレクションで新メソッドの存在を確認してから** `BuildScene()` を呼ぶ
4. 検証

### 踏んだ落とし穴
- **古いアセンブリでBuildSceneが走る**: リフレッシュ直後はまだ再コンパイルが終わっておらず、
  古いコードでシーンが生成されて「成功したのに変更が反映されない」ことが何度もあった。
  `Assembly-CSharp-Editor.dll` のタイムスタンプ、または新メソッドの有無で必ず確認する
- **接続が落ちる**: ドメインリロードやアセット再インポートでMCPリンクが切れる。
  Unityウィンドウがバックグラウンドだと復帰しないことがある。→ ユーザーにクリックしてもらう
- **run_csharp の制約**: Mono.CSharpの対話評価なので `new List<T>()` などジェネリック型の生成が失敗して
  `result: null` になる。配列を使う。複数文＋early returnも失敗しやすいので1文ずつに分ける
- **日本語を含むクエリが文字化けする**ことがある。検証クエリはASCIIで書くのが安全
- Playモードは**Unityが非フォーカスだと止まる**。検証時は `Application.runInBackground = true` にする
- **`BuildPipeline.BuildPlayer` は数分かかりMCPが途中でタイムアウトすることがある**。
  タイムアウトしても実際のビルドはUnity側で継続していることが多いので、
  出力フォルダに実行ファイルが生成されるまで待ってから確認する。MCPの接続が切れた場合は
  Unityウィンドウをクリックして復帰させる（§3の「接続が落ちる」と同じ現象）

### 3-1. Playerビルド時の罠：`Assets/FastMesh` のEditor専用コードが紛れ込んでいた

Play/Editorでは問題なくても、実際に`BuildPipeline.BuildPlayer`でPlayerをビルドすると
`CS0246: The type or namespace name 'SceneView' could not be found` で失敗していた。

- 原因は `Assets/FastMesh/Scripts/SceneViewText.cs`。アセットストアの「Fast Mesh」パッケージに
  付属する宣伝用のオーバーレイ（Sceneビューに広告ボタンを出すだけ）で、`UnityEditor.SceneView` を
  参照しているのに `Editor` フォルダに置かれていなかった。`Editor` フォルダ配下でないスクリプトは
  Player用アセンブリにもコンパイルされるため、`UnityEditor` 名前空間が存在しないPlayerビルドで
  即座にコンパイルエラーになる
- ゲーム本体（`_Game`配下）とは無関係のファイルなので中身は変えず、ファイル全体を
  `#if UNITY_EDITOR` 〜 `#endif` で囲んでPlayerのコンパイル対象から外すだけで直した
- 実測: 修正前は `BuildPipeline.BuildPlayer` の `report.summary.result` が `Failed`
  （`errors=2`、実体は同一エラーの重複カウント）。修正後は同じビルドが `MagicHand.exe` 一式
  （`MagicHand_Data` に3シーンぶんの `level0`/`level1`/`level2` を含む、約215MB）を生成することを確認済み
- 同種の罠を踏まないための教訓: アセットストア素材を追加するときは、Editor専用のスクリプトが
  `Editor` フォルダの外に置かれていないか（特に「ExecuteInEditMode」付きの宣伝・プレビュー系）を
  確認する。Playモードでは`UnityEditor`が使えてしまうため、この種の混入はPlayerビルドまで気づけない

---

## 4. ゲームの構造

### ステート（`GameState`）
`Title → Lobby → Selection → InGame → (TieBreak) → Finish → Result`

**Result は別シーン**（`SceneManager.LoadScene("Victory")`）へ移る。
点は `MatchResultData` の静的フィールドで渡す。シーン内の `ResultPanel` は実質使われていない。

- 単一シーン内で `GameManager` が enum + switch のFSMで切り替える
- 準備ルーム（Lobby）は**同じシーンの Y-100 の位置**に併設。State切替時にワープさせるだけ
- Result の「もう一度」は Title を経由せず直接 Lobby へ戻る
- **TieBreak**: 時間切れの時点で同点だったときだけ挟まる。サドンデスか結果発表かを選ぶ
- **Finish**: Resultへ行く前に必ず挟む、「FINISH」を出すだけの一瞬の間。§4-1

### サドンデス
- 操作は**十字キーで選んで ✕/A（Space）で決定**。手の選択画面と同じ操作に揃えてある。
  ボタンごとに違うキーを割り当てると、押す前に対応を覚える必要があって迷うため
- 選択は**どちらのプレイヤーからでも**できる（先に押した方が通る）。
  同点で終わった直後に「どちらが決める権利を持つか」を決める材料が無いため
- **時計は止めたまま**にする。`Timer.Remaining` が0のまま `Tick` すると即座に終了してしまうので、
  `IsSuddenDeath` の間は `Tick` 自体を呼ばない。HUDの時計は「サドンデス」と出す
- 得点した瞬間に `Result` へ。**負けた側のノックバック・スタン・リスポーンには入らせない**
  （`ResolveContact` が加点直後に抜ける）
- `TieBreak` に入ると入力を切って場を固定する。`InGame` へ戻るとき必ず入れ直すこと
- `IsSuddenDeath` は `ResetScores` で落とす。再戦へ持ち越さないため

## 4-1. Finishステート（`FinishUI`）

以前は時間切れ・サドンデスの決着・TieBreakの「結果発表」、いずれも`Result`へ直接飛んでいた。
「試合が終わった」ことを一瞬でも見せてから結果発表に移りたいという依頼で、
`Result`の手前に必ず`Finish`を挟むようにした。

- `GameState`に`TieBreak`と`Result`の間として追加。**Resultへ行く経路は3つとも必ずFinishを経由する**
  （通常の時間切れ、TieBreakで「結果発表」を選んだとき`FinishWithResult()`、
  サドンデス中に決着がついた瞬間の`ResolveContact`）
- `GameManager.EnterState(Finish)`で入力を切り・アイテム補充を止め（Result直前と同じ後始末）、
  `finishDuration`（既定2秒）待ってから自動で`ChangeState(GameState.Result)`する
  コルーチンを回すだけ。選択肢もボタンもない、ただの待ち時間
- 表示は`FinishUI`（`TieBreakUI`と同じポーリング方式）。全画面を暗くした上に「FINISH」とだけ出す
- 実測: `TieBreak`から`FinishWithResult()`、サドンデス中の`ResolveContact`、両方の経路で
  `CurrentState`が`Finish`になり、`FinishUI`のパネルが実際に表示された状態をスクリーンショットで確認。
  その後`finishDuration`経過で`Result`→`Victory`シーンへ自動遷移することも確認済み

## 4-2. 試合開始のSTART演出

3-2-1のカウントダウンが0になった瞬間に`InGame`へ切り替わっていたが、「START」の合図を挟みたい
という依頼で、既存の`countdownText`（`InGameHUD`、`CountdownThenStart`専用。準備ルームで
待っている間の`LobbyHUD`側のカウントダウンとは別物）を流用して一瞬だけ表示するようにした。

- `GameManager.ShowStartText`（public bool）を追加。`CountdownThenStart`が3-2-1を出し終えた後、
  `startTextDuration`（既定1秒）の間だけtrueにしてから`ChangeState(GameState.InGame)`する
- **「START」は分割画面それぞれではなく画面中央に一つだけ**出す（FINISHと同じ見せ方）。
  最初は各プレイヤーの`InGameHUD.countdownText`（3-2-1と共用）に出していたが、
  分割画面の両側に同じ文字が二つ並んでしまうと指摘があり、`FinishUI`と同じく
  共有Canvas（`UI_Global`）側の専用`StartUI`に分離した。数字の3-2-1は元通り
  `InGameHUD`側（プレイヤーごと）に残っている ---「試合が始まる合図」だけを統一する意図
- `StartUI`は`FinishUI`とほぼ同じ作り（`GameManager.ShowStartText`をポーリングしてpanelを
  ON/OFF）だが、**背景は敷かない**。試合はもう見えているべきで、Finishのように暗くする必要が
  ないため、`AddTextOutline`の縁取りだけで可読性を確保している
- 準備ルームの`LobbyHUD`側（`CountdownThenSelection`、両者がスタート地点に揃うまでの待ち）は
  対象外。「試合開始」に直結するのは`CountdownThenStart`だけなので、そちらだけ変更した
- 実測: `countdownDuration`/`startTextDuration`を一時的に伸ばしてPlayモードで確認。
  3-2-1の後に画面中央へ一つだけ大きく「START」が表示され、`startTextDuration`経過後に
  `InGame`へ切り替わることをスクリーンショットで確認済み。数字の3-2-1が従来通り
  プレイヤーごとに表示されることも別途確認済み

### UIは全てポーリング
`GameManager` のイベント購読はやめ、UI側が毎フレーム `CurrentState` などを見る方式に統一した。
Awake/OnEnable の実行順に依存して初期状態を取りこぼすバグを踏んだため。

### 「GameObjectごと非アクティブにしない」原則
表示のON/OFFは Text や Renderer の `enabled` で行う。
GameObjectを消すとそこに付いた制御スクリプトも止まり、二度と復帰できなくなる。
（`PlayerCarryLabel` と `PlayerStaffVisual` で実際に踏んだ）

---

## 5. 主要スクリプト

```
Assets/_Game/Scripts/
  Core/     GameManager, GameState, MatchTimer, MatchSettings, BGMPlayer, SEPlayer
  Player/   PlayerController, PlayerCombat, PlayerVisual, PlayerAnimatorDriver,
            PlayerFlight, PlayerStatusEffects, PlayerStaffVisual, PlayerBroomVisual,
            HeldItemPose, ThirdPersonCameraRig, HandType, StatusAura, SpeedUpEffect, StunEffect,
            PlayerHandIndicator, HandAdvantageIndicator, PlayerTauntController
  Items/    ItemDefinitionSO(抽象) → HandItemSO / ScrollEffectSO(抽象)
            ScrollEffectSO → SpeedBoost, Teleport, Reveal, Broom,
                             AreaScrollEffectSO(抽象) → StunTrap, HandScramble
            ItemPickup, ItemSpawnManager, ScrollStock, RevealMarker,
            ScrollRangeIndicator, BlinkTargetIndicator, CastEffect, SearchWaveEffect
  Lobby/    LobbyStartZone, LobbyMenuController, PlayerCarryLabel, ProximityLabel
  UI/       TitleUI, SelectionUI, InGameHUD, SceneCover, LobbyHUD,
            LobbySettingsPanel, LobbyControlsHelp, InGameOptionsMenu, OffscreenTargetArrow,
            TieBreakUI
  Respawn/  RespawnManager, RespawnPoint
  Stage/    WorldScaleTiling
Assets/_Game/Editor/
  MagicHandSceneBuilder.cs   シーン生成の本体
  BroomPosePreview.cs        搭乗ポーズを画像に描き出す確認用
Assets/_Game/Shaders/XRayMarker.shader
```

### 役割が紛らわしいもの
- `PlayerFlight` は**状態と数値**だけを持つ。Rigidbody を触るのは `PlayerController`。
  壁ずりと飛行の速度指定が別々に書き換えると、どちらが勝つか追えなくなるため
- `PlayerStatusEffects` は**時間制限のある効果の置き場**。HUDはここだけを見る。
  効果を足す側（各SO・`PlayerFlight`・`GameManager`）が登録する
- `HeldItemPose` は杖とほうきで共有する「手に持たせる姿勢」の計算

### アイテムの拡張方法（設計の核）
`ScrollEffectSO` を継承して `Apply()` を書き、アセットを作ってビルダーの抽選テーブルに足すだけ。
`PlayerController` / `GameManager` / `ItemPickup` / `ItemSpawnManager` は**一切変更不要**。

---

## 6. 数値と仕様（実測で確定済み）

### プレイヤー
| 項目 | 値 |
|---|---|
| 移動速度 | 7 |
| ジャンプ初速 / 追加重力 | 11 / 8（最高到達 約3.4） |
| スタン | 2秒 |
| 当たり判定 | カプセル(高さ2/半径0.5) + トリガー球(半径0.9) |
| あいこ接触 | 互いに弾く(力12/上5。**負け接触のノックバックと同じ値**。2026-08-22に7/3.5から統一)。**点で負けている側だけ**速度1.35倍を3秒 |
| 手を選ぶ時間 | リスポーン時のみ5秒。切れるとランダムで確定。**試合開始はUIすら出さずランダムで即決定** |
| リスポーン後の無敵 | 手を決めてから3秒（`respawnInvincibleDuration`） |
| 初期制限時間 | 5分（30秒刻みで1分〜10分に調整可） |

- 操作は**視点とキャラの向きが独立**。キャラは進行方向を向く。
  カメラ正対方式（フォートナイト型）はやめた。横歩き・後退用のアニメが無く、
  前進モーションが横向きに再生されて破綻したため
- **あいこでも接触が成立する**（以前は何も起きなかった）。決着は勝っている側だけが呼ぶが、
  あいこは勝ち負けが無くどちらからも同じ条件で呼ばれるため、**番号の小さい側だけ**が処理する。
  重なっている間は毎フレーム判定が走るので、0.8秒の間隔を空けて二重に弾かないようにしている。
  加速を負けている側に限るのは、一度離された側がそのまま終わるのを防ぐため。同点なら誰にも渡さない
- **試合開始は選ばせない**。`PlayerController.AssignRandomStartingHand()` が両者へ即ランダムな手を割り当て、
  `SelectionUI` はそもそも出さない（`IsSelecting` を立てないため）。
  既存の「全員が選択中でなくなったらカウントダウン→InGame」という `GameManager` の仕組み
  （`AreAllPlayersReady` → `CountdownThenStart`）がそのまま働くので、
  実装としては選択を経由せず最初からカウントダウンだけが見える形になる
- **手選択の5秒制限とリスポーン後3秒無敵は、リスポーンのときだけ**。
  `BeginSelection()` は現在このリスポーン経路からしか呼ばれないので、引数分岐は持たない
  （以前は開始時にも「時間無制限」で呼んでいたが、UIごと出さない方式に変えたので不要になった）
- **リスポーンで手を決めた直後は3秒無敵**（`GrantInvincibility`、`ConfirmHand` から必ず呼ばれる）。
  湧き先へ先回りされると復帰しただけで落とされる。効果一覧に「無敵」として残り時間が出る
- 接地中と空中で物理マテリアルを切り替える（`PM_PlayerGrounded` 摩擦0.6 / `PM_PlayerAirborne` 摩擦0）。
  空中に摩擦があると壁に張り付き、接地時に摩擦が無いと坂で滑る

### ステージ（60×60、外周の壁は高さ15）
- 2階: 上面 5.30（外周回廊・中央ハブ・橋4本）、地上からスロープ4本（約22度）
- 3階: 上面 10.30（対角2箇所の見晴らし台）、2階からスロープ2本
- ジャンプでは2階に届かないのでスロープ必須（到達3.4 < 5.30）
- 周囲に装飾のコロッセオ（半径46/50/54の3層、314枚）。**コライダーは全除去**

### アイテム
| グループ | 地点数 | 常時湧き | 補充間隔 |
|---|---|---|---|
| 手変更 | 25（1F 11 / 2F 10 / 3F 4） | 20（2026-08-22に10→20） | 10秒 |
| アイテム（スクロール＋ほうき） | 50（1F 23 / 2F 20 / 3F 7） | 20（2026-08-22に25→20） | 10秒 |

湧き位置は乱数の種を固定して散布（`Random.InitState(20260812)`）。再生成しても同じ配置になる。

- **ほうきは「アイテム」グループの保証枠**。専用グループには分けず、スクロールと同じ50箇所
  ・20体制のプールに入れつつ、`ItemSpawnGroup.guaranteedItem`（ほうき）/`guaranteedCount`(1) で
  「20個のうち常に1個はほうき」を保証する。抽選テーブル(`lootTable`)にはほうきを入れない
  （入れると保証ぶんと合わせて2本以上出ることがある）。
  補充時は `ItemSpawnManager.SpawnNextForGroup` が保証数を優先して埋め、
  満たしていれば通常のスクロール抽選に回す。一斉補充・毎フレーム補充・`RelocateAround` の
  3箇所すべてがこの1つのメソッドを経由するので、保証ルールを足す場所は1つで済む
- 準備ルームの設定パネル・見本表示では、ほうきもスクロールと同じ扱いで一覧に並ぶ
  （`RegisterLootWithSettings` が `lootTable` に加えて `guaranteedItem` も個別に登録する）
- **プレイヤーの半径7m以内には湧かせない**（`playerClearRadius`）。
  湧いた足元でいきなり拾えてしまうため。抽選の除外だけでは
  「先に湧いていた場所へ後からプレイヤーが現れる」場合を拾えないので、
  リスポーン直後に `RelocateAround` で周りのアイテムを消して別の場所へ湧かせ直す
  （総数は減らさない）
- **他のアイテムの半径8m以内も、できるだけ避ける**（`itemClearRadius`、2026-08-22追加）。
  手変更・アイテムのグループを問わず、既に湧いている全アイテムとの距離を見る。
  空いている地点のうち「他アイテムから遠い地点」を優先し、そういう地点が1つも無ければ
  諦めて普通に選ぶ（`playerClearRadius`と違い完全な排他条件にはしない。常に一定数を
  維持する方を優先するため、厳密な排他だと空き地点が枯渇して湧けなくなる恐れがある）。
  詳細: `ItemSpawnManager.PickFreePoint`/`IsNearAnyActiveItem`
- **ほうきに乗っている間はアイテムを拾えない**。空を自由に飛べる上に拾い集めまでできると、
  飛行が万能の手番になってしまう
- 実測（2026-08-22更新）: 試合開始直後で手変更20個・アイテム20個（うちほうき1）を
  `ItemSpawnManager`の内部状態から確認済み

### リスポーン地点（20箇所）
| 系統 | 数 | 位置 | 向き |
|---|---|---|---|
| 1F 外周リング | 8 | 半径20（障害物があれば内側へ最大8縮む） | 中心 |
| 1F 内周リング | 4 | 半径9（外周と角を半分ずらす） | 中心 |
| 2F 回廊 | 4 | 各辺、中心から26前後 | **通路に沿った向き** |
| 2F 橋 | 4 | 各橋の中央、中心から14 | 中心 |

- **壁際に湧かせない**のが最優先。カメラが壁にめり込んで前へ押し出され、
  自キャラが画面を埋めて手選択UIが読めなくなる。必要な余裕は カメラ距離5＋最小距離3＝8m
- 判定は**キャラとカメラの両方**（`IsRespawnSpotUsable`）。
  地点そのものが空いていても背後に遮蔽物があるとカメラが押し出されるので、
  背後5m（`CameraDistance`、`ThirdPersonCameraRig.distance` と揃える）も空きを見る
- 2F回廊だけ中心を向かせない。外壁まで4mしかなく、中心向きだとカメラが壁の外へ回り込むため。
  通路に沿わせればカメラも通路の中に収まる
- 1F外周が20mなのは2F回廊との兼ね合い。17mだと「相手が中央」のとき
  抽選のしきい値に届かず1階が候補から丸ごと外れた
- 2F は `SlideUntilClear` が向いている方向へ**最大18m**ずらして遮蔽物を避ける。
  回廊には3m角の `UpperCover_*` が12m間隔で並んでいて、
  体とカメラの両方を空けるには遮蔽物1つぶんを跨ぐ必要がある（8mでは足りなかった）
- 北・南の回廊は3階へのスロープを踏むので、辺の反対側の半分から探し始める
  （`Ramp3F_NE` が北の x 3.9〜17.1、`Ramp3F_SW` が南の x -17.5〜-4.5）
- 検証済み: 全20地点が「床がある・傾斜5度未満・体の空間が空く・カメラ位置に足場がある・
  キャラとカメラの間に遮蔽なし」を満たす

#### 選び方（`RespawnManager`）
- 「相手から**最も遠い**1地点」ではなく「最遠距離の `safeRatio`(0.6) 以上離れた地点から抽選」。
  最遠だけを採ると、マップ最外周の2F回廊が常に勝って実質4箇所しか使われなかった
- 実測: 相手の位置を変えて200回抽選したとき、使われる地点は6〜11箇所、
  2階が選ばれる割合は約半分、相手からの最短距離は20m以上を保つ

### スクロール5種
| 名前 | 効果 |
|---|---|
| スピードUp | 自分の移動速度1.5倍(5秒) |
| ブリンク | 前方14mへ瞬間移動 |
| スタン | 周囲5mの相手を1.5秒動けなくする |
| チャーム | 周囲8mの相手の手を**自分が勝てる手**に変える |
| サーチ | 10秒間、相手の位置が壁越しに見える |
| ほうき | 5秒間 自由に飛べる（下記。抽選は同じ枠だが1個保証） |

### ほうき（飛行アイテム）
「アイテム」グループの保証枠。25個のうち常に1個はほうき（詳細は §6 アイテム節）。

| 項目 | 値 |
|---|---|
| 飛行時間 | 5秒（**途中で降りられない**） |
| 水平速度 | 12.25（地上7の1.75倍。2026-08-22に1.25倍から変更） |
| 上昇・下降 | 7 m/s、R1=上昇 / L1=下降（キーボード Space / Shift） |
| 高度上限 | **基準床+14m**（試合は1階0→14、準備ルームは-100→-86） |
| 滑空 | 落下4 m/s固定、水平は飛行時の50%（6.125） |
| 着地後 | 速度25%＋位置が相手にバレる、3秒。一律 |

- 状態遷移は `PlayerFlight`（None → Flying → Gliding → 着地）。物理は `PlayerController` が握り、
  ここは「どの段階か・速度はいくつであるべきか」までを持つ。
  壁ずりと飛行の速度指定が別々に Rigidbody を書き換えると、どちらが勝つか追えなくなるため
- 飛行中は `useGravity=false`。高度は速度と位置の両方で上限に張り付かせる
  （速度だけだと上昇中に上限を跨いだフレームで行き過ぎる）
- **動けなくなったら飛行を打ち切る**（`CanAct` が false）。スタンを受けても飛び続けると
  重力を切ったまま空中で固まる。敗北時も同様に打ち切り、ペナルティは付けない
- 空中でも接触判定は有効。地上の相手は高度があれば物理的に届かない、という形で守られる
- 位置露出は索敵と同じ `RevealMarker` を**相手の `OwnViewLayer`** に置く（索敵は使用者側に置く）。
  相手にしか見えないので、本人にはHUDの `FlightText` で知らせる

#### 見た目
- モデルは**プリミティブの仮組み**（`CreateBroomModel`）。柄を+Y、穂を-Yにして杖と同じ向きに揃えてある。
  本物のアセットに差し替えるときは、この向きと全長1.6mさえ合わせれば他は触らずに済む
- 落ちている＝ほうき / 手に持つ＝杖と同じ持ち方 / 飛行中＝横座り（`PlayerBroomVisual`）
- 持ち歩きのモーションは長柄クリップを流用（`Armed` が true になるため自動）
- 上昇下降・旋回の手応えは1ポーズではなく**キャラごと傾けて**出す。
  傾けるのは見た目ルートだけ。本体を傾けると当たり判定のカプセルまで倒れて接地判定が狂う

#### 搭乗ポーズ `Broom_Ride.anim` の作り方（`CreateBroomRideClip`）
またぐ形は脚が柄を突き抜けて不自然だったため、**横座り**にしてある。
柄は進行方向へ伸ばしたまま、体だけ90度ひねり、顔は進行方向へ戻す。

作り方は「角度を計算で当てる」ではなく、**モデルを実際に組んでから丸ごと写し取る**。

1. 土台に `Spear_Halberd_Idle` をサンプリング（腕を下ろして柄を握った形が既にある）
2. `Main_Rig` を90度ひねり、`Head` はひねる前の向きへ戻す
3. 手と足を**ワールド座標で直に置く**（`ArrangeSideSaddlePose`）
4. 全ボーンの localPosition と localRotation をそのままクリップへ焼く

角度指定でやろうとして2回失敗している。理由は次の3つ。

- このモデルの休止姿勢は**腕が真横に伸びたTポーズ**。左右軸まわりに回しても腕が自分の軸で回るだけで下りない
- 脚はローブと一体で、動かせるのは**足先の塊だけ**。膝は曲げられない
- **クリップはボーンの位置まで動かす**。回転だけ写すと腕が休止姿勢のまま張り出したままになる

裏を返せば位置を動かせるので、手を柄の上に置くような調整は座標指定の方が確実。

#### ポーズの実測値（このモデル、足元を0とする）
| 部位 | 高さ |
|---|---|
| ローブの裾（Spine00） | 0.48 |
| 頭・帽子（Head） | 1.15〜1.88 |
| 手（Arm_L/R の塊） | 0.20〜0.51 |
| 足先（Foot_L/R） | 0.09〜0.25 |

柄の高さ `BroomSeatHeight` は **0.44**。裾(0.48)のすぐ下に通して腰かけて見せる。
0.28 まで下げると体と柄の間に20cmの隙間が空き、立ったまま柄が足元にあるだけの絵になった。

#### 姿勢を確認する道具
`BroomPosePreview.Capture(出力パス, カメラ位置)` で、編集モードのまま搭乗姿勢を1枚の画像に描き出せる。
数字を見ても姿勢は想像できないので、必ず描いて確かめること。
**出力パスは ASCII にすること**（日本語を含むパスは MCP のペイロードで壊れる）。

- 壁越し表示は専用シェーダー `MagicHand/XRayMarker`（ZTest Always）。
  URPのUnlitは `_ZTest` を公開していないため自作が必要だった
- マーカーは使用者のカメラだけが映すレイヤー（`P1Only` / `P2Only`）に乗せる。相手には見えない

---

## 7. 使用している外部アセット

### Modular Arena（`Assets/LoafbrrAssets/ModularArena`）
- 1マス=1m、壁は3m×3m×0.3m、すべてMeshCollider付き
- ステージの石材とコロッセオに使用。マテリアルは**複製して** `_Game/Materials` に置いている
- 引き伸ばしたプリミティブにそのまま貼るとテクスチャが伸びるので `WorldScaleTiling` で実寸に合わせている

### Free Low Poly Cubic Humans（`Assets/Shokubutsu Studio/...`）
- URP版プレハブを使用（`URP/Prefabs/Characters/Mages/Mage_01・02`）
- **骨格は7本だけ**: `Main_Rig → Foot_L, Foot_R, Spine00 → Arm_L, Arm_R, Head`。
  **手のボーンが無い**ので、杖は `Arm_R` に手動オフセットで取り付けている
- FBXは元々 Generic・アバター未生成だった。ビルダー実行前に
  `animationType=Generic` / `avatarSetup=CreateFromThisModel` / `motionNodeName=Main_Rig` で
  再インポートしてアバターを生成済み（`Mage_01Avatar`）
- アニメーションは Generic。クリップのパスとプレハブ階層が一致しているのでそのまま再生できる

#### アニメーションの在庫と割り当て
| 状態 | Idle | Walk | Run |
|---|---|---|---|
| 素手 | Staff/Mage_Idle（代用） | NPC/NPC_Walk | NPC/NPC_Run |
| 杖持ち | Spear and Halberd/Spear_Halberd_Idle | 同/Spear_Halberd_Walk | 同/Spear_Halberd_Run |

- **素手のIdleはアセットに存在しない**ため魔法使いの待機で代用している
- 杖持ちに `Staff/Mage_*` を使ってはいけない。**杖を掲げる前提のモーション**で、
  走行中の `Arm_R` が 1.14〜1.29 まで上がり、立てて持たせている杖と噛み合わない
- 長柄（Spear and Halberd）は待機・歩き・走りすべてで腕が 0.47〜0.61 に収まり、
  走行中の腕の横ぶれも 0.116m と小さい（NPC_Run は前後に 1.263m 振れる）。
  長い柄を体の横に立てて持つ動きなので杖と相性がよい
- 被弾は `Hit/Damage_Hit_01`。未使用の `Damage_Die` / `Damage_Standup` があり、
  スタン2秒に合わせるならこちらへ差し替える案がある
- ジャンプのクリップは無い。`Grounded` パラメータは用意してあるが**まだ未使用**

#### 各クリップの `Arm_R` 世界Y（実測・キャラ身長1.92m）
| クリップ | 腕の高さ | 用途 |
|---|---|---|
| Mage_Idle | 0.27〜0.40 | 素手の待機 |
| Mage_Walk | 0.34〜0.44 | （不使用） |
| Mage_Run | **1.14〜1.29** | 腕を上げてしまうので不使用 |
| NPC_Walk | 0.38〜0.70 | 素手の歩き |
| NPC_Run | 0.40〜0.92 | 素手の走り |
| Spear_Halberd_Idle/Walk/Run | 0.46〜0.61 | 杖持ち全般 |

#### テクスチャの構造（服の色替えで必要になった知見）
- 体全体が**1メッシュ・1マテリアル**（`Texture_A` 512×512のアトラス）。
  色を乗算すると肌まで染まるため、手ごとにテクスチャを作り分けている
- **肌の判定は色距離ではなく HSV で行う**（`IsSkinPixel`）。
  色相5〜35°・彩度0.10〜0.55・明度0.55以上。
  距離方式は失敗した: 肌 `#F0BAA6` からの距離のヒストグラムに切れ目が無く、
  灰色の布（201,201,201＝距離54）まで肌に混ざる。
  彩度0の布と、明度0.5未満の茶色（靴・木・髪）はHSVなら確実に外れる
- 彩度だけでも分けられない: Mage_01 のローブは無彩色だが Mage_02 は青い
- 衣装は元の色を**明度に落としてから**手の色を乗せる。単純な乗算だと青ローブが濁るだけで色が変わらない
- 出力先 `_Game/Textures` は初回は存在しない。`File.WriteAllBytes` で直接書くので
  **先に `AssetDatabase.CreateFolder` が必要**（これを忘れて例外でビルドが止まっていた）

### 巻物モデル（`Assets/cgtrader_optimized_r1.fbx`）
- 拾えるスクロールの見た目。ルートが紙（mesh `Roll paper` / `Material.061` タン色）、
  子7つが紐（`Material.060` 焦げ茶）
- 元の長さ1.639m。ビルダーが実測して長さ0.7mへ正規化する（`ScrollVisualLength`）
- **色は付けない**。巻物は地の色のまま出す。中身は拾うまで分からない方がよく、
  色分けはHUDが受け持つ。手変更アイテム（グー/チョキ/パー）も2026-08-22以降は
  専用モデル（`Assets/Te`）に差し替わり色染めをやめたため、`ItemPickup` に
  色替えの仕組み自体が残っていない。§7-12
- 傾きは `ScrollVisualTilt`(28, 0, 12)。**長軸がZなので傾けるのはX軸まわり**。
  Z軸まわりに回しても円筒が自転するだけで見た目が変わらない
- 大きさを測るときは傾きを一度戻してから測る。傾けたままだとAABBが膨らんで縮尺がずれる

---

## 7-2. HUD の構成

分割画面で横幅が実質半分しかないので、大事な情報ほど大きく、色を伴わせている。

| 表示 | 場所 | 内容 |
|---|---|---|
| 今の手 | 左下 | アイテム枠と同じ「枠＋アイコン＋名前」（2026-08-22〜）。§7-12 |
| アイテム枠 | 右下 | マリオカート風。アイコン＋下に名前。**空でも枠は消さない** |
| かかっている効果 | 枠の上に4行 | 残り時間の長い順。下ほど長い |
| 飛行／滑空中 | 効果一覧の上 | 滑空は高度次第で終わりが決まらず、タイマーで表せないため別枠 |

### 効果の残り時間は `PlayerStatusEffects` に集約する
HUD から `PlayerController` のタイマーを直接覗く作りにはしていない。タイマーは private なうえ、
サーチは残り時間が `RevealMarker` 側にあり、効果が増えるたびに HUD を直す羽目になるため。

- 効果を掛ける側（各 `ScrollEffectSO`、`PlayerFlight`）が `Apply(識別子, 表示名, 秒数)` で登録する
- **妨害（スタン・減速）は掛けられた側に登録する。** 表示は「自分が今どうなっているか」を伝えるもの
- 同じ識別子を掛け直したときは**残りが長い方**を採る。短い効果を重ねられて長い効果が消えないように
- `IsActive(識別子)` は演出の判定にも使う（画面外の矢印はサーチの登録を見ている）

### アイテムのアイコン
`ItemDefinitionSO.icon`（Sprite）。**コードで描いて** `_Game/Textures/Icons/` に書き出す（`CreateIcon`）。
外部素材を足すたびにスケール・ピボット・マテリアルの調整で手戻りが出ているため。
64×64で、正規化座標に対する図形の式で判定している（`IsInsideShape`）。
アイコンが無いアイテムでも壊れないよう、null なら色だけの四角で代替する。

### ブリンクの着地点表示
`BlinkTargetIndicator`。着地点は必ず `PlayerController.ResolveTeleportTarget` から取る。
別々に計算すると表示と実際がずれ、嘘の情報になる。実測で水平誤差 0.000 を確認済み。
プレイヤーの子にはしない（着地点は本人から離れた場所に出るため）。レイヤーは `P1Only`/`P2Only`。

### 画面外の相手を指す矢印
`OffscreenTargetArrow`。相手が画面外にいて、かつ位置を見てよい場面のときだけ画面の縁に赤い矢印を出す。
見てよい場面は2つある。**自分がサーチを使っている間**と、**相手がほうきで着地して位置を晒している間**。
どちらも壁越しマーカーが出ているので、矢印はそのマーカーを画面内に導く案内になる。
着地の露出は3秒しかなく、画面外に居られると気づかないまま終わってしまうため後から足した。

**背後の扱いに注意。** `WorldToViewportPoint` はカメラの後ろの点にも座標を返すが**上下左右が反転している**。
`z < 0` のとき方向ベクトルを反転させないと、真後ろの相手に対して正反対の縁に矢印が出る。
実測で 背後(z=-13.3) / 右(x=3.41) / 左(x=-2.63) それぞれ正しい縁と角度になり、
画面内(x=0.50, y=0.62)で消えることを確認済み。
着地露出でも同じ挙動を実測（相手が画面外で矢印ON、画面内(0.45, 0.62)に入れてOFF）。

---

## 7-3. 準備ルームの見せ方

俯瞰カメラ1台を2人で共有する。位置は「注視点＋俯角＋距離」で決める。
高さで決めると、俯角を変えたときに見える大きさまで一緒に変わってしまうため。

| 項目 | 値 |
|---|---|
| 俯角 `LobbyCameraPitch` | 45度 |
| 距離 `LobbyCameraDistance` | 24m |
| 注視点 `LobbyCameraFocus` | (0, 0, -7) |
| 床 | ±34（壁は±14） |

**この3つは連動している。** どれかを変えるときは次の両方を必ず確かめること。

- 手前: 湧き位置(z=-12)と開始の円(z=-10)が写るか。画面下端が地面に当たるのは z=-14.4
- 奥: スロープ上の台（一番奥・高い点が x=-9, y=5.30, z=14）が写るか。
  台は俯角18.9度の方向にあり、画面上端の15度に対して3.9度の余裕がある

### 手前の壁は描かない
南の壁だけ `CreateInvisibleWall` で**描画を切り、当たり判定だけ残す**。
俯瞰カメラは南の外側から見下ろすので、描くと高さ16mの壁が部屋を丸ごと隠す。
準備ルームを映すのはこのカメラだけなので、無くなったことは他の角度からも見えない。

### 床は壁より広い
±14 の部屋ぴったりだと、画面の下端と左右の隅で床が尽きて背景の空が見える。
画面端のレイは x±30 / z-14 まで届くので、床は ±34 まで敷いてある。
引き伸ばしたぶんは `ApplyTiling(floor, 10f, false)` でテクスチャを実寸に戻す。
検証は画面を11×11で走査して「空だった点=0/121」を確認する方法が速い。

### 設定パネル
- 制限時間は **1:00〜10:00 を30秒刻み**（`durationMin/Step/Max` = 60 / 30 / 600）。初期値5:00
- **手変更（グー/チョキ/パー）はON/OFFの一覧に出さない**。
  `ItemSpawnGroup.includeInSettings`（HandItemsグループだけ false）で、
  `RegisterLootWithSettings` の登録対象から外している。手変更は勝敗を決める基本アイテムで
  無効化する意味が無く、一覧を無駄に長くするだけだったため。湧き自体には影響しない
  （常に有効として扱われるだけで、25箇所/10体制のスポーンはそのまま動く）
- 行数は**設定に出るアイテム数から計算する**（`5 + itemCount + 1`）。直書きすると増えた分が消える
  （12種になったとき8種ぶんの13行のままで、末尾3種と「戻る」が出ていなかった）。
  `itemCount` はビルダーの `lobbyItems.Count`（スクロール＋ほうき、現在7）を渡す。
  以前は手変更ぶんも足していたが、一覧に出ない種類の分まで行を確保する意味が無いので外した
- 背景はパネル本体ではなく子に置き、**使っている行の下端まで縮める**（`ResizeBackground`）。
  行の枠は最大数ぶん確保してあるので、畳んでいるときに背景まで伸びると空の板が残る
- 「操作説明」の行は1P・2Pどちらからも開ける。表示は1つなので**どちらかが開いていれば出す**。
  読むだけなので取り合いにならない。位置はアイテム設定より前（後ろだと展開時に埋もれる）

---

## 7-4. リザルトへの遷移

勝敗の表示は **`Victory` シーン**が受け持つ。`MainScene` 側には結果画面を置かない。

`SceneManager.LoadScene` はその場でシーンを差し替えず、**呼んだフレームの Update と描画は最後まで走る**。
以前はここで `ResultUI` の結果パネルが1フレーム出てから `Victory` に切り替わり、
別の画面が一瞬映り込んでいた。`ResultUI` と `ResultPanel` は削除し、
代わりに全面を黒で覆う `SceneCover` を用意して、遷移を決めた瞬間に被せている。

- `UI_Global` は `ScreenSpaceOverlay`（sortingOrder 10）なので、
  `ScreenSpaceCamera` の分割画面HUDより必ず前に出る
- 覆いは出すたびに `SetAsLastSibling` で最前面へ回す（後から生成されたUIが上に来ることがあるため）
- 再戦は `VictoryManager.ReturnToReadyScene` が `MainScene` を読み直す。
  そのため `MainScene` 側の再戦ボタンは不要になった

---

## 7-5. 音（BGM / SE）

音源は `Assets/_Game/BGM/`（BGM本体）と `Assets/_Game/BGM/SE/`（効果音）に置く。
どちらもファイル名に日本語・全角括弧・スペースを含むが、`AssetDatabase.LoadAssetAtPath` は
実際のファイルパスと一致してさえいれば問題なく読み込める。

### BGM（`BGMPlayer`）
| 画面 | クリップ | シーン |
|---|---|---|
| タイトル | `game start.mp3` | MainScene |
| 準備ルーム〜サドンデス | `game play.mp3` | MainScene |
| リザルト | `game end.mp3` | Victory（`VictoryManager` が直接再生） |

- MainScene側は `AudioSource` 1つ（`BGMPlayer`）で足りる。
  `GameManager.CurrentState` を見て「タイトルか、それ以外か」で曲を出し分けるだけなので、
  複数の `AudioSource` を切り替える必要が無い
- 曲の切り替えは**変わったフレームだけ** `Stop→Play` する。毎フレーム同じ曲を鳴らし直すと、
  そのたびに頭出しされて音楽が途切れて聞こえるため、`current` フィールドで前回のクリップを覚えておく
- Victoryは`MainScene`と別シーンなので `BGMPlayer` の管轄外。`VictoryManager.Start()` が
  自分の `GameObject` に `AudioSource` を実行時に追加して鳴らす（シーン側に手作業で
  `AudioSource` を置く必要が無い。クリップの参照だけはシーンに保存されている）
- 音量はビルダーの `BgmVolume` 定数（現在 0.01）と `VictoryManager.bgmVolume`
  （Victoryシーンに保存済みの値、同じく0.01）の両方を合わせて変える必要がある

### SE（`SEPlayer`）
| イベント | クリップ | 呼び出し箇所 |
|---|---|---|
| タイトルでSTART決定 | 決定ボタンを押す3（スタートボタンの音） | `TitleUI.OnStartClicked` |
| 手をランダムで決めた直後のカウントダウン開始 | カウントダウン電子音（ゲーム開始時のカウントダウン） | `GameManager.CountdownThenStart` |
| 勝ち手で接触・相手が倒れた瞬間 | スタジアムの歓声2（相手にぶつかった時） | `GameManager.ResolveContact` |
| あいこで接触・弾かれた瞬間 | ロボットを強く殴る2（あいこの時） | `GameManager.ResolveDraw` |
| アイテム取得 | 食べ物をパクッ（アイテム取得音） | `ItemPickup.OnTriggerEnter` |
| スタン発動 | 足首がグキッ（スタン） | `StunTrapEffectSO.PlayActivationSound`（override） |
| ブリンク発動 | 俊敏15（ブリンク） | `TeleportEffectSO.Apply` |
| スピードUP発動 | 俊敏11（スピードアップ） | `SpeedBoostEffectSO.Apply` |
| チャーム発動 | 回想（チャーム） | `HandScrambleEffectSO.PlayActivationSound`（override） |
| ほうき発動 | シャキーン2（ほうきを装備） | `BroomEffectSO.Apply` |

- SEはBGMと違い**重なって鳴っても構わない**（アイテムを連続で拾う、あいこと同時にアイテムを拾う等）ので、
  `AudioSource` 1つに対して `PlayOneShot` で積み重ねる方式にしてある。
  クリップを差し替えて `Play()` するBGM方式だと、鳴っている途中の音が次の音で止まってしまう
- 呼び出し側は `SEPlayer.PlayXxx()` という名前付きの静的メソッドを呼ぶだけで、
  クリップの割り当てはビルダー（`BuildSePlayer`）に集約してある
- カウントダウンは実装上2段階ある（準備ルームでの `CountdownThenSelection` →
  Selectionでの `CountdownThenStart`）。SEは**2段目の`CountdownThenStart`だけ**で鳴らす。
  1段目は「両者が開始地点に乗って揃うまでの待ち」で、手はまだ決まっていない。
  2段目こそが「手をランダムで決めた直後、試合開始に直結するカウントダウン」に当たるため、
  ユーザーの言う「はじめのカウントダウン」はこちらを指す
- サドンデスの決着点（`IsSuddenDeath` で即 `Result` へ行く分岐）は `ApplyDefeat` を呼ばないので、
  倒れたSEは鳴らない。ノックバック演出自体が無い決着なので、狙い通りの挙動
- 実測: `SEPlayer.PlayXxx()` の直後に `AudioSource.isPlaying` が `true` になることを10種すべてで確認。
  `ResolveContact`／`ResolveDraw`／`ItemPickup.OnTriggerEnter` の実際の呼び出し経路からも
  同様に発火することを確認済み

### スクロール発動音（5種）の配線先が分かれている理由（§7-11で詳述）
`AreaScrollEffectSO.Apply()` が `sealed override` のため、範囲系（スタン・チャーム）は
共通クラス側に足した `protected virtual void PlayActivationSound()` フックを個々のサブクラスで
上書きする形になり、非範囲系（ブリンク・スピードUP・ほうき）は各 `Apply()` の先頭に直接
`SEPlayer.PlayXxx()` を書いている。呼び出しタイミングの違いではなく、C#の言語制約（sealed）が
理由なので、新しい範囲系アイテムを足すときは前者、それ以外は後者の形に倣うこと。

---

## 7-6. アイテム発動エフェクト（`CastEffect`）

アイテム（スクロール）を発動した瞬間、使用者の足元に輪が一瞬で広がって消える演出。
色はアイテムの `DisplayColor` をそのまま使うので、新しいアイテム種別を足しても
専用の実装は要らず、色だけで自動的に見分けが付く。

- フック先は `ScrollStock.TryUse()` 一箇所。ここが全種類（スピードUp・スタン・ブリンク・
  チャーム・サーチ・ほうき）共通の発動口なので、個々の `ScrollEffectSO` 側は一切変更不要
- 見た目は `ScrollRangeIndicator`（足元の効果範囲円）と同じ「ローカルXY平面を90度倒して
  地面に寝かせる」`LineRenderer` 方式。透明度ではなく **太さを0へ細らせて消す**
  （共有マテリアルの Surface Type を Transparent にする必要が無く、他の輪表示にも影響しない）

### ハマった罠（2つ、どちらも実測で特定）

1. **`ScrollStock` が2重に生成されていた**: `PlayerController` に
   `[RequireComponent(typeof(ScrollStock))]` が付いているため、`root.AddComponent<PlayerController>()`
   の時点で ScrollStock が自動で1つ付く。そこへ気づかず `root.AddComponent<ScrollStock>()` を
   明示的に呼んでいたため、同じ GameObject に ScrollStock が2つできてしまい、
   **配線した方（2つ目）とゲームが実際に使う方（`GetComponent` が拾う1つ目）がズレて**、
   実行時には常に `castEffectPrefab` が null に見えていた。
   `PlayerFlight` は元から「無ければ足す」形（`GetComponent` → null なら `AddComponent`）で
   この罠を避けていたので、`ScrollStock` も同じ形に直した。
   **教訓**: `[RequireComponent]` が付いている型を `BuildPlayer` 内で明示的に `AddComponent`
   する前には、既に付いていないか確認すること。`FindObjectsByType<T>()` で期待より多い数が
   返ってきたら、まずこれを疑う
2. **`EditorSceneManager.NewScene()` をまたぐと `SaveAsPrefabAsset` で作った直後の
   プレハブ参照が null に見えることがある**: `enemyMarker`（`CreateRevealMarkerPrefab` で
   同じ手順で作る）は平気なのに `castEffectPrefab` だけ壊れるという再現する現象を実測で確認したが、
   両者の作り方の違い（Cube+Collider除去 vs LineRenderer直付け）のどこが効いているかは
   特定できていない。原因追及より確実さを優先し、**`NewScene()` の直後に
   `AssetDatabase.LoadAssetAtPath` でパスから読み直す**ことで回避した
   （`BuildScene` 内、`NewScene()` の直後）

---

## 7-7. 効果発動中エフェクト（`StatusAura`）

`CastEffect`（発動の瞬間だけの演出）とは別に、効果が**続いている間ずっと**足元に輪を出し続ける演出。
スタンや減速は動きの変化だけでは分かりにくく、原因が伝わらないまま試合が進んでしまうため。

- フック先は `PlayerStatusEffects.Apply(id, label, duration, color)`。時間制限つきの効果は
  すべてここを通る（自己バフのスピードUp・サーチだけでなく、相手にかけるスタン・サーチ済み通知、
  ほうき着地の減速・露出、あいこの追い上げ、リスポーン無敵も含む8箇所すべて）ので、
  1回の変更で全種類に効かせられる
- `Apply` に `Color` 引数を追加した。アイテム由来のものは `ScrollEffectSO.DisplayColor` を
  そのまま渡す。アイテムに紐づかないもの（追い上げ・無敵・ほうき関連）は意味に合わせた色を
  その場で指定した（例: 追い上げ＝オレンジ、無敵＝金、露出＝赤）
- 複数の効果が重なっているときは、**残り時間が最長（HUD一覧の先頭）** のものの色を使う
  （`PlayerStatusEffects.TryGetPrimaryColor`）。現在の除外リストは
  **スピードUp・スタン・サーチされている**（それぞれ§7-8/§7-9/§7-10と後述の理由による）。
  除外対象しかかかっていなければ輪は出さない
- `ScrollRangeIndicator`（発動前の「今持っている」表示）と違い、**相手にも見えていい**ので
  レイヤーは絞らない。妨害を受けている本人だけでなく、かけた側にも効いているのが見える
- 見た目は `CastEffect`/`ScrollRangeIndicator` と同じ「地面に寝かせた輪」だが、消えずに
  ゆっくり回転し続ける（`transform.Rotate(Vector3.forward, ..., Space.Self)`。地面に倒した
  ローカルZが鉛直軸にあたるので、これを軸に回すと輪が水平に回って見える）
- 実測: `PlayerStatusEffects.Apply` で長時間の効果を直接付与し、`LineRenderer.enabled` が
  `true` になり色が指定色と一致すること、`Quaternion` が経時変化して回転していること、
  `Clear()` で効果が消えると同時に `enabled` が `false` に戻ることを確認済み

---

## 7-8. スピードUp専用エフェクト（`SpeedUpEffect`）

「スピードUpは自身の周りに小さくいくつか上矢印が出て疾走感が出るように」という依頼で、
汎用の `StatusAura`（足元の輪）から**スピードUpだけ**専用演出へ差し替えた。

- 自分の周りに5本の小さな上矢印（`LineRenderer` 3点の山形 `^`）を輪状に配置し、
  それぞれ位相をずらしながら下から上へ駆け上がって消える動きを繰り返す
- 矢印は `LineAlignment.View` にしてあるので、分割画面のどちらのカメラから見ても
  常にこちらを向く（`CastEffect`/`StatusAura` の「地面に寝かせる」方式とは別の見せ方）
- 消え際の表現も `CastEffect` と同じ**太さ0へのフェード**方式。`sin(progress * π)` を
  幅の係数にすることで、駆け上がりの最初と最後で自然に幅0になり、透明度を使わずに
  「現れて→駆け上がって→消える」動きを表現している
- `StatusAura` 側は `PlayerStatusEffects.TryGetPrimaryColor(out color, 除外id...)` を使うことで、
  スピードUpだけが掛かっているときは輪を出さない。スピードUp＋他の効果が同時に掛かっていれば、
  輪（他の効果用）と矢印（スピードUp用）が両方同時に表示される
- 実測: 長時間のスピードUpを直接付与し、5本すべてが有効・異なる高さ・異なる幅（フェード中）で
  動いていること、色がスピードUpの色と一致すること、スピードUp単体では `StatusAura` の輪が
  出ないこと、他の効果と併用すると輪（除外後の色）と矢印が両方出ることを確認済み

---

## 7-9. スタン専用エフェクト（`StunEffect`）

「スタンはビリビリとした雷に感電したようなエフェクトに」という依頼で、スピードUpと同じ形で
`StatusAura` からスタンだけ専用演出へ差し替えた。

- 体の周りのランダムな位置に、ジグザグの稲妻（`LineRenderer` 5点の折れ線）を最大5本まで出す
- `SpeedUpEffect` の矢印が**なめらかで規則正しい**動きなのに対し、こちらは**わざと不規則**にした。
  各稲妻は0.03〜0.12秒ごとに独立して「出すかどうか」を再抽選し（`showChance`＝60%）、
  出すたびに位置・角度・ジグザグの形も乱数で作り直す。感電特有のビリビリした明滅感はこの
  「毎回バラバラ」から来ている
- `PlayerStatusEffects.TryGetPrimaryColor` の除外リストにスタンも追加。スピードUp・スタンの
  どちらも掛かっていなければ `StatusAura` の輪を出す、という条件になった
- 実測: 同時刻に一部の稲妻だけ点灯・別の稲妻は消灯という不規則な状態を確認し、
  少し時間を置いて再確認すると点灯パターンが入れ替わっていること（＝明滅している）を確認。
  点灯中の稲妻の座標がXだけランダムに揺れながらYが直線的に下降するジグザグ形状になっていること、
  色がスタンの色（紫、`StunTrapEffectSO.DisplayColor`）と一致すること、
  スタン単体では `StatusAura` の輪が出ないことを確認済み

---

## 7-10. サーチ専用エフェクト（`SearchWaveEffect`）

「使用者から3D同心円状に周りをスキャンしているような波動を出し、距離が進むほど薄くなる」
という依頼で追加した、サーチ専用の演出。これまでの `StatusAura`/`SpeedUpEffect`/`StunEffect`
（発動中ずっと出続ける）とは違い、`CastEffect` と同じ**発動の瞬間だけ**の一発演出。
ただし `CastEffect`（全アイテム共通、地面に1枚の輪）を置き換えるのではなく**追加**で出す。

- フック先は `RevealEffectSO.Apply()`（サーチ専用の効果クラス自身）。全アイテム共通の
  `ScrollStock.TryUse()` ではなく、あえてサーチだけに閉じた場所に実装した
- 使用者の胸の高さ（足元+1.1m）を中心に、**互いに直交する3枚の輪**（前後・左右・上下の
  各平面）を同時に同じ半径で広げる。1枚だけだと「地面の波紋」に見えてしまうが、
  3枚を組み合わせることで特定方向だけでなく全方位に球状に広がっているように見える
  （＝依頼の「3D同心円」）
- 「距離が進むほど薄くなる」は、`CastEffect` と同じ**太さを0へ細らせる**手法で表現。
  半径が最大（27m。初期値9mの3倍に変更）に近づくほど太さが0に近づくので、
  遠くまで届いた輪ほど薄く見える（透明度は使わない。既存方針を踏襲）
- プレハブはビルダー内 `CreateSearchWaveEffectPrefab` で、3枚の子 `LineRenderer`
  （回転 `identity`／`(0,90,0)`／`(90,0,0)`）を持つ1つの GameObject として作る。
  `RevealEffectSO.wavePrefab` への配線は `NewScene()` をまたぐ前（`CreateScrolls` の中）で
  完結させているため、`CastEffect` で踏んだ「NewScene()をまたぐと参照が壊れる」罠は
  そもそも発生しない
- 実測: 発動すると波動が生成され、位置が使用者の足元+1.1mと一致すること、3枚の輪の
  ローカル回転がそれぞれ `(0,0,0)`/`(0,90,0)`/`(90,0,0)` になっていること、半径が時間とともに
  拡大しながら太さが0へ近づくこと、色がサーチの色（赤）と一致すること、`CastEffect`（共通演出）
  と同時に両方生成されることを確認済み

---

## 7-11. スクロール発動音（5種）とあいこノックバックの統一（2026-08-22）

依頼: 「魔法発動オンをつけたい。一つ一つ音声が違う」＋「あいこでぶつかったときはどちらも負けの時と
同じように吹っ飛ぶように」。作業当日の会話終盤に来た依頼で、**翌日は別PCのClaude Codeで続ける**
という明示指示があったため、この節はやや厚めに書いてある。

### 発動音の割り当て
`Assets/_Game/BGM/SE/` から5つを個別に割り当てた（音源ファイルは元から存在していたものを使用、
新規追加はしていない）。対応表は §7-5 のSE表を参照。

- `SEPlayer.cs` に `stunClip`/`blinkClip`/`speedUpClip`/`charmClip`/`broomClip` の5フィールドと
  `PlayStun()`/`PlayBlink()`/`PlaySpeedUp()`/`PlayCharm()`/`PlayBroom()` の5メソッドを既存5種と
  同じパターンで追加
- **配線が2パターンに分かれる理由は `AreaScrollEffectSO.Apply()` が `sealed override` なこと**。
  範囲系（`StunTrapEffectSO`＝スタン、`HandScrambleEffectSO`＝チャーム）は共通の `Apply()` を
  直接触れないため、`AreaScrollEffectSO` に `protected virtual void PlayActivationSound() { }` を
  新設し、`Apply()` の先頭（対象ループに入る前）で無条件に呼ぶようにした。各サブクラスは
  これを `override` して自分のSEを鳴らす。**「当たり音」ではなく「発動音」**なので、範囲内に
  対象が1人もいなくても空振りで鳴る設計にしてある（それが依頼の「魔法発動音」の意図に合う）
- 非範囲系（`TeleportEffectSO`＝ブリンク、`SpeedBoostEffectSO`＝スピードUP、`BroomEffectSO`＝ほうき）は
  各自の `Apply()` が `sealed` ではないので、フックを増やさずそのまま `Apply()` の先頭に
  `SEPlayer.PlayXxx()` を直書きした
- サーチ（`RevealEffectSO`）は今回の依頼リストに含まれていないため、専用の発動音は追加していない
  （既存の `SearchWaveEffect` 視覚演出はそのまま）
- ビルダー側は `MagicHandSceneBuilder.cs` の `BuildSePlayer` に5つの `AudioClip` ロード＋
  `SetObject` 呼び出しと、5つの `SeXxxPath` 定数を追加しただけ。既存5種と全く同じ形

### あいこノックバックの統一
`GameManager.ResolveDraw` が使う `drawBounceForce`/`drawBounceUpForce` を、`PlayerController` の
負け接触ノックバック値 `knockbackForce`(12)/`knockbackUpForce`(5) と同じ値に変更した
（旧: 7/3.5）。**2つのフィールドは別クラスにある別々の値のままで、片方を書き換えて数値を
揃えただけ**（読み取り元を共通化する設計変更はしていない。あいこは無敵判定・スタン・リスポーンを
一切伴わない別の仕組みなので、値だけ合わせれば依頼の「同じように吹っ飛ぶ」を満たせると判断した）。
今後どちらかの数値だけを変えると再びズレるので、ノックバックの強さを調整するときは
両方（`GameManager.drawBounceForce`/`drawBounceUpForce` と `PlayerController.knockbackForce`/
`knockbackUpForce`）を見比べること。

### 検証方法
`SEPlayer.PlayXxx()` を直接呼んで `AudioSource.isPlaying` を確認、加えて
`StunTrapEffectSO`/`HandScrambleEffectSO` のアセットを `AssetDatabase.LoadAssetAtPath` で読み、
`.Apply(user)` を直接呼んで sealed 経由でも発火することを確認（sealed の override 呼び出しは
静的な目視だけでは間違えやすいので実行確認が必須）。あいこ側は `SerializedObject` で
`drawBounceForce`/`drawBounceUpForce` が 12/5 になっていることを確認した
（`AddForce(..., ForceMode.VelocityChange)` は次の物理ステップまで速度に反映されないため、
呼んだ直後に `Rigidbody.linearVelocity` を読んでも 0 のままなのは正常。慌てて「効いていない」と
誤診しないこと）。

### この日にMCP経由の作業で新たに踏んだ罠
- **`MagicHandSceneBuilder` の名前空間は `MagicHand.EditorTools`**（`MagicHand.Editor` ではない）。
  `Type.GetType("MagicHand.Editor.MagicHandSceneBuilder, Assembly-CSharp-Editor")` は静かに null を返す。
  名前空間を決め打ちせず、`AppDomain.CurrentDomain.GetAssemblies()` を総当たりして
  `t.Name == "MagicHandSceneBuilder"` で探すか、`typeof(MagicHand.EditorTools.MagicHandSceneBuilder)`
  を直接書くこと
- **`BuildScene()` は `public static` なので、リフレクション経由で呼ぶ必要が無い。**
  `MagicHand.EditorTools.MagicHandSceneBuilder.BuildScene();` と直接書いた方が
  Mono.CSharpの評価器が安定する。今回、リフレクション経由（`GetMethod`→`Invoke`）の呼び出しは
  `resultSet:false` を繰り返した末に**実は一度も実行されていなかった**
  （§3の「古いアセンブリでBuildSceneが走る」とは別の失敗パターン: アセンブリは最新なのに
  呼び出し自体が握りつぶされていた）。`MainScene.unity` のファイル更新日時が古いままなのが
  発覚のきっかけだった。**`BuildScene()` を呼んだ直後は、必ずシーンファイルの更新日時か
  実際のフィールド値（`SerializedObject`）で「本当に走ったか」を確認すること。
  `resultSet:true` で返ってきたことだけでは信用しない**
  （今回は `result:"DONE"` かつログ出力 `[MagicHand] シーンを生成しました` が
  同時に返ってきて初めて確実だと判断できた）
- `FindObjectsByType<MagicHand.PlayerController>(FindObjectsSortMode.None)` のような
  カスタム型を渡すジェネリック呼び出しが、このセッションでは何度リトライしても
  `resultSet:false` のままだった（§3に既出の制約の一種と思われるが、今回は名前付き
  `GameObject.Find("Player1")` に切り替えて回避した。**ジェネリックで詰まったら、
  名前検索など別の経路に逃げる方が早い**）

---

## 7-12. 手変更アイテムの専用モデル化／相手の手を頭上に表示／自分の手表示の枠化（2026-08-22）

依頼: 「今手を変えるのは丸だけれど `Assets/Te` の中にあるものに置き換え（グー＝`gu-.prefab`／
チョキ＝`choki.prefab`／パー＝`pa-.prefab`）」＋「相手の手を色以外で見分けれる機能。相手の頭上に
グー/チョキ/パーを表示し、自分からは見えず相手の手だけ見えるように」＋「自分の手の表示も、
アイテムのような枠にして文字/アイコンでひと目に分かるように」。

### `Assets/Te` の3モデルの中身（そのまま使う。分解しない）
3つとも「元のシーンでの縮尺・位置をそのまま prefab 化したもの」で、中身も互いに構造が違う。

- `gu-.prefab`: 岩の塊9個（`ST_Stone5` の使い回し）＋中心の小さなメッシュ1個。**拳の形**に
  見えるよう配置済みで、ばらすと意味を失う。ルート scale 5、ワールド寄りの座標が焼き込まれている
- `choki.prefab`: `Sword3`（剣）を2本、角度を変えて交差させたもの。**交差する刃＝はさみ**に
  見せる意図。影が落ちるとXの形になる（実測で確認済み）
- `pa-.prefab`: 単一メッシュ（開いた本）。ルート scale 0.1

3体で元の縮尺がバラバラ（5倍／2倍／0.1倍）なので、数値をそのまま使うと大きさが揃わない。
`CreateScrollVisual`（巻物の正規化）と同じ要領で、**実際のバウンディングボックスを実測してから
目標サイズへ縮尺し直す**方式にした（`MagicHandSceneBuilder.CreateHandVisual`、
`HandVisualTargetSize = 0.55`＝旧・球と同じ大きさ）。読み込みは `PrefabUtility.InstantiatePrefab`
（`Object.Instantiate` ではない。ネストしたprefab参照を保つため、他の外部モデル読み込み箇所と同じ
作法）。当たり判定は装飾なので `GetComponentsInChildren<Collider>` で全部剥がす。

### `ItemPickup` の見た目切り替えを「型」から「型＋HandType」に拡張
元は `handVisual`（球1個・色染め）／`scrollVisual`／`broomVisual` の3枠を中身の**型**だけで
切り替えていた。グー/チョキ/パーが別モデルになったので、`guVisual`/`chokiVisual`/`paVisual`の
3枠に分割し、`(definition as HandItemSO)?.Hand` で選ぶように変更。**色染めの仕組み
（`MaterialPropertyBlock`／`colorProperty`／`ResolveTintTargets`）は丸ごと削除した**
——手変更アイテムの色染めが最後の呼び出し元で、スクロール・ほうきは元々常に空配列
だったため、消しても他に影響が無いことを確認してから消した。

### 相手の頭上にだけ見える表示（`PlayerHandIndicator`）
`ScrollRangeIndicator`（自分だけに見せる範囲円）が使っている「相手のカメラの cullingMask から
除外されたレイヤー」の仕組みを、**逆向き**に使う。

- 各プレイヤーの `BuildPlayer(index, ...)` は既に `rivalLayer`（＝自分のカメラが除外している、
  相手のカメラだけが映すレイヤー）をローカル変数として持っている（カメラの cullingMask 設定に
  使っているのと同じ変数）。**この `rivalLayer` を頭上表示のレイヤーにそのまま使うだけで、
  「自分には見えず相手にだけ見える」が実現できる**——2人目のプレイヤーを待って配線し直す
  ような2段階処理は不要だった
- 頭上のオブジェクト（`HandIndicator`、足元+2.3m）はグー/チョキ/パーの3モデルを
  子として持ち、`PlayerHandIndicator`（新規）が `player.CurrentHand` を毎フレーム見て
  ON/OFFを切り替える。地面のアイテムより一回り小さく（`HandIndicatorTargetSize = 0.35`）
  縮小してある
- レイヤーは `SetLayerRecursively`（新規のビルダー内ヘルパー）で子孫まで再帰的に揃える。
  `RevealMarker.SetLayerRecursively`（インスタンスメソッド版）と同じ発想だが、
  ビルダー側は静的にオブジェクトを組む場面のみで使うので別に用意した
- 検証は「一方のカメラの実際の `cullingMask` を退避したカメラに移し、対象の頭上へ寄せて
  レンダリングする」方式で行った。1P視点で2Pの頭上にパー（本）が浮いているのが見え、
  2P視点で1Pの頭上に拳が浮いているのが見えることを実際にスクリーンショットで確認済み
  （cullingMaskの値も `-513`/`-257` で期待通り単一レイヤーだけ除外されていることを数値でも確認）

### 自分の手表示の枠化
`BuildHandDisplay` を、持ちアイテム枠 `BuildItemBox` と同じ「暗い板＋縁取りされた枠＋
アイコン＋下に名前」の構成に作り直した。アイコンは当初は手描き図形（`IconShape.Fist`等）で
作ったが、同日中の追加依頼で **`Assets/Te` の実物モデルを撮った画像に差し替えている**
（詳細は §7-13）。枠の色は `HandType.ToColor()` をそのまま使うので、赤/緑/青の対応は変えていない。

### この節で新たに分かったこと
- **Play モード中の `Object.Instantiate(prefab, pos, rot)` は評価器がしばしば `resultSet:false`
  を返し、かつ実際にも生成されない**（§3/§7-11既出の「resultSetがfalseでも実は成功している」
  パターンとは逆に、今回は本当に失敗していた）。`UnityEditor.PrefabUtility.InstantiatePrefab(prefab)`
  に切り替えると通った。Playモードでテスト用オブジェクトを即席で置きたいときはこちらを使うこと
- **カメラの `cullingMask` は退避してから一時カメラに移し替えれば、実際のプレイヤー視点を
  そのまま検証カメラで再現できる**。`ThirdPersonCameraRig` 経由で狙った位置へフレーミングしようと
  すると追従ロジック（Teleport直後にLateUpdateが追いつくタイミング等）が読みづらく、
  同じ場所を2回スクリーンショットしても見た目が変わらない、ということが起きた。
  検証用に位置と向きを完全に制御したい場合は独立した一時カメラを使う方が速い
- 試合中の残り時間が0になると `TieBreak` へ進み、場のアイテム（手変更・スクロール）が
  一旦すべて消える。Playモードでテストのために `ChangeState(GameState.InGame)` を直接呼んでも
  残り時間はリセットされないので、検証を焦らずに時間切れの兆候（アイテムが急に0件になる）が
  出たら `ChangeState(GameState.InGame)` を呼び直して湧かせ直す

---

## 7-13. 自分の手アイコンを実物レンダリングに差し替え／地面の手変更アイテムを2倍サイズに（2026-08-22）

依頼: 「HUDのグー/チョキ/パーも `Assets/Te` の3モデルを使った表示に変更」＋
「マップでの手変更アイテムの大きさを今の2倍に」。§7-12 の直後に来た追加依頼。

### 自分の手アイコンを「撮った画像」にする
§7-12 では手描き図形（塗りつぶし円／X字／長方形）でアイコンを作ったが、地面のアイテムと
頭上表示がすでに実物モデルなので、HUDだけ図形のままだと3箇所で見た目が揃わない。
`RenderHandIcon`（新規、`MagicHandSceneBuilder.cs`）で解決した。

- `BroomPosePreview.cs` と同じ「本編から離れた高所（Y=800）にモデルを1体だけ置いて
  専用カメラで撮る」手法を使う。ただし今回は**UIに貼るアイコンなので背景を透過させる必要がある**
- URPのカメラ背景アルファがそのままPNGの透過に使えるか自信が持てなかったため、**クロマキー方式**
  にした: 背景をマゼンタ（`(1,0,1)`）で塗って撮り、`Texture2D.GetPixels32()` で読んだあと
  マゼンタに近い画素だけCPU側で `alpha=0` に置き換えてから `EncodeToPNG()` する。
  モデルの素材にマゼンタが使われていないことが前提だが、3体とも岩・鋼・革の色なので問題なかった
- **最初の1回は真っ暗（ほぼシルエット）で撮れた**。原因は高所（Y=800）に本編のライトが
  屆いていなかったこと。剣（チョキ）と本（パー）は素材が明るいのでまだ見られたが、
  岩（グー）は地の色が暗く、実用に耐えないくらい潰れた。**撮影用の専用
  `Directional Light`（intensity 1.6, shadow無し）をステージに追加**して解決した。
  本編の照明に頼らず、アイコン撮影は常に自前で光源を用意すること
- グー/チョキ/パーで元の縮尺がバラバラな点は §7-12 の `CreateHandVisual` と同じ問題なので、
  同じ「実測してから単位サイズへ正規化」を撮影前にも適用している
- 生成物は `Textures/Icons/Icon_HandGu.png` 等に**キャッシュ**される
  （`CreateIcon` と同じ「無ければ作る」方式）。**モデルや撮り方を直したいときは、
  既存のPNGとmetaを先に消してから `BuildScene()` を呼び直さないと、
  古いキャッシュがそのまま使われて変更が反映されない**。今回、暗すぎた1回目の絵を
  ライト追加で直した際もこれを踏まえて先にPNG/metaを消してから撮り直している
- 手描き図形の `IconShape.Fist`/`Scissors`/`Palm` と対応する `IsInsideShape` の分岐は、
  呼び出し元が無くなったため削除した

### 地面の手変更アイテムを2倍に（このあと更にもう一段2倍になった。§7-14参照）
`HandVisualTargetSize` を `0.55` → `1.1` に変更しただけ。頭上の相手向け表示
（`HandIndicatorTargetSize = 0.35`）は `CreateHandVisual` が返した大きさに対する**比率**
（`HandIndicatorTargetSize / HandVisualTargetSize`）で縮小し直す設計にしてあったため、
地面側だけが2倍になり、頭上表示の大きさは変わらない（意図した副作用の抑制）。
地面に置く高さのオフセット（`localPosition.y = 0.6`）は変更していないが、
2倍サイズで実測しても地面へのめり込みは起きていない（Playモードで実測確認済み）。

### 検証方法
Playモードで実際に湧いたアイテムの座標を取得し、専用の一時カメラ（`Object.Instantiate`ではなく
`PrefabUtility.InstantiatePrefab` で置いた即席カメラ）を寄せてスクリーンショットで確認。
グー（拳）・チョキ（交差剣）とも、旧サイズの2倍相当の大きさで、かつ地面から浮いて見えることを
実測した。HUDのグー枠には拳の実物レンダリングが赤地に映っていることも確認済み。

---

## 7-14. 手アイコンの余白調整とサイズ再倍増、および長時間のMCP切断インシデント（2026-08-22）

依頼: 「見切れている色は縁だけにしてアイコンをもっと見やすく」＋「マップに落ちている
手変更アイテムの大きさを今の2倍に」。§7-13 の直後に来た追加依頼。

### アイコンの縁が見えない問題（カメラを引くだけでは直らなかった）
最初の実装（§7-13）はカメラをモデルのすぐ近くに置いていたため、グーの拳が枠いっぱいに
写り、赤/緑/青の縁がほとんど見えなかった。素直に「カメラを引く」よう直したところ、
**今度はチョキ（交差剣）だけが極端に小さく写るようになった**。

原因: 交差剣は見た目の割にバウンディングボックスが3軸に大きく広がる（剣が斜めに
2本交差しているため）。「対角の長さ」や「正規化した最長辺の半分」のような、形に依存する
近似値でカメラ距離を決めると、剣のような"スカスカ"な形と、拳や本のような"詰まった"形とで
見かけの大きさが揃わない。

対処: **実際に使うカメラの向きへ、バウンディングボックスの8つの角を投影して、
本当に必要な横幅・高さを実測する**方式に変更した（`RenderHandIcon` 内、
`right`/`up` 基底ベクトルを作って各角を `Vector3.Dot` で投影）。これなら形が
偏っていても、その視点から実際に見える範囲を正確に測れるので、3体の見かけの
大きさが安定する。

カメラの向きも合わせて調整した: 斜め45度だと交差剣の刃がほぼ真横から見えて
線1本にしか見えなくなるため、ほぼ正面＋わずかな見下ろし
（`new Vector3(0f, 0.25f, -0.97f)`）に変更している。

`fillFraction`（枠に対してどれだけ大きく写すか）は最初 `0.62` にしたが、
同日中の追加依頼「アイコンをもっと大きく、でも見切れないように」を受けて
**`0.85`** まで上げた。枠側の `Icon` の RectTransform が枠から12%内側に
余白を取ってある（`BuildHandDisplay` の `new Vector2(0.12f, 0.12f)`）ため、
撮影側をここまで大きくしても縁の色は隠れない（実測確認済み）。

### 地面の手変更アイテムをさらに2倍に（0.55→1.1→2.2）
`HandVisualTargetSize` を `1.1` → `2.2` に変更。§7-13 で作った「頭上表示は比率で
縮小し直す」設計のおかげで、今回も頭上表示の大きさには影響していない。

### 長時間（10分以上）Unity側のMCP接続が切れたインシデント
このアイコン調整中、`unity_force_refresh_assets` を短時間に何度も呼んだ後、
HTTPブリッジ（`localhost:8086`）が `unityConnected:false` のまま10分以上戻らない
状態になった。`Editor.log` を直接確認したところ:

- ログ自体が数分間まったく増えていない（新しいコンパイル・リフレッシュの形跡が無い）
- 直前のログには `[Synaptic] WebSocket re-attached to port 8086 after reload.` が
  出ていたが、その後 `/health` は `unityConnected:false` のまま変化しなかった
  （エディタ側は再接続したつもりでも、ブリッジ側の状態が追従していなかった可能性）
- `tasklist` で見ると `Unity.exe` プロセス自体は生きていた（メモリ使用量が変動していたので
  完全なフリーズではなさそうだった）

**結局、特別な操作はせず時間を置いて `unity_force_refresh_assets` を呼び直しただけで
自然に復帰した**（体感で合計10分強）。復帰後は通常どおり動作した。

- 教訓: `unity_force_refresh_assets` を短時間に連続で呼ぶと、この手の長時間切断を
  誘発しやすい可能性がある。**1回のリフレッシュ結果を確認してから次を呼ぶ**、
  短時間に3回以上連続で呼ばないなど、間隔を空ける方が安全そうだ
- 切断が数分続いても、Unity.exe プロセスが生きていて `Editor.log` のタイムスタンプが
  更新され続けている（＝何かしら処理はしている）うちは、慌てて強制終了せず
  待つのが無難だった。ログが完全に静止した状態が続く場合は本当のフリーズの
  可能性があるため、その場合はユーザーに画面の確認を依頼すること

### 検証方法
Playモードで実際にHUDへ3種類の手を順番にセットし、枠の色（赤/緑/青）がはっきり
見える状態でアイコン（拳／交差剣／本）が写っていることをスクリーンショットで確認した。
地面のアイテムは既に§7-13で2倍化の実測手順が確立していたため、今回は
`HandVisualTargetSize` の値変更のみで対応し、個別の再実測は行っていない
（同じ仕組みの延長のため、数値を変えれば同様に機能することが分かっている）。

---

## 7-15. 手枠を「縁だけ色」に、パーの本を横向きに、アイコンをさらに拡大（2026-08-22）

依頼: 「色は縁のみ／パーの本は横向きに／もう少し大きく表示」。§7-14 の直後に来た追加依頼。

### 枠を「縁だけ色」にする
`BuildHandDisplay` の `Frame`（手の色で塗った板）は今まで**全面が色で塗りつぶされていた**。
これを額縁のように見せるため、`Frame` の内側にもう1枚 `FrameInner`（外側の暗いパネルと
同じ色）を重ね、その上にアイコンを置くよう変更した:

```
HandDisplay (暗いパネル背景)
└ Frame（手の色。88%×68%）
  └ FrameInner（暗色。Frame から内側へ7%）  ← 新規
    └ Icon（FrameInner から内側へ4%）        ← 前は Frame の直接の子だった
```

`Frame` の色そのものは変えず「上に暗い板を重ねて中身を隠す」やり方にしたので、
`InGameHUD.UpdateHand()` 側のロジック（`handFrame.color = hand.ToColor()`）は無改造で済んだ。

### パーの本を横向きに
`RenderHandIcon` に `float rollDegrees = 0f` を追加。カメラの向きそのものを
自身の前方軸まわりにロール（`right`/`up` の基底ベクトルを回してから
`LookAt(bounds.center, up)` に渡す）させることで、モデル側のローカル回転を
一切知らなくても画像だけ回せるようにした。**投影の実測（横幅・高さ）もロール後の
right/up で行う**ため、ロールを掛けても縁が見切れる心配はない。

パーだけ `RenderHandIcon("Icon_HandPa", PaVisualPrefabPath, 90f)` としてロールを渡している。

**ここでMono.CSharp評価器の別の癖を踏んだ**: `PrefabUtility.InstantiatePrefab(prefab)` を
**親を指定せずに**呼ぶと、この評価器では実行時に静かに失敗する（`resultSet:false` かつ
オブジェクトも実際に作られない）。`PrefabUtility.InstantiatePrefab(prefab, parent)` と
**親を必ず渡す**と正常に動く。ビルダー本体のコードはもともと常に親を渡しているため
影響は無いが、Playモードや検証用の即席スクリプトを書くときは要注意。

**目視だけで「横向きになったか」を判断しない方がいい**という教訓も得た。撮った本の
表紙模様は回転してもそれらしく見えてしまい、最初は「まだ縦向きに見える」と誤診した。
実際には `Texture2D.GetPixels32()` で不透明画素の外接矩形を測り、幅106px・高さ83pxと
**数値で**横長になっていることを確認して初めて確信できた。3Dの見た目確認は、
可能なら生成物（PNGなど）を直接数値で検証する方が目視より確実。

### アイコンをさらに拡大
`fillFraction` を `0.85` → `0.92` に変更。`Icon` のRectTransform側の余白（4%）と
`FrameInner` の余白（7%）が既にあるため、撮影側をここまで大きくしても縁の色は
隠れない。

### 検証方法
Playモードでグー/パーをHUDに表示させてスクリーンショットを確認: 縁（赤/青）が
帯として残り、内側の暗い部分にアイコンが大きく表示されていることを確認した。
パーは本の表紙が横長に写っていることも確認済み。

---

## 7-16. 3D側（地面のパー・頭上表示）も横向きに、頭上表示を3倍に（2026-08-22）

依頼: 「マップに落ちているパーも相手の頭の上のパーもすべて横向きに変更。頭の上の
表記を今の3倍に変更」。§7-15 はHUDアイコン（2D画像）だけの対応だったので、
3Dで実際に置かれているモデル（地面のアイテム／頭上表示）にも同じ調整を広げた依頼。

### 3Dモデル本体を回す（HUDアイコンとは別の仕組み）
§7-15 の「横向き」はカメラをロールさせるだけ（2Dの見た目だけを回す）だったが、
地面のアイテムや頭上表示は**プレイヤーが自由な角度から見る3Dオブジェクト**なので、
カメラ側を回しても意味が無い。**モデル自体を回す**必要がある。

`CreateHandVisual` に `Vector3 extraEuler = default` を追加し、実測前
（`localRotation` を単位回転にする代わりにこの角度を適用）に回すようにした。
こうすることで、回した後の実際の見た目のサイズで正規化（`HandVisualTargetSize` /
`HandIndicatorTargetSize` への縮尺）が行われるため、回転を加えても大きさの調整は
狂わない。パーだけ `new Vector3(0f, 0f, 90f)` を渡している
（地面のアイテム＝`CreateItemPrefab`、頭上表示＝`BuildHandIndicator` の両方）。

見た目としては、正面向きの開いた本（表紙が観客側、背表紙が縦）だったものが、
90度回転で「表紙が左右に開いた、横長の見開き」になった。実測で確認済み
（地面のアイテムを広めの画角で撮ると、閉じた背表紙を軸に表紙が左右へ開いた
横長のシルエットになっている）。

### 頭上表示を3倍に
`HandIndicatorTargetSize` を `0.35` → `1.05` に変更。地面のアイテムとの縮尺比
（`shrink = HandIndicatorTargetSize / HandVisualTargetSize`）で正規化し直す設計は
そのままなので、この値を変えるだけで意図通り頭上表示だけが大きくなる
（地面のアイテムのサイズには影響しない）。実測すると、キャラの帽子の幅と
同じくらいの大きさまで大きくなった。

### 検証方法
Playモードで1Pをパーにし、2P視点用のcullingMaskを持たせた検証用カメラで
1Pの頭上を撮影。大きな開いた本が帽子の上に浮いており、以前より明らかに
大きく、見開きが横長になっていることを確認した。地面のパーアイテムも
広い画角で確認し、開いた表紙が左右に広がる横長のシルエットになっていることを
確認した。

---

## 8. 操作方法

| | パッド | キーボード |
|---|---|---|
| 移動 | 左スティック | WASD |
| 視点 | 右スティック | 矢印 |
| ジャンプ | ✕ | Space |
| 飛行の上昇 / 下降 | R1 / L1 | Space / Shift |
| スクロール発動 | □ | E |
| オプション | Start | Esc |
| 手を選ぶ | 十字キー → ✕ で決定 | 矢印 → Space |
| ロビーのメニュー | 十字キー | IJKL |

- 入力定義: `Assets/_Game/Input/MagicHandControls.inputactions`
- マップは `Gameplay` / `Selection` / `Lobby` の3つ。State切替時に `SwitchToMap` で切り替える
- コントローラーの接続台数に応じて1P→2Pの優先順でGamepad/Keyboardを動的に割り当てる。§8-1

#### 押しっぱなしの入力は Send Messages で受けてはいけない
`PlayerInput` の Send Messages は、**ボタン型のアクションでは押した瞬間しかメッセージを送らない**。
離した瞬間（canceled）が送られるのは値型のアクションだけ（`PlayerInput.OnActionTriggered` の実装）。

```csharp
if (!(context.performed || (context.canceled && action.type == InputActionType.Value)))
    return;
```

そのため上昇・下降をメッセージで受けると、一度押した値が1のまま残る。
上昇も下降も1に張り付いて差し引き0になり、**上下とも動かなくなる**（実際にこの症状が出た）。
上昇・下降だけは `PlayerController.ReadVerticalInput` が毎フレーム `InputAction.IsPressed()` を読む。
入力マップは準備ルームと試合で切り替わるので、`currentActionMap` が変わったときだけ引き直している。

---

## 8-1. コントローラー優先度の動的割り当て（`ControllerPriorityAssigner`）

コントローラーの接続台数に応じて、**1P→2Pの優先順**でGamepad/Keyboardを動的に割り当てる。
以前は明示的な割り当てロジックが無く、`PlayerInput`（`m_NeverAutoSwitchControlSchemes=true`）が
最初に入力した機器へロックされるだけのUnity既定の早い者勝ち挙動に頼っていたため、
「1Pが必ずコントローラーになる」保証が無かった。

- 実装は `Assets/_Game/Scripts/Core/ControllerPriorityAssigner.cs`。`PlayerInputManager`は使わず、
  `InputSystem.onDeviceChange` を購読するだけのプレーンC#クラス（MonoBehaviourではない）。
  `GameManager.Awake()` で生成、`OnDestroy()` で破棄する。シーン（`MagicHandSceneBuilder.cs`）側の
  変更は不要——`GameManager` が既に持っている `players` から `PlayerInput` を都度 `GetComponent` するだけ
- 判定は `Gamepad.all` の数のみで決める。0台なら両者Keyboard、1台なら1P=その1台・2P=Keyboard、
  2台以上なら1P=1台目・2P=2台目（3台目以降は無視）
- **試合中の抜き差しにも追従する**（毎フレームのポーリングはせず `onDeviceChange` イベント駆動）。
  2台接続中に1P側のパッドだけ抜くと、2P用だったパッドが1Pへ引き継がれ、2Pはキーボードへ落ちる
  ——優先度は常に1P側が上、という仕様通りの「横取り」が起きる。これは意図した挙動
- キーボード同士のキー分け（1P/2P別キー）は対象外。既存の共通Keyboardスキーム（WASD+矢印、§8）のまま
- 実測: 本体（`C:\gameB9\jyankenonigokko`）のPlayモードで、実機のXboxコントローラー2台と
  `InputSystem.AddDevice<Gamepad>()` の仮想パッドの両方を使って確認。
  0台→両者Keyboard、1台→1P=Gamepad・2P=Keyboard、2台→1P=1台目・2P=2台目、
  2台中1P側を`InputSystem.RemoveDevice`で抜くと1Pが残った1台を引き継ぎ2PがKeyboardへ落ちる、
  の一連の遷移すべてを`currentControlScheme`/`devices`の実値で確認済み

## 8-2. 試合中のオプションから操作説明を開く

準備ルームにしか操作説明が無く、試合中に忘れても確認できなかったため、`InGameOptionsMenu`
（Start/Escで開く画面隅の感度調整パネル）に3行目「操作説明」を追加した。

- `InGameOptionsMenu`のカーソルを`Sensitivity / Invert / Controls`の3行に拡張。上下で移動、
  「操作説明」の行で左右を押すとパネルが開閉する（`LobbySettingsPanel`の「操作説明」行と同じ操作感）
- パネルの中身は準備ルームと共通の`BuildControlsHelp`をそのまま呼び出して流用している。
  表を二重に持たないので、操作方法を変えるときはどちらか一箇所を直せば両方に反映される
- オプション自体を閉じる（Startで閉じる／試合状態が変わる）と、開いていた操作説明も強制的に閉じる。
  次に開いたときに前回の状態を引きずらないため
- 実測: Playモードで`InGameOptionsMenu`を開き、カーソルを「操作説明」まで動かして開くと
  準備ルームと同じ表（移動・視点・ジャンプ…の一覧）が画面中央に表示されることをスクリーンショットで確認。
  もう一方のプレイヤー側は影響を受けず独立して動作することも確認済み

---

## 9. いま途中の作業

### 完了済み（すべて再生モードで実測確認済み）
古い順。数値の根拠は各節を参照。

- **服の色を手に対応**: `Texture_A_{Gu,Choki,Pa}.png` と `M_Character_*.mat` を生成し、
  `PlayerVisual` が `VisibleHand` を見てマテリアルごと差し替える。肌は元のまま
- **杖の見た目とモーション**: `Staff_01`（全長2.30m）＋長柄クリップ
- **ほうき（飛行アイテム）**: 5秒飛行→滑空→着地ペナルティ。§6のほうき節
- **HUDの刷新**: 手の色帯・マリオカート風アイテム枠・効果の残り時間。§7-2
- **ブリンクの着地点表示 / 画面外の敵矢印**: §7-2
- **湧き位置の増量**: 手変更44・スクロール30
- **手を選ぶ制限時間5秒**（切れるとランダム）
- **あいこ接触**: 互いに弾き、点で負けている側だけ加速
- **負けたときの表示**: 画面中央に「負けてしまった」（`DefeatText`、72pt赤・縁取り）。
  負けた瞬間は視点ごと吹き飛ばされて当たったことに気づきにくく、
  スタンが明けると手の選択へ移ってしまうため
- **タイトルを1枚絵に差し替え**: `_Game/Textures/UI/Title_Background.jpg`（元は `C:\gameB9\start.jpg`）。
  題字もSTARTの台座も絵の中にあるので、コードで文字や板を重ねない。
  押せる領域だけを台座に重ねてある（横 0.320〜0.676、下から 0.029〜0.261 を実測）
- **準備ルームの視点と操作説明**: §7-3
- **サドンデス**: 時間切れで同点なら分岐画面。十字キーで選び ✕/A で決定。§4
- **表記を漢字に統一**: UIのひらがな表記を漢字へ（「まけてしまった」→「負けてしまった」など）。
  コメントとTooltipは対象外
- **開始の手選択を時間無制限に / リスポーン後3秒無敵**: §6
- **飛行の水平速度を地上の1.25倍(8.75)に**: §6
- **スロウの削除**: `SlowFieldEffectSO`・`Scroll_SlowField`・`Icon_Slow`・`PlayerStatusEffects.Slow` を撤去。
  スクロールは7種＋ほうき、設定一覧は11種になった
- **ほうきを独立枠に**: マップに1本まで、取得後10秒のクールタイム、搭乗中は取得不可。§6
- **プレイヤー周辺にアイテムを湧かせない**: 半径7m。§6
- **着地露出にも画面外矢印**: §7-2
- **制限時間を1:00〜10:00に**: §7-3
- **リザルト遷移のちらつき修正**: §7-4
- **初期制限時間を5分に**: 準備ルームで1分〜10分・30秒刻みに調整できる点は変わらず、初期値のみ変更
- **試合開始の手選択を廃止**: UIを出さず両者ランダムで即決定。既存のカウントダウンだけが見える。§6
- **【バグ修正】試合中に視点が動かない**: 上記「手選択を廃止」の副作用。
  以前は `BeginSelection`→`ConfirmHand` の中で入力マップを Selection→Gameplay へ戻していたが、
  ランダム決定にしてそれらを呼ばなくなった結果、準備ルームの `Lobby` マップのまま試合に入り、
  Look（視点）を含む Gameplay 側のアクションが一切反応しなくなっていた。
  `GameManager` の Selection ステート開始時に `player.SwitchToMap(PlayerController.GameplayMap)` を
  明示的に呼んで直した。実測: 修正前は Selection/InGame でもマップが `Lobby` のまま、
  修正後は Selection の時点で `Gameplay` に切り替わり、lookInput注入でカメラが回転することを確認済み
- **アイテム湧き数の再設計**: 手変更25箇所/10体制、アイテム(スクロール+ほうき)50箇所/25体制。
  ほうきは独立グループをやめ、アイテムグループの保証枠（25個中常に1個）に統合。§6
- **ミラージュの削除**: `HandDisguiseEffectSO`・`Scroll_Disguise`・`Icon_Disguise`、
  および `PlayerController` の偽装まわり（`VisibleHand`/`IsDisguised`/`ApplyHandDisguise` 等）を撤去。
  `PlayerVisual` は常に `CurrentHand` を見る。スクロールは6種＋ほうき、設定一覧は10種になった
- **手変更を設定一覧から除外**: `ItemSpawnGroup.includeInSettings`（HandItemsだけfalse）。
  グー/チョキ/パーは勝敗を決める基本アイテムでON/OFFする意味が無いため、
  準備ルームの設定パネルには出さない（湧き自体は変わらず25箇所/10体制のまま）。§7-3
- **スキャンの削除**: `RevealEffectSO` は元々サーチ（相手）とスキャン（落ちている手）を
  `RevealTarget` 列挙で切り替える共用クラスだったが、スキャンを削除した結果サーチしか残らないため、
  列挙ごと剥がしてサーチ専用に単純化した。`Scroll_RevealItems`・`Icon_Scan`・
  `PlayerStatusEffects.RevealItems`、専用の索敵マーカー(`RevealMarker_Item`/`M_MarkerItem`、
  ビルダー内では`itemMarker`)も撤去。相手を索敵する`RevealMarker_Enemy`は
  ほうき着地の位置露出とも共有しているため触っていない。スクロールは5種＋ほうき、設定一覧は6種になった
- **BGMの実装**: タイトル/試合中/リザルトの3画面で曲を出し分け。音量は現在0.01（4分の1を2回。
  元は0.25で依頼された）。§7-5
- **SEの実装**: スタートボタン・カウントダウン・倒れた・あいこ・アイテム取得の5種。
  音量は現在0.15（初期値1→0.5→0.25→0.15、2026-08-22に0.25から変更）。§7-5
- **アイテム発動エフェクト（`CastEffect`）**: 発動した瞬間に足元の輪が広がって消える演出。
  色はアイテムの `DisplayColor` を流用。実装中に `ScrollStock` の2重生成と
  `NewScene()` をまたぐプレハブ参照の消失という2つの罠を踏んで特定・修正した。§7-6
- **効果発動中エフェクト（`StatusAura`）**: 時間制限つきの効果がかかっている間ずっと
  足元の輪が回り続ける演出。`PlayerStatusEffects.Apply` に色を持たせ、8箇所の呼び出し元
  すべてに意味に合う色を渡した。相手にも見えるようレイヤーは絞っていない。§7-7
- **スピードUp専用エフェクト（`SpeedUpEffect`）**: 「疾走感のある演出に」という依頼で、
  スピードUpだけ `StatusAura` の輪から、自身の周りを駆け上がる5本の上矢印に差し替えた。§7-8
- **スタン専用エフェクト（`StunEffect`）**: 「雷に感電したような演出に」という依頼で、
  スタンだけ体の周りに不規則に明滅するジグザグの稲妻（最大5本）に差し替えた。§7-9
- **手変更アイテムの表示名から「の巻物」を削除**: 「グーの巻物」→「グー」のように短縮。
  `CreateHandItems`（ビルダー）のみの変更で、内部の識別子・アセットファイル名は変更していない
- **サーチ専用エフェクト（`SearchWaveEffect`）**: 「3D同心円状にスキャンし、進むほど薄くなる
  波動を」という依頼で、`CastEffect`（共通発動演出）に加えてサーチだけこちらを追加した。
  直交する3枚の輪を同時に広げて全方位に見せ、太さを0へ細らせて距離減衰を表現。§7-10
- **バランス調整一式**: チャームの色をサーチと被らない緑（0.4,0.9,0.4）に変更、
  サーチ波紋の最大半径を9m→27m（3倍）、スタンの半径を8m→5m、チャームの半径を10m→8m、
  スピードUpを1.6倍4秒→1.5倍5秒に変更。ほうき乗車中の速度（地上の1.25倍＝8.75）は
  依頼時点で既に設定済みだったため変更なし
- **サーチされている側の足元の輪を非表示に**: `StatusAura` の除外リストに `Searched` を追加。
  「サーチされている」はHUDだけの私的な通知のつもりだったが、世界空間の輪を出すと
  索敵している側からもその輪が見えてしまい、壁越しでない索敵情報が漏れる形になっていたため。
  実測でサーチ使用者側は輪が出たまま、されている側だけ輪が出なくなることを確認済み
- **スクロール発動音5種（スタン・ブリンク・スピードUP・チャーム・ほうき）**: 既存のSE素材から
  個別に割り当て。範囲系2種は `AreaScrollEffectSO` に新設した `PlayActivationSound` フックの
  override、非範囲系3種は各 `Apply()` に直書き。§7-11
- **あいこノックバックを負け接触と同じ強さに統一**: `GameManager.drawBounceForce`/
  `drawBounceUpForce` を 7/3.5 → 12/5（`PlayerController.knockbackForce`/`knockbackUpForce` と同値）
  に変更。無敵判定・スタン・リスポーンは伴わせず、ノックバックの数値だけ揃えた。§7-11
- **手変更アイテムの専用モデル化＋相手の手を頭上に表示＋自分の手表示の枠化**: 地面の
  手変更アイテムが色つき球から `Assets/Te` の専用モデル（グー＝拳の形をした岩・チョキ＝
  交差する2振りの剣・パー＝開いた本）に変わった。相手の頭上にだけ見える形（色ではなく）で
  同じ3モデルを表示し、自分の手表示もアイテム枠と同じ「枠＋アイコン＋文字」に変えた。§7-12
- **自分の手アイコンを実物レンダリングに変更＋地面の手変更アイテムを2倍サイズに**:
  HUDの手アイコンが手描き図形から `Assets/Te` の実物モデルを撮った画像になり、
  地面の手変更アイテムの見た目サイズが2倍（`HandVisualTargetSize` 0.55→1.1）になった。
  頭上の相手向け表示は比率で縮小し直す設計だったため、この変更で大きさは変わっていない。§7-13
- **手アイコンの縁が見えるよう余白調整＋地面の手変更アイテムをさらに2倍（1.1→2.2）**:
  カメラ距離の決め方を「実際の視点方向へバウンディングボックスの角を投影して実測する」
  方式に変更し、交差剣（チョキ）だけ極端に小さく写る問題を解消した。§7-14
- **手枠を「縁だけ色」に、パーの本を横向きに、アイコンをさらに拡大**: `Frame` の内側に
  暗色の `FrameInner` を重ねて額縁のような見た目にした。`RenderHandIcon` にロール角の
  引数を追加し、パーの本だけ90度回して横長に見せている。§7-15
- **地面のパー・頭上表示のパーも横向きに、頭上表示を3倍に**: `CreateHandVisual` に
  回転角の引数を追加し、パーの3Dモデル本体を90度回した（§7-15はHUDアイコンだけの
  対応だったための追加依頼）。`HandIndicatorTargetSize` を 0.35→1.05 に変更。§7-16
- **コントローラー優先度の動的割り当て**: `ControllerPriorityAssigner` を新設し、コントローラーの
  接続台数（0/1/2台以上）に応じて1P→2Pの優先順でGamepad/Keyboardを割り当てるように変更。
  試合中の抜き差しにも追従する。§8-1
- **Resultの手前にFinish画面を追加**: `GameState.Finish` を新設し、通常の時間切れ・サドンデスの
  決着・TieBreakの「結果発表」、Resultへ行く3経路すべてが必ずFinishを経由するように変更。
  `FinishUI` が「FINISH」を`finishDuration`（2秒）だけ出してからResultへ自動で進む。§4-1
- **試合開始のカウントダウンに「START」を追加**: `CountdownThenStart` で3-2-1の後
  `ShowStartText` を`startTextDuration`（1秒）だけ立て、`InGameHUD` の同じ`countdownText`に
  数字の代わりに「START」を出してから`InGame`へ入るように変更。§4-2
- **ほうきの水平速度を地上の1.75倍(12.25)に**: 1.25倍(8.75)から変更。滑空速度もその50%として
  自動で追従する（6.125）。§6
- **試合中のオプションに操作説明を追加**: `InGameOptionsMenu` に3行目「操作説明」を追加し、
  準備ルームと同じ操作表（`BuildControlsHelp`を共用）をトグルできるように変更。§8-2
- **【ビルド修正】Playerビルドが失敗する問題を解決**: `Assets/FastMesh/Scripts/SceneViewText.cs`
  （アセットストアの宣伝用オーバーレイ、`Editor`フォルダに置かれておらず`UnityEditor.SceneView`を
  参照）が原因で`CS0246`のコンパイルエラーが発生しビルドが止まっていた。ゲーム本体とは無関係の
  ファイル。内容は変えず`#if UNITY_EDITOR`で全体を囲み、Player側のコンパイル対象から外して解決。
  実測: 修正前は`BuildPipeline.BuildPlayer`が`Failed`（`error CS0246`）、修正後は`MagicHand.exe`
  一式（約215MB、3シーンぶんのlevel0/1/2を含む）が生成されることを確認済み
- **アイテムが使えないときにバツ印**: `PlayerController.CanUseScroll`を新設（スタン中は
  ブリンクだけ例外で使える）。`InGameHUD`のアイテム枠にバツ印を重ねて表示。§11-1
- **視野角の設定を追加**: `MatchSettings`にper-playerのFOV（初期90・範囲90〜110・5刻み）を追加し、
  `ThirdPersonCameraRig.SetFieldOfView`で反映。オプション画面・準備ルーム設定の両方に行を追加。§11-2
- **巻物の中身を拾った瞬間の抽選に変更**: `RandomScrollSO`を新設。以前は湧いた瞬間に5種類の
  どれかへ確定していたが、地面の巻物はすべて中身未定にし、拾った瞬間に候補から抽選するように変更。
  準備ルームで無効化した種類は候補から除外（現状維持）。§11-3
- **デバッグモードを追加**: オプションボタンを5秒長押しでON/OFF。ONの間だけ`InGameOptionsMenu`に
  「クリエイティブ飛行」と巻物5種の「付与」行が増える。`ScrollStock.ForceStock`で1個ストック制限を
  無視して即上書き、`PlayerFlight.CreativeFlight`で無制限ホバー飛行（壁・地形の当たり判定は通常通り）。§11-4
- **【バグ修正】ControllerPriorityAssignerが1P/2Pどちらかの割り当てに失敗する問題を解決**:
  根本原因は`PlayerInput`の`m_DefaultControlScheme`が空だったこと。`"Keyboard"`を既定にして
  Unity自身の自動ペアリングを先に走らせることで、`InputUser`の未初期化に起因する
  "Invalid user"例外を解消。§11-5
- **手変更アイテムに縁取りを追加**: グー/チョキ/パーの地面アイテムに反転殻方式の白い縁取りを
  付け、無地の巻物と一目で区別できるように変更。§11-6
- **アイテムの湧き数調整＋近接回避**: 手変更を10→20体制、アイテムを25→20体制に変更
  （地点数はそれぞれ25・50のまま）。あわせて`itemClearRadius`（8m）を追加し、
  グループを問わず他の湧いているアイテムからできるだけ離れた地点を優先するように変更。§6
- **視野角の範囲を60〜80に変更**: 初期90/範囲90〜110から変更。初期値も新しいminに合わせて60に。§6
- **同点の時間切れもFinishを経由するように変更**: 以前は同点だとTieBreakへ直接飛んでいたが、
  他の経路と同じくFinishを必ず挟んでからTieBreakへ進むように変更。§11-7
- **SEの音量を0.15に変更**: 0.25→0.15（BGMは0.01のまま変更なし）
- **デバッグパネルをオプションと分離**: オプションを開いている間はデバッグパネルを隠し、
  閉じている間だけ表示するように変更。アイテム付与の決定操作をLだけに絞った。§11-4を更新
- **ほうきの湧き位置に薄いビーコンを追加**: 半透明の光の柱を立て、遠くからでも
  ほうきの位置が分かるように変更。§11-8
- **アイテム説明パネルの文字はみ出しを修正**: 準備ルームのアイテム説明で、長い説明文
  （特にほうき）が枠からはみ出ていた。折り返し（`HorizontalWrapMode.Wrap`）を有効にし、
  パネルも一回り広げて対応。§15
- **得点表示（画面上部の「3 - 2」）を拡大**: フォント34→56に。ハイフンは元から
  数式上は画面中央だったが、数字との余白を広げて見た目にも中心と分かりやすくした。§16
- **アイテム説明の枠縮小・得点/残り時間の再拡大・残り時間の通知・優位劣位マーク・
  手変更アイテムの均等湧き**: 5件まとめて対応。優位/劣位マークは新規
  `HandAdvantageIndicator`（相手の頭上表示と体の間、緑▲/赤▼/白ひし形）。
  LineRendererのbillboard系alignmentが2種類とも特定角度で壊れる不具合を踏み、
  最終的に単純な三角形メッシュ＋スクリプトビルボードに作り直した。§17

### 杖の持たせ方（作り直すときはここを読む）
- 杖は **`Arm_R` の子にしてはいけない**。このボーンには 0.28 倍のスケールが入っており、
  子にすると杖が1/3に縮む（実際これで別物のような小さい塊に見えていた）
- ビルダーは杖を**キャラのルート直下**に置くだけ。位置合わせは実行時に
  `PlayerStaffVisual.LateUpdate` が毎フレーム行う
- 位置決めの方針: **左右前後は `Arm_R` に追従、角度と高さは足元基準で固定**。
  - 高さを腕から取ると破綻する。`Arm_R` の原点は手の位置ではない（身長1.92mに対しY=0.84）うえ、
    アニメーションで上下するので、焼き込んだ位置だと石突きが地面を0.36m突き抜けた
  - 杖(2.30m)は身長(1.92m)より長いので、**石突きを足元+0.10mに合わせる**と
    自然に手の高さを通り、宝石側が頭より上に出る
- `pivotToBottom`（杖の原点→石突き）はビルダーがメッシュのローカルboundsから実測して渡す

### 依頼されているが未着手
（現在なし）

### 改善案として挙げたが保留中
- 設定の保存（PlayerPrefs）。今は Play を抜けると初期値に戻る
- 準備ルームで手選択の練習ができない
- 1P/2P の見分けがつきにくい（両方とも魔法使い）
- 被弾モーションがスタン2秒より短く、後半が棒立ちになる

---

## 10. 検証のやり方

Playモードで状態遷移を直接呼んで確認するのが速い。

```csharp
// ロビーまで進める
MagicHand.GameManager.Instance.StartMatch();

// スタート地点に2人を乗せる → カウントダウン → Selection
var z = UnityEngine.Object.FindFirstObjectByType<MagicHand.LobbyStartZone>();
a.Teleport(new Vector3(z.transform.position.x - 1.2f, -100f, z.transform.position.z), Quaternion.identity);

// 手を確定して InGame へ
a.ConfirmHand(MagicHand.HandType.Gu);
b.ConfirmHand(MagicHand.HandType.Choki);
```

接触判定は 2人を `Teleport` で隣接させて手を設定すれば即座に検証できる。
**`transform.position` への代入では動かないことがある**ので `Teleport` を使うこと。

### 今回よく使った手法
- **見た目は必ず画像に描き出して目で見る。** 数値では姿勢も構図も判断できない。
  - 実行中の全画面: `ScreenCapture.CaptureScreenshot("C:/gameB9/x.png")`（1フレーム後に読む）
  - 特定カメラだけ: `cam.rect` を全画面にして `RenderTexture` へ `Render()` → `ReadPixels`
  - 編集モードの搭乗ポーズ: `BroomPosePreview.Capture(パス, カメラ位置)`
  - **出力パスは必ずASCII。** 日本語を含むパスはMCPのペイロードで壊れる
- **画面に空(背景)が写っていないかは11×11で走査する。** `ViewportPointToRay` → `RaycastAll` で
  当たった物を数える。描画を切った壁は名前で除外する（当たり判定は残っているため）
- **押しっぱなしの入力は注入できない。** 物理コントローラーが毎フレーム自分の状態を送って
  上書きするため。`InputSystem.AddDevice<Gamepad>()` で仮想パッドを足すと注入が通る（検証後に削除）
- **private フィールドはリフレクションで読む。** `speedMultiplier` や `cursor` など。
  ただし評価器は複数文＋early return に弱いので、1文ずつ短く書く

### 検証で状態が汚れる点に注意
テスト中に制限時間やスコアを動かすと、その値のまま残る。
`MatchSettings` は実行を止めれば保存値へ戻るが、確認は最後にやり直すこと。

---

## 11. 2026-08-22 追加分（バツ印・視野角・巻物抽選・デバッグモード）

### 11-1. アイテムが使えないときのバツ印

- `PlayerController.CanUseScroll`（`CanAct`とは別の新しいプロパティ）:
  `IsSelecting`/`IsDefeated`/ゲーム状態はCanActと同じ条件。**スタン中だけ例外**で、
  持っている巻物が`TeleportEffectSO`（ブリンク）のときだけtrueを返す
- `OnUseScroll`は`CanAct`ではなく`CanUseScroll`でゲートするよう変更
- `InGameHUD`のアイテム枠に`itemUnusableMark`（赤い「✕」、`AddTextOutline`で縁取り）を追加。
  `stock != null && !player.CanUseScroll` のときだけ表示する
- 実測: スタン中に非ブリンクを持たせるとバツ印が表示され、ブリンクに差し替えると
  スタン中でも消える（`CanUseScroll`もtrueに変わる）ことをスクリーンショットで確認済み

### 11-2. 視野角（FOV）設定

- `MatchSettings`にper-playerの`fieldOfView[]`を追加。`fovStep`5、
  `fovMin`60・`fovMax`80、初期値も60（2026-08-22: 当初90/90〜110で追加、
  同日中に60/60〜80へ変更。初期値は常にminに合わせている）
- `ThirdPersonCameraRig.SetFieldOfView(float)`が`Camera.fieldOfView`に反映する。
  `PlayerController.Update()`で感度・上下反転と同じタイミングで毎フレーム反映
- `InGameOptionsMenu`と`LobbySettingsPanel`の両方に「視野角」の行を追加（依頼の
  「準備ステージのところにも追加して」に対応）
- 実測: `MatchSettings.AdjustFieldOfView`を60→80まで6回呼び、80でクランプされること、
  実際のカメラの`fieldOfView`が80になることを確認済み

### 11-3. 巻物の中身は拾った瞬間に抽選

以前は`ItemSpawnManager`が**湧いた瞬間**に5種類（スピードUp/スタン/ブリンク/チャーム/サーチ）の
どれかへ確定させ、`ItemPickup`にbakeしていた。地面の見た目は元々色分けしておらず区別できなかったが、
中身自体はプレイヤーが気づく前から決まっていた。依頼により、**拾った瞬間に抽選**する仕様へ変更した。

- `RandomScrollSO`（新規）: `ItemDefinitionSO`を継承し、5種類の`ScrollEffectSO`を
  候補（`candidates`）として持つ。`TryPickup`で準備ルームにより無効化されていない候補から
  ランダムに1つ選び、`player.Scrolls.TryStock(chosen)`する。**ハズレは無い**
  （候補が1つも有効でなければ取得自体を成立させない＝場に残る、というのが唯一の失敗パターン）
- `MagicHandSceneBuilder.CreateRandomScroll`が5種類をラップした`RandomScrollSO`を1個作り、
  「アイテム」グループの抽選テーブルにはこれだけを入れる（ほうきは従来通り別の保証枠のまま）
- 落とし穴: `RandomScrollSO`を抽選テーブルに1個だけ入れると、準備ルームの設定パネルが
  「巻物」1種類しか表示・トグルできなくなってしまう。`ItemSpawnManager.RegisterLootWithSettings`で
  `RandomScrollSO`を見つけたら`Candidates`（5種類）を展開して個別に`MatchSettings`へ登録することで、
  設定パネルの見た目・ON/OFFは今まで通り5種類ぶん出るようにしてある
- 実測: `ItemSpawnManager.BeginSpawning()`後、地面のアイテムが`RandomScrollSO`型になっていることを
  確認。`TryPickup`を直接呼ぶと毎回ランダムな効果（例:チャーム）がストックされることを確認。
  スタン以外の4種類を`MatchSettings.ToggleItem`で無効化すると、5回連続で必ずスタンが選ばれる
  （＝準備ルームの設定が抽選に反映される）ことを確認済み

### 11-4. デバッグモード

オプションボタンを5秒間押し続けると、そのプレイヤーだけデバッグモードに入る。

- `InGameOptionsMenu`（感度・上下反転・視野角・操作説明の4行、固定）とは**別のパネル**
  （`debugPanel`）にクリエイティブ飛行＋巻物5種の付与、計6行を持たせている
  （2026-08-22: 当初はオプションパネルに動的に行を足す設計だったが、「オプションを閉じたあとに
  デバッグ画面を表示」という依頼で分離した。同じ十字キーを2枚のパネルが同時に取り合わないよう、
  `showDebug = debugMode && !isOpen && inGame`——**オプションが閉じている間だけ**デバッグパネルを
  出す。開くと自動で隠れ、閉じるとまた出る）
- 長押し検知は`TrackDebugHold()`が毎フレーム`InputAction.IsPressed()`を直接読む方式
  （`PlayerController.ReadVerticalInput`と同じ理由。Send MessagesはボタンアクションだとON/OFFの
  瞬間しか届かないため「押し続けている」を検知できない）。既定5秒（`debugHoldDuration`）
- 「クリエイティブ飛行」: `PlayerFlight.CreativeFlight`/`SetCreativeFlight(bool)`を新設。
  ONにした瞬間`Phase=Flying`・`phaseTimer=Infinity`にして時間切れを無効化。スタン等で
  `Cancel()`されても`CanAct`に戻り次第自動で再度浮く（Updateで毎フレーム見ている）。
  OFFにすると`Cancel()`のみ呼び、着地ペナルティ・位置露出は一切付けない。壁・地形の
  当たり判定はいつも通り（PlayerController側の物理はそのまま）。十字キーどちら向きでもON/OFFする
- 「付与」: `ScrollStock.ForceStock(ScrollEffectSO)`を新設。既存の`TryStock`と違い
  1個ストック制限を無視し、埋まっていても即座に上書きする（デバッグ専用）。
  **決定操作はL（右）だけ**に絞ってある（2026-08-22追加。J（左）では何も起きない。
  押し間違いで意図せず切り替わらないようにするため）。行の表示にも「（L）」と明記している
- デバッグモードを抜けるとクリエイティブ飛行は自動でOFFになる（`SetDebugMode(false)`内で
  `player.Flight.SetCreativeFlight(false)`を呼ぶ）。`PlayerFlight.ResetState()`
  （試合開始時の初期化）でも`CreativeFlight`をfalseに戻す
- 実測: `debugMode`をtrueにすると、オプションを開いていない間だけ`debugPanel`が表示され、
  6行（クリエイティブ飛行＋付与5種、各行に「（L）」表示）が正しく出ることをスクリーンショットで確認。
  オプションを開くとデバッグパネルが隠れ、閉じると再び出ることも確認。
  付与行でJ（左）を押しても何も起きず、L（右）で確実に上書き付与されることを確認済み。
  **5秒長押しの実際の入力検知は実機コントローラー/キーボードでの確認を推奨**
  （自動テストでは`debugMode`フィールドを直接書き換えて下流の効果だけを検証した）

### 11-5. 【解決済み】ControllerPriorityAssignerが1P/2Pどちらかの割り当てに失敗する問題

Playモードに入るたびに、1P・2Pのどちらか片方（実行のたびに変わる）で
`InvalidOperationException: Invalid user`が`GameManager.Start()`から再現していた。
今回の5件の依頼とは無関係の、既存の`ControllerPriorityAssigner`（§8-1）の問題。

```
InvalidOperationException: Invalid user
  at UnityEngine.InputSystem.Users.InputUser.get_index()
  ...
  at UnityEngine.InputSystem.PlayerInput.SwitchCurrentControlScheme(...)
  at MagicHand.ControllerPriorityAssigner.Assign(...)
```

#### 根本原因

`ConfigurePlayerInput`で`m_DefaultControlScheme`を空のままにしていたため、
`PlayerInput`が一度もUnity自身の通常の自動ペアリング（`OnEnable`内）を経由せず、
`InputUser`が未初期化（`user.valid=false`、`user.id=0`）のまま残っていた。
この状態の`PlayerInput`に対して外部から`SwitchCurrentControlScheme`を呼んで
初めてペアリングさせようとすると、1P・2Pのうちどちらか一方（**実行のたびにランダム**）が
"Invalid user"で失敗する。`PlayerInputManager`を使わず2つの`PlayerInput`を手動で
同時に初期化しようとしたことが原因で、Unity Input System側の対応していない使い方だったと考えられる。

#### 特定までに除外した仮説（すべて実測で否定済み）
- Awake/OnEnableの実行順 → `Awake()`→`Start()`へ移動しても再現
- 同一フレーム内での連続呼び出し（競合） → 1フレーム空けても再現
- MCP接続切断によるエディタセッションの汚れ → **Unity Editorを完全に再起動しても再現**
  （MCPブリッジの再接続だけでは直らないが、エディタ本体の再起動でも直らなかった）
- 2つの`PlayerInput`が同じ`InputActionAsset`を共有し、実行時に自動複製されること →
  2P用に別アセット（`MagicHandControls_P2.inputactions`）を持たせても再現
- 失敗した`PlayerInput`側の再試行・コンポーネントの無効化→有効化 → どちらも効果なし
  （一度失敗すると、そのPlayセッション中はそのPlayerInputへの`SwitchCurrentControlScheme`が
  ずっと失敗し続ける＝タイミングの問題ではなく初期化そのものが行われていない）

#### 実際の修正
1. `MagicHandSceneBuilder.ConfigurePlayerInput`で`m_DefaultControlScheme`を`"Keyboard"`に設定
   （空のままにしない）。これによりUnity自身の通常の自動ペアリングが`OnEnable`で先に走り、
   `InputUser`が確実に初期化される。`ControllerPriorityAssigner`は起動直後にこれを
   上書きするので、実際にキーボード固定になるわけではない
2. `ControllerPriorityAssigner`の生成を`Awake()`→`Start()`へ移動（保険。単独では直らなかったが、
   全員のAwake/OnEnable完了を待つのは一般的に安全な作法なので残した）
3. `Assign()`に`try-catch`を追加（保険。上記1で根本原因は直ったが、万一の失敗時に
   もう片方の割り当てを巻き込んで止めないための安全策として残した）
4. `Reassign()`を1フレーム間隔を空けたコルーチン（`ReassignRoutine`）に変更
   （保険。根本原因ではなかったが、`GameManager`をコルーチンのホストとして渡す構成に変更）

実測: 修正後にPlayモードへ3回連続で入り直し、毎回`p0.user.valid`・`p1.user.valid`が
両方`true`になることを確認。0台→1台→2台のコントローラー抜き差しシナリオも
再実施し、1P=1台目・2P=2台目の割り当てが正しく動くことを確認済み

### 11-6. 手変更アイテムの縁取り

グー/チョキ/パーを、地面の巻物（無地）と一目で見分けられるように白い縁取りを付けた。

- 手法は**反転殻方式**（`MagicHandSceneBuilder.AddOutlineShell`）。本体モデルをひとまわり
  大きく（既定1.06倍）複製し、複製側の全レンダラーのマテリアルを専用の縁取りマテリアルに
  差し替える。縁取りマテリアルは`_Cull`を`Front`にした`Universal Render Pipeline/Unlit`で、
  表面（カメラ側）を消して背面だけ描く。深度テストにより本体（等倍・通常描画）が
  中央部分を隠すので、本体の輪郭からはみ出た部分だけが縁として残る。
  新しいシェーダーアセットは不要（既存の`CreateUnlitMaterial`と同じUnlitシェーダーの
  `_Cull`プロパティを変えるだけ）
- `CreateHandVisual`に`addOutline`引数を追加（既定false）。地面のアイテム
  （`CreateItemPrefab`内の3呼び出し）だけ`true`を渡す。頭上の相手向け表示・HUDアイコン撮影用の
  呼び出しは`false`のまま——頭上表示は既に小さく縁取りが煩雑になり、HUDアイコンは
  実物を撮影して2D化する用途なので縁取りが余計な影として写り込むのを避けるため
- 実測: Playモードでグーの地面アイテムにスクリーンショットで縁取りを確認。
  白い縁がモデルの輪郭に沿ってはっきり見えることを確認済み

### 11-7. 同点の時間切れもFinishを経由する

以前は時間切れの瞬間、同点なら`TieBreak`へ直接飛び、同点でなければ`Finish`を挟んでいた
（§4-1参照）。依頼により「同点のときも必ずFinishを見せてからTieBreakへ」に変更した。

- `GameManager.finishNextState`（`GameState`）を新設。`Finish`を抜けたあとどこへ進むかを、
  `ChangeState(GameState.Finish)`を呼ぶ**直前**に設定しておく方式にした
  - 通常の時間切れ・同点でない: `finishNextState = Result`
  - 時間切れ・同点: `finishNextState = TieBreak`（今回追加）
  - TieBreakの「結果発表」: `finishNextState = Result`
  - サドンデスの決着: `finishNextState = Result`
- `FinishThenResult()`（コルーチン名は変えていない）の末尾を`ChangeState(GameState.Result)`の
  決め打ちから`ChangeState(finishNextState)`に変更しただけ。Finish自体の見た目・待ち時間は変更なし
- これで「Resultへ行く経路は必ずFinishを経由する」（§4-1）に加えて
  「TieBreakへ行く経路（＝同点の時間切れ）も必ずFinishを経由する」が成り立つ。
  結果として、試合が終わる瞬間は常にFinishが最初に挟まる
- 実測: `finishNextState`を`TieBreak`にして`Finish`へ入り、`finishDuration`経過後に
  実際に`TieBreak`へ遷移することを確認。続けて`FinishWithResult()`（結果発表）を呼び、
  今度は`Finish`から`Result`→`Victory`シーンへ正しく進むことも確認済み

### 11-8. ほうきの湧き位置にビーコン

ほうきはマップに1本しかない貴重なアイテムなので、依頼により遠くからでも位置が分かる
薄いビーコンを追加した。

- `ItemPickup`に`beaconVisual`フィールドを追加。`SetVisualsActive`で`broomVisual`と同じ条件
  （`isBroom`）でON/OFFする。ほうき以外の中身のときは出ない
- 見た目は`MagicHandSceneBuilder.CreateBeacon`が作る、細長い円柱（半径0.18・高さ10）。
  足元（原点）から上へ伸ばす
- マテリアルは`CreateTransparentUnlitMaterial`（新規ヘルパー）。`Universal Render Pipeline/Unlit`を
  半透明（Surface=Transparent、Blend=Alpha、ZWrite off）に設定し、ほうきの`DisplayColor`と同じ
  金色（アルファ0.25、「薄く」の指定通り）を使う
- 実測: Playモードでほうきから離れた位置からスクリーンショットを撮り、金色の薄い光の柱が
  ほうきの真上に立っていることを確認済み

---

## 12. 2026-08-22 追加バッチ：イージーモード新設・UI改善

以下7件をまとめて実装。目玉は**イージーモード**（縮小マップ・アイテム減の練習向けモード）で、
既存の「ノーマルモード」アリーナとは別に、もう1つ小さいアリーナをまるごと生成して
遠く離れた場所に置き、準備ルームの設定で切り替える方式にした。

### 12-1. 巻物・ほうきの地面表示を2倍に

- `ScrollVisualLength`（`MagicHandSceneBuilder`）を`0.7→1.4`に変更するだけ。この定数は
  地面の巻物見た目にしか使わないので副作用なし
- ほうきは`CreateBroomModel`が「プレイヤーが持っているとき」と「地面に落ちているとき」の
  両方で共有されているため、定数を直接変えるとプレイヤーの杖代わりの見た目まで変わってしまう。
  そのため`CreateItemPrefab`側だけで`broomVisual.transform.localScale`を2倍にし、
  接地位置がズレないよう`localPosition.y`も`BroomLength*2/2`へ合わせて調整した

### 12-2. 得点表示を画面の境目へ

分割画面は左右分割（1P=左半分・2P=右半分）。得点表示（`ScoreText`）を、
**自分の画面の中で仕切り線に近い側**へ寄せた（1Pは右寄せで右端、2Pは左寄せで左端）。
これで仕切り線を挟んで両者の得点が隣り合い、一目で差が分かる。`BuildPlayerUI`内で
`index`（0/1）に応じてアンカーとテキストの整列を出し分けているだけで、`InGameHUD`側は無変更。

### 12-3. オプションに「ゲーム終了」を追加

`InGameOptionsMenu`の行に`EndGame`を追加（`Sensitivity/Invert/Fov/Controls`の次、5行目）。
決定は左右どちらでも反応すると誤操作の恐れがあるため、デバッグパネルのアイテム付与と同じ方式で
**L（右）だけ**に絞った。選ぶと`GameManager.ReturnToLobby()`を呼ぶ（リザルト画面の
「もう一度」と同じ、既存のメソッドをそのまま流用）。試合を強制終了して準備ルームへ戻る。

### 12-4. 準備ルームにアイテムの説明を追加

見本アイテムの名札（`ProximityLabel`、ワールド空間）は名前だけだったため、効果の説明文
（`ItemDefinitionSO.Description`、既にデータとしては存在していた）を画面に表示するように追加した。

- 新規`ItemDescriptionDisplay`（`Scripts/UI`）。見本アイテム全部を`samples`リストで持ち、
  毎フレーム「一番近いプレイヤーとの水平距離が`showDistance`（既定5m）以内の見本」を1つだけ選び、
  名前と説明文を画面固定サイズのUIパネルに出す。名札と違って**画面上の位置・文字サイズが
  常に一定**なので、遠近によらず読みやすい
- ハマった点1: `samples`を`[SerializeField]`なしの`private List<ItemPickup>`にして、
  シーン生成時に`RegisterSample()`を直接呼んで詰めていたが、**Unityは非シリアライズ
  フィールドをシーン保存時に書き出さない**ため、Playモードでシーンが読み込み直された瞬間に
  空リストへ戻ってしまい、何も表示されなかった。`[SerializeField]`を付けて
  `SetList(display, "samples", ...)`（既存の`SerializedProperty`経由ヘルパー）で書き込む方式に修正
- ハマった点2: `ItemDescriptionDisplay`を表示パネル自身（`panel.AddComponent<...>()`）に
  付けていたところ、パネルは初期状態`SetActive(false)`なので**コンポーネント自体が
  Updateを回せず、二度と自分で表示に戻れない**事故があった。カウントダウン等と同じパターン
  （`LobbyHUD`）に倣い、常時アクティブな`Canvas`ルート側にコンポーネントを付け、
  子のパネルだけを`SetActive`で切り替える形に直した
- ハマった点3: 画面上寄り（アンカーY 0.78〜0.92や0.94近辺）に置いたところ、俯瞰カメラの
  近くにある練習台の3Dモデルに**UIごと隠れて全く見えなかった**（`ScreenSpaceCamera`の
  Canvasは`planeDistance`の位置に置かれ、通常の3Dオブジェクトとの前後関係で隠れることがある）。
  画面中央よりやや下（アンカーY 0.22〜0.46あたり）に置き直して解決。準備ルームの俯瞰カメラに
  新しくUIを足すときは、この高さ帯を避けること

### 12-5. イージーモードの実装

#### 設定・切り替え
- `MatchSettings.EasyMode`（bool、`ToggleEasyMode()`で切替）を追加。他の共有設定
  （制限時間・アイテムON/OFF）と同じく1Pの設定パネルにだけ行を追加（`LobbySettingsPanel`の
  `RowKind.EasyMode`、`Duration`の次）
- 画面中央（`LobbyHUD`の`modeText`、アンカーY 0.22〜0.30）に「ノーマルモード」／
  「イージーモード」を常時表示。設定パネルの小さな文字だけだと切り替え忘れに気付きにくいため。
  §12-4と同じ理由で、この高さより上（俯瞰カメラに近い3Dモデルの位置）は避けている

#### 縮小マップ
既存のアリーナ生成コード（`BuildEnvironment`/`BuildUpperFloors`/`BuildUpperCover`/
`BuildRespawnPoints`/`BuildItemSpawners`とその配下）を、コピペで2本持たず
**同じコードに設定（`ArenaConfig`）を変えて2回通す**方式に書き換えた。

- `ArenaConfig`（`scale`・`upperScale`・`offset`・`includeThirdFloor`）を新設。
  - `Normal = (scale:1, upperScale:1, offset:0, 3階あり)`（数値は完全に旧来どおり）
  - `Easy = (scale:0.9, upperScale:0.9*0.82, offset:(0,0,600), 3階なし)`
  - 高さ（Y）方向はどちらもスケールしない。ジャンプ到達高さ等、既にチューニング済みの
    縦方向の値に触らないため。水平（X/Z）だけを縮める
  - `upperScale`は2階（回廊・ハブ・橋・スロープ）専用の縮尺で、`scale`よりさらに縮めることで
    「マップ全体を1割小さく、2階はさらに縮小」を表現している。スロープの勾配は
    その分だけノーマルより急になるが、範囲内で問題なく登れることを実測済み
- イージーは同じワールド空間の別地点（Z+600）に生成し、準備ルーム（Y-100）と同じ要領で
  干渉を避けている。地面のY座標はどちらも0のままなので、`PlayerFlight.arenaFloorY`等の
  高さ関連の値は変更不要だった
- 3階は`includeThirdFloor=false`のときは`BuildThirdFloor`の呼び出しごと省略。
  スロープ（`Ramp3F_NE/SW`）も同じ`ThirdFloor`オブジェクトの子なので同時に無くなる
- 客席（`BuildColosseum`）はノーマル側にしか生成していない。イージーは遠く離れた場所にあり、
  俯瞰しない試合中アリーナなので見た目の優先度が低いための判断
- `RespawnManager`・`ItemSpawnManager`もノーマル・イージーそれぞれ別インスタンス
  （`respawnManagerEasy`・`itemSpawnManagerEasy`を`GameManager`に追加）。
  `GameManager.ActiveRespawnManager`/`ActiveItemSpawnManager`が`MatchSettings.EasyMode`を見て
  使う方を選ぶ。準備ルームでモードを切り替えられるため、`Lobby`/`Title`に入るたびに
  **両方の`ItemSpawnManager`を`StopSpawning()`**して、モード切り替え後に前のアリーナへ
  アイテムが残り続けないようにしている

#### アイテムの個数・種類
- ノーマル: 手変更 湧き25箇所/常時20個、アイテム 湧き50箇所/常時20個（変更なし）
- イージー: 手変更 湧き20箇所/常時15個、アイテム 湧き30箇所/常時15個
- イージー専用の巻物抽選テーブル`Scroll_Random_Easy`（`CreateRandomScrollEasy`）を新設。
  チャーム（`HandScrambleEffectSO`）とブリンク（`TeleportEffectSO`）を候補から除外し、
  スピードUp・スタン・サーチの3種のみにした（駆け引きが複雑になりすぎるため）
- ほうきのビーコン（§11-8）は**イージー限定**に変更。`ItemPickup.SetVisualsActive`で
  `MatchSettings.Instance.EasyMode`を見て、ノーマルでは常にOFFにする
  （見本アイテム・両アリーナで同じプレハブを共有しているため、モードで出し分ける形にした。
  アリーナごとにプレハブを分ける必要がない）

#### 実測
Playモードで`MatchSettings.ToggleEasyMode()`→`GameManager.ChangeState(Selection)`と進め、
プレイヤーがZ+600（イージーアリーナ）・2階基準の高さ(Y=5.30)へ配置されることを確認。
`ItemSpawnManager_Easy`の`groups`を`SerializedObject`経由で読み、
`HandItems: target=15 points=20`・`Items: target=15 points=30`・
巻物候補が「スピードUp・スタン・サーチ」の3つのみであることを確認。
ほうきの`beaconVisual`がイージー中は`active=true`であることも確認。
ノーマル側は`HandItems: target=20 points=25`・`Items: target=20 points=50`・
巻物候補5種（変更前と同一）のままであることも確認済み。

---

## 13. 2026-08-22 追加バッチ2：得点表示・アイテム説明の1P/2P分離、設定文字の拡大

§12のスクリーンショットを見た依頼者から3件の追加修正。

### 13-1. 試合中の得点表示を「自分の点＋画面中央の-」に変更

以前（§12-2）は各HUDが`"YOU {self} - {rival} RIVAL"`とまとめて出していたが、
「真ん中を境に自身のポイントのみ表示して真ん中に-」という依頼で構成を変えた。

- `InGameHUD.scoreText`は自分の点数（数字だけ）を出す。仕切り線側の位置は§12-2のまま
  （1Pは自分の右端＝画面中央、2Pは自分の左端＝画面中央）
- 新規`ScoreDashUI`（`Scripts/UI`）。`FinishUI`/`StartUI`と同じパターンで、
  画面全体を覆う共有Canvas（`UI_Global`、`ScreenSpaceOverlay`）に「-」を1つだけ置く。
  分割画面の各HUDはカメラのビューポート（半分）の中でしか描けないため、
  仕切り線をまたぐ文字は片方のHUDだけでは置けない——共有Canvasでないと画面中央に置けない、
  という制約が理由。`Selection`/`InGame`の間だけ表示（`InGameHUD`の`inMatch`条件と同じ）
- 見た目のアンカーはScoreTextの縦位置（Y 0.86〜0.94）に合わせ、横は画面ちょうど中央
  （X 0.47〜0.53）。実測で「3」（1P）・「-」（中央固定）・「2」（2P）の3つが揃って
  並ぶことを、`ScoreText.text`が数字のみになっていること・`ScoreDashPanel`が
  `InGame`中に`active=true`であることの両方で確認済み

### 13-2. 準備ルームのアイテム説明を1P/2Pで左右に分離

以前（§12-4）は1つのパネルで「どちらかのプレイヤーに一番近い見本」だけを出していたが、
「1Pと2Pが持っているアイテムの説明を別々に、右側と左側で分けて」という依頼で構成を変えた。

- `ItemDescriptionDisplay`を、単一の`panel/nameText/descriptionText`から
  配列（`panels[2]`/`nameTexts[2]`/`descriptionTexts[2]`、0番=1P・1番=2P）に変更。
  毎フレーム「1Pに一番近い見本」「2Pに一番近い見本」を**別々に**探して、
  それぞれ独立に表示・非表示を切り替える（片方が範囲内でももう片方は無関係）
- パネルは画面左（1P、X 0.02〜0.35）・右（2P、X 0.65〜0.98）に分けて配置。
  どちらのプレイヤー用か分かるよう、隅に色分けした「1P」（黄）/「2P」（水色）の
  小さなラベルを追加した（プレイヤーラベルと同じ配色）
- 実測: 1Pをスピードアップの見本、2Pをブリンクの見本にテレポートさせ、
  左パネルが「スピードUp」、右パネルが「ブリンク」を同時に、互いに影響されず
  表示することを確認済み

### 13-3. 準備ルームの設定パネルの文字を拡大

「設定の中の文字を大きくわかりやすく」という依頼で、`CreateSettingsPanel`の文字サイズを
底上げした：タイトル（「1P設定」等）30→34、各行（視点感度・視野角・イージーモード等）19→24。
行の高さは既存の行数から自動計算されている（`rowHeight = 0.84f / rowCount`）ため、
サイズ変更にあたってレイアウト側の調整は不要だった。

---

## 14. 2026-08-22 追加バッチ3：アイテム名変更、アイテム説明ON/OFF、説明文の追記

### 14-1. アイテム名変更（ブリンク→ワープ、チャーム→チェンジ）

`MagicHandSceneBuilder.CreateScrolls`の`displayName`を変更しただけ
（`TeleportEffectSO`→「ワープ」、`HandScrambleEffectSO`→「チェンジ」）。表示名は
`ItemDefinitionSO.DisplayName`を経由して全UI（HUD・準備ルームの見本・アイテム説明・
デバッグパネルの付与メニュー・設定パネルのON/OFF一覧）に自動で反映されるため、
UI側の追加修正は不要だった。

C#のクラス名・ファイル名（`TeleportEffectSO`/`HandScrambleEffectSO`/
`BlinkTargetIndicator`）、SEのアセットパス定数（`SeBlinkPath`/`SeCharmPath`、
実ファイルが`俊敏15（ブリンク）.mp3`のように旧名のまま存在する）は**変更していない**。
これらは内部識別子・実ファイル参照であり、プレイヤーの目に触れる「アイテム名」ではないため。
一方、旧名を書いていたコード中のドキュメントコメント（十数箇所）は、読んだときに
矛盾しないよう新名に合わせて更新した

### 14-2. アイテム説明のON/OFF切り替え

§12-4で追加した準備ルームのアイテム説明パネルを、個人の好みで消せるようにした。

- `MatchSettings.showItemDescription`（`bool[2]`、既定`true`）を追加。§13-2で
  1P/2Pの表示を分離した経緯と同じ理由で、**プレイヤーごとに**ON/OFFできるようにしてある
  （視点感度や視野角と同じ「個人設定」の扱い）
- `LobbySettingsPanel`に`RowKind.ItemDescription`を追加。1P・2Pどちらのパネルにも
  出る個人設定なので、`includeSharedSettings`の条件を付けず`Fov`の直後に置いた
  （制限時間・イージーモードのような共有設定より前）
- `ItemDescriptionDisplay.Update()`で、そのプレイヤー番号ぶんの
  `MatchSettings.IsItemDescriptionEnabled(i)`を見て、OFFならそのプレイヤーの
  パネルだけ常に非表示にする（もう片方には影響しない）
- 実測: `MatchSettings.ToggleItemDescription(0)`を呼んで1Pだけ`false`にし、
  1Pの`ItemDescriptionPanel_1P`が`active=false`に切り替わることを確認。
  設定パネル側も対象プレイヤーの行だけ「アイテム説明　OFF」に変わり、
  もう一方のプレイヤーの行・パネルは影響を受けないことを確認済み

### 14-3. 説明文の追記（ほうき・スタン）

依頼の「ほうきは使用中アイテム、手変更アイテムを取得できない」「スタンでは、相手を
スタンさせると相手はワープ以外のアイテムの使用ができなくなる」は、どちらも**既存の挙動**
（`ItemPickup.OnTriggerEnter`の`if (player.IsRiding) return;`、
`PlayerController.CanUseScroll`の`if (IsStunned) return scrolls.Current is TeleportEffectSO;`）
の説明が抜けていたという指摘。実装は変えず、`description`文字列にその挙動を追記した：

- ほうき: 「5秒間 自由に飛べる。着地すると3秒間 位置が相手にバレて足が遅くなる。
  **使用中はアイテムも手変更アイテムも拾えない。**」
- スタン: 「周囲の相手を短時間スタンさせる。**スタン中の相手はワープ以外の
  アイテムを使用できなくなる。**」

---

## 15. アイテム説明パネルの文字はみ出しを修正（2026-08-22）

依頼: 「文字がはみ出しているので二行にしたりして改善して」。§14-3で説明文（特にほうき）が
長くなった結果、準備ルームのアイテム説明パネル（`ItemDescriptionDisplay`、§12-4/§14-2）で
1行に収まらず枠の外にはみ出していた。

### 原因
`CreateText`ヘルパーはデフォルトで`horizontalOverflow = HorizontalWrapMode.Overflow`
（折り返さない）。タイトルやラベルなど短い文字列を前提にした既定値で、
説明文のような長い文章には合っていなかった。

### 対処
`MagicHandSceneBuilder.CreateItemDescriptionSide`のみを変更（`CreateText`自体の既定値は
他の短い文字列に影響するため触っていない）:

- 説明文の`Text`に`horizontalOverflow = HorizontalWrapMode.Wrap`を設定して折り返すように変更
- 折り返すと縦に伸びるため、枠自体も広げた: パネルを幅0.33→0.38・高さ0.14→0.20に拡大
  （`BuildItemDescriptionPanel`のアンカー）。パネル内の名前欄と説明欄の配分も、
  説明欄によりスペースを回すよう調整（説明欄 0.05〜0.50→0.04〜0.64、フォントサイズ20→18）
- 縦方向（`verticalOverflow`）は`Overflow`のままにした。3行になるような長い説明文でも
  文字を切り詰めず全文を出す方針（枠を大きくしたので通常は2〜3行で収まる）

### 検証方法
Playモードで1Pをいちばん説明文が長い「ほうき」の見本に近づけ、俯瞰カメラ
（`Camera_Lobby`）をレンダリングして確認。「5秒間 自由に飛べる。着地すると3秒間／
位置が相手にバレて足が遅くなる。使用中はアイテムも手変更アイテムも拾えない。」の
全文が枠内に収まって折り返され、はみ出しが無いことを確認した。

### この節でMCP経由の作業中に新たに踏んだ罠
- **`run_csharp`だけが単独でタイムアウトし続け、他のツール（`unity_analyze_console_logs`、
  `unity_get_scene_summary`等）は正常に動くことがあった**。切り分け方法: まず対象のツールを
  `execute`経由の生の`curl`で直接叩いてみる。`curl`でも失敗するならUnity側の問題、
  `curl`は通るのに`run_csharp`だけ失敗するならMCPクライアント側の一時的な不調である
  可能性が高い（実際、数分〜十数分待って`run_csharp`を再試行すると復帰した）
- **`run_csharp`が使えない間の代替手段**: `unity_capture_game_view`
  （`unity_get_screenshot_result`と対で使う。Playモードへの遷移も自動でやってくれる）を使うと、
  C#を書かなくてもゲーム画面のスクリーンショットが撮れる。今回はこれで
  「Unityは実際には動いている」ことを先に確認してから`run_csharp`の復帰を待てた
- **`Object.FindFirstObjectByType<T>()`のジェネリック呼び出しは、この評価器では
  `resultSet:false`になりやすい**（§3に既出の制約と同種）。今回は
  `ItemDescriptionDisplay`が付いている`GameObject`名（`"UI_Lobby"`）が分かっていたため、
  `GameObject.Find("UI_Lobby").GetComponent<T>()`に切り替えて回避した

---

## 16. 得点表示（画面上部の「3 - 2」）を大きく、ハイフンを中心に（2026-08-22）

依頼: 「これの表記を大きくして見やすく／ハイフンを境の中心に」。§13-1で追加された
画面上部の得点表示（`InGameHUD.scoreText` と `ScoreDashUI` の組み合わせ）が
小さく読みにくかったという指摘。

### 仕組みのおさらい
自分の点は各プレイヤー自身のHUD（分割画面のCanvas、`InGameHUD.scoreText`）が
仕切り線ぎりぎりに出し、両者の間に挟む「-」だけは画面全体を覆う共有Canvas
（`UI_Global`、`ScoreDashUI`）に別で置いてある。分割画面のCanvasは片方の
カメラのビューポート内でしか描けず、ちょうど画面中央には置けないため
（詳しくは`ScoreDashUI.cs`のコメントとHANDOFF内§13参照）。

### 実際の修正
`MagicHandSceneBuilder.cs`の2箇所を変更しただけ:

- `BuildPlayerUI`の得点テキスト: フォント34→56、縦の表示域を0.86〜0.94→0.80〜0.94へ広げて
  大きい文字でも縦に収まるようにした。加えて仕切り線側の余白を広げた
  （1P: 右端0.99→0.96、2P: 左端0.03→0.04）。詰めすぎて「-」に数字がくっついて
  見えていたのを緩和する狙い
- `BuildScoreDashUI`の「-」: フォント34→56、アンカーを(0.47,0.86)〜(0.53,0.94)から
  (0.44,0.80)〜(0.56,0.94)へ拡大。**横方向は0.44〜0.56のまま中心0.50を維持**しており、
  数式上は元から画面中央だった（`UI_Global`は`ScreenSpaceOverlay`の全画面Canvasで、
  各プレイヤーのカメラの`viewport`も`(0,0,0.5,1)`/`(0.5,0,0.5,1)`ちょうど半分なので、
  ズレる要素は無かった）。実測して確認したところ問題なく中心に来ており、
  依頼の「センターに寄せる」ための特別な補正コードは不要だった。
  文字が大きくなって数字との間隔が空いたことで、見た目にも中心にあると分かりやすくなった

### 検証方法
Playモードで`GameManager`の`scores`（private配列、リフレクションで直接書き換え）を
3-2に設定し、`unity_capture_game_view`でゲーム画面全体（分割画面込み）を撮影。
「3 - 2」が以前よりはっきり大きく表示され、画面全体の横幅（1819px）のほぼ中央に
「-」があることを目視で確認した。

### 検証で使ったテクニック
`GameManager.scores`は`private readonly int[]`でスコア加算メソッド（`ResolveContact`）を
経由しないと変えられないが、`ResolveContact`はノックバック・スタン・リスポーンまで
一括で走ってしまい見た目のテストには不向き。**readonlyな配列フィールドでも、
配列そのもの（参照先の中身）へは`GetField(...).GetValue(gm)`で取り出して
要素を直接書き換えれば良い**（フィールドの「参照を差し替える」わけではないので
readonly制約に触れない）。`SerializedObject`経由（`[SerializeField]`が付いていない
private配列には使えない）より素直に通った。

---

## 17. アイテム説明の枠調整・得点/残り時間の拡大・残り時間の通知・優位劣位マーク・手変更アイテムの均等湧き（2026-08-22〜23）

依頼: 「黒枠の大きさ調整／点数と残り時間を一回り大きくして／残り時間1分で中心に残り1分通知を
黄色で、残り10秒で赤色で強調表示／相手の頭上の手表示と本体の間に、自分が相手の手に
勝っているか（優位/劣位/互角）を、どの角度から見ても正面に見える形で表示／手変更アイテムの
数がマップ上におおよそ均等になるよう調整」。一度に5件の依頼だったため、まとめて対応した。

### 17-1. アイテム説明の黒枠を縮小
§15で説明文の折り返しに対応した際、いちばん長い「ほうき」の説明文（3行）に合わせて枠を
広げたが、短い説明文（「スタン」など2行）だと枠の下側が大きく余って間延びして見えた。
`BuildItemDescriptionPanel`のアンカーを高さ0.20→0.15に縮め、内訳もやや詰めた。

### 17-2. 点数・残り時間を拡大
`TimerText`を54→64、`ScoreText`を56→64（前回§16で34→56にした続き）、
`ScoreDashUI`の「-」も56→64に統一。タイマーの表示域も縦に少し広げた
（0.86〜0.99 → 0.84〜0.99）。

### 17-3. 残り1分／残り10秒の通知
`InGameHUD`に`timeAnnounceText`（画面中央、初期非表示）を追加。`Update`内で
`manager.Timer.Remaining`を見て、60秒以下になった瞬間に1回だけ「残り1分」を黄色
（`RGBA(1,0.9,0.2,1)`）で、10秒以下になった瞬間に1回だけ「残り10秒」を赤
（`RGBA(1,0.3,0.25,1)`）で出す。`announceDuration`（既定2.5秒）だけ表示してから
自動的に消える。加えて、最後の10秒間は`TimerText`本体も赤くなり、
`Mathf.Sin(Time.unscaledTime*9f)`でわずかに拡大縮小させて緊迫感を出している。

- 発火は「一致した瞬間だけ」なので、`announcedOneMinute`/`announcedTenSeconds`の
  boolフラグで一度きりにしてある。新しい試合が始まった瞬間（`!wasInMatch && inMatch`）に
  フラグを戻さないと、次の試合で二度と通知が出なくなるため、そこだけは必ず戻す
- サドンデス中は時計そのものが止まっている（表示も「サドンデス」に変わる）ため対象外にした
- **実装済みの`CountdownText`ブロックに元から`float remaining`というローカル変数があり、
  新しく追加した`float remaining = manager.Timer.Remaining;`と名前が衝突してCS0136で
  コンパイルエラーになった**。後から読む人がハマりやすいので、既存のローカル変数を
  `countdownRemaining`に改名して解決した

### 17-4. 優位/劣位/互角マーク（`HandAdvantageIndicator`、新規）
相手の頭上の手表示と本体の間に、「自分が相手の手に勝っているか」を出す。
`owner`（マークが付いている本人）と`viewer`（比べる相手＝マークを見る側）を持ち、
`viewerHand.Beats(ownerHand)`なら優位（緑の上向き三角）、`ownerHand.Beats(viewerHand)`なら
劣位（赤の下向き三角）、どちらでもなければ互角（白のひし形）を出す。
`PlayerHandIndicator`と同じ「頭上表示は自分には見えず、相手にだけ見える」レイヤー
（`rivalLayer`）に乗せてある。owner本体だけ渡してビルドし、両プレイヤーが揃ってから
`WireHandAdvantageIndicators`でviewerを配線する（§7-12のPlayerHandIndicatorと同じ2段階）。

**この節でいちばん時間を使ったのは見た目の不具合切り分けだった**:

1. 最初は`LineRenderer`（`alignment = LineAlignment.View`、`SpeedUpEffect`/`StunEffect`と
   同じ作り方）で実装した。ところが**プレイヤー同士が正面から向き合う（いちばん多い状況）と、
   カメラの視線とマークの線分がほぼ平行になり、Viewの内部計算（線分に垂直な向きをカメラ方向との
   外積で求める）が破綻して、画面の端から端まで伸びる巨大な線になる不具合**が実測で見つかった
   （まさに対面のときにいちばん見たい表示なのに、いちばん多い状況で壊れるという最悪の組み合わせ）
2. `alignment = LineAlignment.TransformZ`（回転しない固定形状）にして、代わりにスクリプト側で
   Transform自体を`viewer.CameraRig.transform`へ`Quaternion.LookRotation`で向ける
   「本物のビルボード回転」に変えた。しかしこれも**特定の角度で幅が潰れて完全に見えなくなる**
   別の不具合が出た（`TransformZ`の幅の向きの内部計算が、線分の向きと`transform.up`の外積に
   依存しており、回転のさせ方次第でここも不安定になったと見られる）
3. 最終的に**単純な三角形メッシュ（`MeshFilter`/`MeshRenderer`、両面描画のため同じ3頂点を
   両方の巻き順で2枚重ねる）に作り直して解決した**。メッシュなら「幅」を線分方向から
   逆算するような内部計算が無く、頂点をそのまま描くだけなので、この手の不具合が起きようがない。
   ビルボード回転はLineRenderer版と同じスクリプトのやり方をそのまま流用できた
- 位置は「頭上表示(2.3付近、大きさ1.05)より下、体の高さ」を狙ったが、**キャラの中心
  （X=0,Z=0）にそのまま置くと体のメッシュに埋もれて外からは見えなかった**。ローカルZ+方向
  （キャラの前方）へ0.4mほど浮かせてようやく体の外に出た。高さも帽子のつば（実測で1.5〜1.6m
  付近）に隠れない1.3mまで下げてある
- **この一連の切り分けで学んだこと**: LineRendererのbillboard系alignment（View/TransformZ）は
  「特定の相対角度で内部計算が破綻する」弱点を両方とも持っている。カメラに正対させたい・
  かつ角度が読めない（プレイヤーの向き次第で決まる）ワールド空間マークは、最初から
  メッシュ＋スクリプトビルボードで作った方が結局早い

### 17-5. 手変更アイテムの均等湧き
`ItemSpawnManager.PickEnabledLoot`を、単純な均等抽選から**「今マップ上にいちばん少ない
種類だけに絞ってから選ぶ」**方式に変更した。有効な候補それぞれの`CountAliveOfDefinition`を
数え、最小値と同じ候補だけを`leastRepresentedBuffer`に集めてその中から抽選する。
手変更（グー/チョキ/パー）に限らず全グループに効く一般的な変更だが、他のグループ
（巻物側）は§11-3で「拾った瞬間に中身を抽選する」方式に変わっていて湧いた時点では
まだ中身が決まっていないため、実質的に効果が出るのは手変更グループだけになる。

### 検証方法
Playモードで実際に確認：
- アイテム説明の枠：ほうきの見本に近づいて縮んだ枠と2行の折り返しを確認
- 得点/残り時間：`unity_capture_game_view`でスクリーンショットを撮り拡大を確認
- 残り1分/10秒通知：`GameManager.Timer`の`Remaining`をリフレクションで直接書き換えて
  閾値をまたがせ、`InGameHUD`のフィールドと`Text`の状態を直接読んで発火を確認。
  自動的に消えるまでの時間（2.5秒）がツール呼び出しの往復時間より短く、
  スクリーンショットで「表示された瞬間」を狙い撃ちするのは難しかったため、
  最終的には状態を手動で再現してから撮って見た目（文字・色・位置）だけを検証した
  （発火ロジック自体はフィールドの値を直接読んで確認済み）
- 優位/劣位マーク：1Pグー・2Pチョキで向き合わせ、1P視点で2Pの体に緑の▲（優位）、
  2P視点で1Pの体に赤の▼（劣位）が出ることを確認
- 均等湧き：ロジックの見直しのみ。長時間の統計的な検証はしていない（アルゴリズム自体は
  シンプルで、`CountAliveOfDefinition`が既存のヘルパーをそのまま使っているため低リスクと判断）

### この節でMCP経由の作業中に新たに踏んだ罠
- **`unity_force_refresh_assets`を呼んで`unity_analyze_console_logs`でエラー無しを確認しても、
  実際にはまだ古いコンパイル済みアセンブリのまま`BuildScene()`が走ってしまうことがあった**
  （実測: `TimerText`のフォントサイズをコードで64に変えたのに、ビルド後のシーンで54のまま
  だった）。原因はコンパイルエラー（§17-3の`remaining`名前衝突）で、`force_refresh_assets`
  経由だとその後のエラーチェックが実際のコンパイル完了より早く走ってしまい、
  エラーが無いように見えていた。**`UnityEditor.AssetDatabase.ImportAsset(path,
  ImportAssetOptions.ForceUpdate)`を`run_csharp`から直接呼ぶ方が確実**（呼んだ直後に
  `[ScriptCompilation] Requested script compilation because: ...`のログが返ってくるので、
  本当にコンパイルがキューに入ったことをその場で確認できる）。以後、コード変更後は
  この方法を使い、`BuildScene()`の後に実際にシーン内の値を読んで反映を確認する
  （ログのタイムスタンプやシーンファイルの更新日時だけでなく、フィールドの値そのものを見る）
  習慣を徹底した
- Play中に一時カメラを動かして特定のワールド空間オブジェクトを覗こうとするとき、
  **`LineAlignment.View`/スクリプトビルボードは「今まさにその一時カメラへ向いている」とは
  限らない**（本番のビルボードは実際のプレイヤーカメラへ向けてあるため）。狙った角度で
  見えない場合は、まず対象の実際の回転・位置をコードで読んで、どちらのカメラへ向いているかを
  確認してから一時カメラをそこへ合わせる方が早い

---

## 18. 残り1分の間、タイマー本体も黄色のままにする（2026-08-23）

依頼: 「1分で色変わらない」。§17-3で作った「残り1分/10秒」の仕組みは、
**画面中心の通知（`timeAnnounceText`）だけを一瞬（2.5秒）黄色で光らせ、
タイマー本体（`timerText`）は残り10秒からしか赤くしていなかった**。依頼は
「残り1分の間はタイマーの表示そのものが黄色であってほしい」という意図だったが、
実装が「一瞬光る通知」と「タイマー本体の色」を別物として作ってしまい、
後者に対応する分岐が無かったために「1分経っても色が変わらない」ように見えていた。

### 対処
`InGameHUD.Update()`に`finalMinute`（60秒以下かつ10秒より上）の判定を追加し、
`timerText.color`の分岐に差し込んだ:

```
サドンデス中の色 > 残り10秒以下なら赤 > 残り1分以下なら黄色 > それ以外は白
```

中心の通知（一瞬だけ光る「残り1分」「残り10秒」の文字）は§17-3のまま変えていない。
タイマー本体の色は通知が消えたあとも変わったままになるので、「もう1分切っている」
「もう10秒切っている」という状態が常に一目で分かる。

### 検証方法
`GameManager.Timer.Remaining`をリフレクションで51秒に設定し、`InGameHUD`の
`timerText.color`を直接読んで`RGBA(1,0.9,0.2,1)`（黄色）になっていることを確認。
`unity_capture_game_view`でも実際に「0:27」が黄色く表示されていることを
スクリーンショットで確認した。

---

## 19. デバッグモードのL限定を解除／ほうきの上昇下降にRT・LTを追加（2026-08-23）

依頼: 「デバックモードのL決定を削除」「ほうきの上昇下降をLRとプラスしてRT、LTを追加して」。

### 19-1. デバッグパネルのアイテム付与、左右どちらでも決定できるように

`InGameOptionsMenu.ApplyDebug()`は、誤操作防止のつもりで
「アイテム付与の決定はL（左スティック右方向）だけに絞る」という制限
（`if (step.x <= 0f) return;`）を持っていた。この制限自体が「右にしか反応しない」
という分かりにくい挙動になっていたため、依頼どおり削除し、左右どちらの入力でも
同じ付与処理が走るようにした。あわせて`RefreshDebugPanel()`の表示テキストからも
古い「（L）」ラベルを外した。

- なお、通常のオプションメニュー（デバッグパネルではない方）の「試合終了」項目にある
  類似のL限定（`Apply()`内、コメント「決定操作としてL（右）だけに絞る」）は
  今回の依頼が「デバックモード」に限定していたため、意図的に触っていない
- 変更ファイル: `Assets/_Game/Scripts/UI/InGameOptionsMenu.cs`

### 19-2. ほうきの上昇下降にRT・LTを追加

上昇（Ascend）/下降（Descend）は元々ゲームパッドの`rightShoulder`/`leftShoulder`
（R1/LB相当）とキーボードのSpace/Shiftにバインドされていた。これに**加えて**
`<Gamepad>/rightTrigger`（Ascendへ）と`<Gamepad>/leftTrigger`（Descendへ）を追加した。
既存のバインドを置き換えるのではなく、同じアクションに対する追加バインドとして
JSON中に新しいbindingブロックを挿入する形にしてある（`PlayerController`側の
`ReadVerticalInput()`はアクション名でポーリングしているだけなので、バインド追加に
コード変更は不要）。

Ascend/Descendアクションは`type: "Button"`（`expectedControlType: "Button"`）で
定義されている。トリガーは本来アナログ軸（float）だが、Input Systemは
Button型アクションにアナログ軸を直接バインドした場合、既定の押下しきい値（0.5）で
自動的にオン/オフへ変換する。これは既存のrightShoulder/leftShoulderバインドと
同じ仕組みなので、processorやinteractionの追加設定は不要だった。

**Gameplayマップ・Lobbyマップの両方**に同じバインドを追加した
（`Assets/_Game/Input/MagicHandControls.inputactions`内に同名の2つのアクションマップが
存在し、それぞれ独立したbindings配列を持っているため、片方だけ直すと
ロビー画面とインゲームで挙動が食い違う）。

#### ハマった点：2P用コピー（`MagicHandControls_P2.inputactions`）が自動更新されない

`AssignInputActions()`は「`MagicHandControls_P2.inputactions`が**存在しない場合だけ**
プライマリからコピーする」という一度きりの複製ロジックになっている。そのため
プライマリ側にバインドを追記しても、既に存在するP2ファイルには反映されない。
今回はP2ファイル（と`.meta`）を削除してから`BuildScene()`を呼び、複製し直させる
方針を取った。

ただし1回目の`BuildScene()`直後に確認したところ、なぜかP2ファイルが
生成されていなかった（`AssignInputActions`内の`AssetDatabase.CopyAsset`が
効いていないように見えた）。原因の切り分けはできていないが、直前に
`AssetDatabase.ImportAsset(path, ForceUpdate)`でプライマリの`.inputactions`を
強制再インポートした直後にビルドを走らせたため、インポート処理と
`CopyAsset`が競合した可能性がある。`run_csharp`から`AssetDatabase.CopyAsset()`を
直接呼んで手動でP2ファイルを作り直し、その状態でもう一度`BuildScene()`を
呼び直したところ、以後は正常にP2ファイルが維持されるようになった。
**教訓**: `.inputactions`をForceUpdateした直後に`BuildScene()`を1回呼んだだけで
P2側の複製有無を判断しない。念のため生成後に該当ファイルの存在を
`ls`等で直接確認すること。

### 19-3. 操作説明パネルの表記更新

`MagicHandSceneBuilder.cs`の操作説明テーブル（`BuildControlsHelpPanel`相当、
line ~2785）にある「飛ぶ（上／下）」の行を
`"R1・L1 / RB・LB"` → `"R1・R2・L1・L2 / RB・RT・LB・LT"` に更新した。

### 検証方法
- `AssetDatabase.ImportAsset(path, ForceUpdate)`でコンパイルを強制し、
  `unity_analyze_console_logs`でエラー0件を確認
- `BuildScene()`後、プライマリ・セカンダリ両方の`InputActionAsset`を
  `AssetDatabase.LoadAssetAtPath`で読み込み、`Gameplay`/`Lobby`両マップの
  `Ascend`/`Descend`アクションの`bindings[].effectivePath`を列挙して、
  `<Gamepad>/rightShoulder,<Gamepad>/rightTrigger,<Keyboard>/space`
  （Descendも同様にleftShoulder/leftTrigger/leftShift）になっていることを
  P1・P2両方のアセットで実測確認済み
- `InGameOptionsMenu.cs`の変更はコード差分ベースの確認（左右どちらの`step.x`でも
  同じ付与処理を通る一本のパスになったことをソース上で確認）。デバッグパネルは
  実機コントローラー入力のシミュレーションが難しいため、実際のゲームパッド操作での
  最終確認はまだしていない

---

## 20. 優位/劣位/互角マークを図形から文字表示に変更、サイズ不具合を修正（2026-08-23）

依頼: 「優位、劣位、互角で表示どの角度から見ても、正面から見える形にして。優位、劣位、互角のまま文字で表示」。
§17で作った図形（三角形/ひし形）マークを、実際の文字（"優位"／"劣位"／"互角"）に差し替えてほしいという依頼。
「どの角度から見ても正面を向く」というビルボード要件自体は図形版から変えず維持する。

### 20-1. 実装

`HandAdvantageIndicator`のメッシュ生成部分を丸ごと`TextMesh`コンポーネントに置き換えた
（`Assets/_Game/Scripts/Player/HandAdvantageIndicator.cs`、
`Assets/_Game/Editor/MagicHandSceneBuilder.cs`の`BuildHandAdvantageIndicator`）。
フォントは他のUIテキストと同じ`BuiltinFont()`（`LegacyRuntime.ttf`、日本語を含む動的OSフォント）を再利用。
色は`TextMesh.color`（頂点カラー）で個体ごとに設定するので、共有マテリアルを書き換える心配がない
（`MaterialPropertyBlock`が不要になった）。

#### ハマった点：TextMeshは裏表があるので、ビルボードの符号を間違えると鏡文字になる

図形（三角形）版は両面描画だったのでビルボードの回転方向がどちらでも見た目に影響しなかったが、
文字は裏表があるため、`Quaternion.LookRotation`に渡すベクトルの符号を間違えると鏡文字（反転した
読めない文字）になる。以下の手順で切り分けた：

1. まっさらな`TextMesh`（回転ロジックなし）をシーンに作り、`(0,0,-3)`から`+Z`を見るカメラと
   `(0,0,3)`から`-Z`を見るカメラの2つでレンダリングして比較。前者は正しく読める文字、
   後者は鏡文字だった → **TextMeshの読める面はローカル-Z側**（オブジェクトの-Zがカメラを向く必要がある）
2. これは「forwardを"カメラの方向"にする」（三角形版でも使っていた式）ではなく、
   「forwardを"カメラと逆方向"にする」必要があることを意味する。図形版の
   `Quaternion.LookRotation(viewerCamPos - transform.position)`をそのまま流用せず、
   `Quaternion.LookRotation(transform.position - viewerCamPos)`に符号を反転して実装した

（余談：この検証中に一度、手動でマークの回転を上書きしてレンダーする2ステップのテストを組んだところ、
ツール呼び出しの間に実際のビルボードスクリプト（`LateUpdate`）が本物の視点カメラに向けて
毎フレーム回転を上書きしてしまい、狙った角度で撮れず鏡文字に見える瞬間があった。
`HandAdvantageIndicator`コンポーネントを一時的に`enabled=false`にしてから手動で角度を設定する
ことで、この競合を避けて確実に検証できた）

### 20-2. ハマった点その2：文字が旧マークの4倍のサイズになっていた（表示されないように見えた原因）

依頼後の最初の実装では`fontSize=64, characterSize=0.1`にしていたが、実測したところ
ワールド座標で幅1.28×高さ0.7（旧三角形マークは幅0.32×高さ0.16）と、**縦横とも約4倍**の
大きさになっていた。配置位置`localPosition=(0,1.3,0.4)`は「帽子のつば（1.5〜1.6付近）より下」を
狙って決めた高さだったが、高さ0.7の文字だと上端が`1.3+0.35=1.65`まで届いてしまい、
帽子のつばに頭が突き刺さる形になって角度によっては自分の帽子メッシュに隠れて見えなくなっていた。
これが「頭と手の間に表示が出ていない」という報告の実体だと考えられる。

`characterSize`を`0.045`に縮小し、実測でおよそ幅0.58×高さ0.32（上端が`1.3+0.16=1.46`で
つばの下に収まる）まで縮めて解決した。

### 検証方法
- `AssetDatabase.ImportAsset(ForceUpdate)`でコンパイル確認、`unity_analyze_console_logs`でエラー0件
- Play modeに入り、両プレイヤーの`CurrentHand`（`<CurrentHand>k__BackingField`）を
  リフレクションでGu/Chokiに強制設定し、`HandAdvantageIndicator`が
  `meshRenderer.enabled=true`・`textMesh.text="劣位"`（Gu対Chokiの組み合わせで期待通り）に
  なることを実測確認
- 一時的なRenderTextureカメラで、実際のフォントマテリアル・実際のレイヤー（P2Only等）・
  実際のワールド座標のまま、文字が正しい向き（鏡文字でない）で、かつ帽子のつばの下に収まる
  サイズで描画されることをスクリーンショットで確認
- 実際の`Camera_P2`（分割画面用、`targetTexture`を一時的に差し替えて`Render()`）からも撮影を
  試みたが、テスト時点でP1とP2の位置が離れており（リフレクションで手だけを設定し、実際の
  移動・接近はさせていないため）、フレーム内にP1が入らなかった。これは今回の検証手法上の
  制約であり、マーク自体の不具合ではない（三角形版のときも同じ理由で「近距離での見た目」は
  手動で作ったカメラでしか確認できていない）

---

## 21. 準備ルーム設定パネルの決定をB/○にも対応、十字キー下で煽りエモート（2026-08-23）

依頼: 「[準備ルーム設定パネルの]決定を十字キー左右でもできたがXBOXでのB、PSでの◯でも反応するように変更」
「オプションを押していない時に十字キー下を押すことで[指定の.animファイル]の煽りエモートをできる機能を実装」

### 21-1. 準備ルーム設定パネルにB（Xbox）／○（PS）の決定を追加

画面写真の「1P 設定」パネルは`LobbySettingsPanel.cs`（`InGameOptionsMenu.cs`ではなく、
準備ルーム専用の別クラス）。感度・視野角・制限時間のような値調整の行は左右で結果が変わるが、
上下反転・アイテム説明・イージーモード・操作説明・アイテム設定（展開/戻る）のようなトグル系の
行は「左右どちらを押しても同じ結果」になっている。これが依頼文の「十字キー左右でもできた」の
実体。

`LobbySettingsPanel`に`Confirm()`を追加し、値調整系の行（Sensitivity/Fov/Duration）では
何もせず、それ以外の行では`Navigate(0, 1)`（右を押したのと同じ処理）を呼ぶようにした。
値調整の行を除外したのは、決定ボタンを押しただけで感度や制限時間が意図せず1段階変わって
しまうのを避けるため。

入力側は`Assets/_Game/Input/MagicHandControls.inputactions`の`Lobby`マップに新規アクション
`Confirm`を追加し、`<Gamepad>/buttonEast`（Xbox B／PS ○）をバインドした
（既存の十字キー決定を置き換えるのではなく追加）。`LobbyMenuController`に
`OnConfirm(InputValue value)`を追加し、`GameState.Lobby`中だけ`panel.Confirm()`を呼ぶ。

### 21-2. 十字キー下で煽りエモート

`Assets/_Game/aoriemo-------------to/emo-toAnimation.anim`（Genericリグ向け、`Main_Rig/Spine00`
等のボーンパスを持つ約2.27秒のクリップ、`m_LoopTime: 1`）を再生する機能。

新規`PlayerTauntController.cs`（`Assets/_Game/Scripts/Player/`）をプレイヤー本体に追加し、
`MenuNavigate`（十字キー）の下入力をエッジ検出してAnimatorの`Taunt`トリガーを発火する。
条件は「試合中（`GameState.InGame`）」「十字キーがオプション/デバッグパネルに取られていない
（`InGameOptionsMenu`に`IsInputCaptured`プロパティを新設）」「`player.CanAct`（選択中/スタン中/
被弾直後でない）」の3つ。オプション/デバッグパネルはどちらも同じ`MenuNavigate`を使うため、
依頼文の「オプションを押していない時」は実装上「オプション/デバッグパネルが十字キーを
使っていない時」に対応させた。

`Assets/_Game/Animations/PlayerAnimator.controller`（既存の`Hit`トリガーと全く同じ構造で）に
Trigger型パラメータ`Taunt`、AnyState→`Taunt`状態への遷移（条件`Taunt`）、`Taunt`→
`Locomotion_Unarmed`への戻り遷移（`exitTime: 0.9`、無条件）を追加。この`.controller`は
Unity上で手で組んだものではなく、既存の`Hit`ブロックをテンプレートにYAMLを直接編集して作った
（UnityEditor.Animations.AnimatorController経由で読み込み、パラメータ/状態/遷移が全て
意図通りに存在することをコードから実測確認済み）。

#### 重要な発見：MCP経由のPlay modeテストでは、実際のフレーム駆動のAnimator評価にトリガーが反映されないことがある

`Animator.SetTrigger()`を呼んだ直後に実時間で（`sleep`を挟んで）状態を確認すると、
**新規のTauntだけでなく、既存で動作実績のあるHitトリガーも同様に遷移が起きたように見えない**
という現象が再現した。一方、`Animator.Update(dt)`を同じ`run_csharp`呼び出し内で
数回連続して手動で呼ぶと、TauntもHitも正しく遷移する。

切り分けた結果:
- コントローラーのデータ構造（パラメータ・状態・遷移条件）は実測で完全に正しい
- `PlayerTauntController.Update()`が十字キー下を正しく検知し、`SetTrigger`を呼んでいることも
  `wasDown`フィールドの実測で確認済み
- それでも実際のUnityエンジンの自動フレーム更新（1秒間に900フレーム以上回っているのを
  `Time.frameCount`で確認済み、`Time.timeScale`も1で正常）では遷移が反映されない
- 既存のHitトリガーも同一条件・同一テスト方法で同じ現象を示した

これは「MCPのrun_csharp経由でPlay中のUnityにアクセスするとき、外部から呼んだ
`Animator.SetTrigger`がその後の自動フレーム更新に正しく伝播しないことがある」という、
このテスト環境固有の制約だと判断した（Hitのような既存の動作実績がある機能まで
同じ形で「見えなく」なるため、TauntやAnimatorControllerの実装不備ではない）。
実際のゲームパッド操作（本物のフレームループの中で入力→SetTriggerが起きる）では
この問題は起きないはずだが、**実機のコントローラーでの最終確認はまだできていない**。
次にこの機能を触るときは、まずコントローラーで実際に十字キー下を押して確認すること。

### 検証方法
- `AssetDatabase.ImportAsset(ForceUpdate)`でコンパイル確認、`unity_analyze_console_logs`で
  エラー0件（`PlayerTauntController.cs`、`LobbySettingsPanel.cs`、`LobbyMenuController.cs`、
  `InGameOptionsMenu.cs`、`MagicHandSceneBuilder.cs`、`PlayerAnimator.controller`、
  `.inputactions`すべて）
- `.inputactions`: `Lobby`マップの`Confirm`アクションがP1・P2両方のアセットで
  `<Gamepad>/buttonEast`にバインドされていることを実測確認
- `PlayerAnimator.controller`: `UnityEditor.Animations.AnimatorController`経由で
  パラメータ6個（Taunt含む）、状態5個（Taunt含む）、AnyState遷移3本（Taunt含む）、
  Taunt状態のMotionが`emo-toAnimation`であることを実測確認
- Play modeで`PlayerTauntController`がP1・P2双方にAddComponentされ、`animator`・
  `optionsMenu`フィールドが正しく配線されていることを実測確認
- 上記の「重要な発見」の通り、実際のフレーム駆動でのアニメーション遷移そのものは
  このテスト環境からは確認できなかった。手動`Animator.Update()`ステップと
  データ構造の実測で「配線は正しい」ことまでは確認済みだが、実機コントローラーでの
  「本当に画面上でエモートが再生されるか」の最終確認は未実施

---

## 22. エモート中にキャラが動くバグの修正／優位・劣位・互角マークを相手の中心へ・縁取り追加（2026-08-23）

依頼: 「エモート時にキャラクターの位置が移動するバグの修正」「劣位、優位、互角を相手キャラの中心にし、
縁取りして見やすく。それと同じ色で頭の上の手も縁取り。でも地面の手アイテムとは色を変えて」。

**このセッションの冒頭で気づいた重要な事実**: worktree側のHANDOFF.mdが、メインリポジトリ
（`C:\gameB9\jyankenonigokko`）に直接コミットされた**別セッション・別ユーザー（`HYAKKI\百鬼`）による
2コミット**（`78fb402`「アイテムの説明」、`2bd54f5`「エモートをできる機能を実装 優位、劣位、互角表示...」）
に対して478行分古かった。この2コミットで`HandAdvantageIndicator.cs`・`PlayerTauntController.cs`が新設され、
§17・§20・§21が書かれていた。作業開始前にメインリポジトリの`git log`とHANDOFF.mdを直接確認し、
Scripts/Editor/Input/docsをworktreeへ`cp`で同期してから着手した。**このプロジェクトでは、
自分（このセッション）以外の手でもメインリポジトリへ直接コミットされることがあるため、
作業を始める前に必ず`git log`とHANDOFF.mdの行数を突き合わせて確認すること。**

### 22-1. エモート中にキャラが動くバグ

依頼文からは「アニメーションクリップのボーンカーブがおかしいのでは」と推測されたため、まず
`emo-toAnimation.anim`の全カーブバインディング（33個）を実測で洗い出した。しかし:

- ルート直下（パスに"/"を含まない）のバインディングは`m_LocalPosition`ではなく
  `localEulerAnglesRaw`のみ（回転だけで位置カーブなし）
- 唯一位置カーブを持つ`Main_Rig/Spine00`（胸のボーン）も、X/Zの振れ幅は実測で1e-6オーダー
  （浮動小数点誤差レベル）、Yは1.94236で完全に一定
- つまり**クリップのボーンカーブに実質的なズレは無かった**。§21執筆時点の推測
  （「外部素材のクリップにルートボーンの絶対座標がそのまま焼き込まれている」）は誤りだったと判明

実際の原因は単純だった。**`PlayerTauntController`がエモート再生中もプレイヤーの移動を
一切止めていなかった**。トリガーを発火させるだけで、その後の`FixedUpdate`の移動処理
（`PlayerController.CanAct`が`true`である限りRigidbodyへ移動を適用し続ける）は素通しのまま
だったため、エモートの身振りポーズを取りながら移動入力を入れっぱなしにするとキャラが
物理的に滑っていき、「エモート中にキャラの位置が動く」ように見えていた。

#### 対処
- `PlayerController`に`IsTaunting`（bool、`SetTaunting(bool)`で設定）を追加し、
  `CanAct`の判定に含めた（`IsSelecting || IsStunned || IsDefeated || IsTaunting`のいずれかで false）。
  既存のロック状態（選択中・スタン・敗北）と全く同じ扱いにしたことで、`FixedUpdate`の移動適用も
  `cameraRig.SetLookInput`も`flight.SetVerticalInput`も自動的に止まる
- `PlayerTauntController.Update()`の先頭で毎フレーム
  `animator.GetCurrentAnimatorStateInfo(0).IsName("Taunt")`を読み、その結果をそのまま
  `player.SetTaunting()`に渡すだけにした。専用タイマーを別に持たず、Animatorの実際の再生状態と
  直結させることで、Animator側の`exitTime`（0.9）やトランジション時間を変更しても
  自動的に追従する（タイミングの二重管理を避けるため）
- ついでに、§21で「Unity上で手で組んだものではなくYAMLを直接編集して作った」とされていた
  `PlayerAnimator.controller`のTauntブロックを、`CreatePlayerAnimatorController()`の
  コードへ正式に移植した（`Hit`ブロックと全く同じパターン：パラメータ`Taunt`(Trigger)、
  AnyState→Taunt（条件Taunt、duration 0.08）、Taunt→Locomotion_Unarmed（exitTime 0.9、
  duration 0.15）。新規`LoadTauntClip()`ヘルパーが`aoriemo-------------to/emo-toAnimation.anim`を
  読み込む。この節の冒頭の調査（ボーンカーブは正常）を踏まえ、クリップを書き換える処理は
  持たせていない（当初はルート直下の位置カーブを取り除く処理を実装したが、
  実際には対象が0件だったため意味が無く、混乱を避けるため削除した）。
  これで「シーンはコードから生成する」の原則どおり、`PlayerAnimator.controller`が
  手作業のYAML編集に依存しなくなった。ただし**既存の`.controller`アセットがあると
  `CreatePlayerAnimatorController()`は中身を見ずに早期returnする**（`LoadOrCreate`と同じ罠）ため、
  コード変更後は`AssetDatabase.DeleteAsset`でアセットを消してから`BuildScene()`を呼ぶ必要がある

#### 検証方法
- `Animator.SetTrigger("Taunt")`→手動`Animator.Update()`を5回呼んで`Taunt`ステートに
  遷移することを実測確認
- **§21の「重要な発見」（MCP経由の自動フレーム更新にはSetTriggerの効果が伝播しないことがある）を
  踏まえ、`PlayerTauntController.Update()`をリフレクションで直接手動呼び出しすることで、
  「トリガー発火→Animatorの状態遷移→PlayerTauntControllerがそれを読む→SetTaunting呼び出し→
  CanActがfalseになる」の一連の配線を、自動フレーム更新に頼らずエンドツーエンドで実測確認できた**
  （`isTaunting=True canAct=False`）。これは§21で「実機コントローラーでの最終確認ができなかった」
  としていた部分を、手動呼び出しという別の方法で実質的に埋め合わせるやり方
- Animatorをさらに60フレームぶん進めてから同じ手動呼び出しをすると、
  `Taunt`ステートを抜けて`isTaunting=False canAct=True`に戻ることも確認済み

### 22-2. 優位/劣位/互角マークを「相手キャラの中心」へ

§17-4の実装は`go.transform.localPosition = new Vector3(0f, 1.3f, 0.4f)`という**owner基準の
固定ローカル座標**で、ローカルZ+（ownerの前方）へ0.4m浮かせていた。これは
「ownerが視聴者の方を向いている」ときにしか成立しない前提で、**ownerが視聴者に背を向けると
マークが体の裏側へ回り込んで隠れる**という不具合があった。依頼の「相手キャラの中心にし」は
これの指摘だと判断した。

#### 対処
`HandAdvantageIndicator.LateUpdate()`で、位置を**ownerの向きではなくviewerの方向**から
毎フレーム計算する方式に変更した:

```
Vector3 basePos = owner.transform.position + Vector3.up * IndicatorHeight;
Vector3 toViewer = viewer.transform.position - owner.transform.position;
toViewer.y = 0f;
Vector3 direction = toViewer.sqrMagnitude > 0.0001f ? toViewer.normalized : owner.transform.forward;
transform.position = basePos + direction * IndicatorForwardOffset;
```

これで、ownerがどちらを向いていても、マークは常にviewerから見える側（＝体の手前・中心）に出る。
ビルダー側の固定ローカル座標指定は不要になったため`BuildHandAdvantageIndicator`から削除した。

実測: ownerを視聴者の方へ向けた状態・背を向けた状態の両方でマークのワールド座標を読み、
どちらも`(0, 1.3, 0.4)`（＝視聴者側に浮いた位置）で一致することを確認（背を向けたときに
`(0, 1.3, -0.4)`（逆側）になっていないことが修正の実測確認）。

### 22-3. マークの縁取り・頭上の手表示の連動縁取り

TextMeshは`UnityEngine.UI.Outline`が使えない（Canvas配下のGraphicコンポーネント専用のため）。
反転殻方式（§11-6）も、文字メッシュは裏表の意味が地面アイテムの立体モデルと違う（§20-1で
判明した「TextMeshの読める面は-Z側」の制約）ため、そのままは使えない。

- **マーク本体の縁取り**: 同じ`HandAdvantageIndicator`のGameObjectに、ひとまわり大きい
  （`characterSize`を1.35倍）黒い`TextMesh`を子として追加し、ローカルZ+へ0.01だけ
  （ビルボードの奥＝カメラから遠い側）浮かせて重ねた。アンカーが同じ中心なので、
  拡大した分が全方向へ均等にはみ出して縁のように見える。本体側で`state`が変わるたびに
  `outlineTextMesh.text`も同じ文字列へ同期させる（色は常に黒固定）
- **頭上の手表示（`PlayerHandIndicator`）の縁取り**: `BuildHandIndicator`でグー/チョキ/パーの
  3モデルに`addOutline: true`を渡し、地面のアイテムと同じ反転殻の縁取りを追加。
  ただし色は地面用の白マテリアル（`M_HandItemOutline`、共有）をそのまま使わず、
  `HandAdvantageIndicator`側で毎フレーム`MaterialPropertyBlock`によりマークと同じ色
  （優位=緑／劣位=赤／互角=白）に上書きする。マテリアル自体は共有のままなので
  地面側の見た目（常に白）には一切影響しない——「地面の手アイテムとは色を変えて」を、
  新しいマテリアルを増やさずインスタンスごとの上書きだけで満たしている
  （`BlinkTargetIndicator`と同じ、この種の「同じ形・違う色」を扱うときの定石）
- 頭上の手表示は3形状（グー/チョキ/パー）ぶんの縁取りレンダラーをまとめて
  `HandAdvantageIndicator.handOutlineRenderers`へ渡している。同時に見えるのは
  今の手ぶん1つだけなので、3つまとめて色替え・表示切替しても支障はない

#### 検証方法
Playモードで1P=グー・2P=チョキに設定し、1P視点のスクリーンショットで確認：
- 2P（グーに負けるチョキを持つ相手から見て、グーを持つ1P自身の意）の頭上に緑の
  「優位」の文字が、黒い縁取りとともにキャラの胸の高さ・中心に表示されていることを確認
  （1Pのグーが2Pのチョキに勝つので、1P視点では2Pに対して優位＝緑が正しい）
- 頭上の手アイコン（グー/チョキ/パー）にも縁取りが付いており、地面に落ちている同じ形の
  アイテム（白い縁取り）とは別の見た目になっていることをスクリーンショットで確認
- リフレクションで`handOutlineRenderers`の`MaterialPropertyBlock`から`_BaseColor`を読み、
  `RGBA(1, 0.35, 0.35, 1)`（劣位色）になっていることを実測確認。地面側の
  `M_HandItemOutline`マテリアル本体は`RGBA(1,1,1,1)`（白）のまま変化していないことも確認済み

---

## 23. 優位/劣位/互角マークが壁を貫通して見えるバグ・エモート中に動くバグの修正（2026-08-23）

依頼: 「互角、優位、劣位が壁を貫通して見えるのを修正」「エモートをすると位置が移動するのを修正」
（§22-1のエモート修正は不十分で、まだ再現していた）。

### 23-1. マークが壁を貫通して見えるバグ

`TextMesh`の既定マテリアル（`Font.material`、シェーダー名`"GUI/Text Shader"`）は
IMGUI（旧UnityGUI）向けのレガシーシェーダーで、実測したところURPでは通常の深度テストを
行わず、**不透明な壁の向こう側にあっても常に手前に描かれる**ことが分かった
（`mat.HasProperty("_ZTest")`が`false`＝ZTestを露出すらしていない）。§17-4・§20で
このシェーダーのまま使い続けていたのが原因。

#### 対処
新規`CreateTextMeshMaterial`（`MagicHandSceneBuilder.cs`）で、フォントのテクスチャ
（`font.material.mainTexture`、グリフのアルファマスク）をそのまま流用しつつ、
URPの`Universal Render Pipeline/Unlit`シェーダーへ載せ替えたマテリアルを作る。
`_Surface=Transparent`・`_Blend=Alpha`・`_ZWrite=0`（§7-6の`CreateTransparentUnlitMaterial`と
同じ作法）で、**ZTestは既定（LEqual）のまま触らない**ことで通常の深度テストを効かせる。
`TextMesh.color`は頂点カラーに焼き込まれる仕組み（マテリアル本体の色を変えなくても
インスタンスごとに違う色で表示できる）なので、マテリアル自体の色は白のまま共有できる。
本体（`textMesh`）と縁取り（`outlineTextMesh`）の**両方**に同じマテリアルを割り当てる必要がある
——最初本体だけ差し替えて検証したところ改善して見えたが、縁取り側が旧シェーダーのまま
残っていて「本体は直ったが縁取りだけ壁を貫通する」状態になっていた。

#### 検証方法：`unity_capture_game_view`が分割画面の右目/左目のカリングを正しく再現しないことが判明

この節の検証で新しい罠を踏んだ。P1のマークをP2視点でだけ見えるレイヤー
（`P2Only`、§17-4のレイヤー機構）に乗せてある前提で、P1のカメラの`cullingMask`が
`P2Only`を確実に除外していることを数値で確認した（`-513`＝bit9のみ除外）にも関わらず、
`unity_capture_game_view`で撮ったスクリーンショットには**P1側の区画にまでP2Only専用の
マークが写り込んでいた**。原因の切り分けに時間を使ったが、`unity_capture_game_view`は
分割画面の複数カメラ（`Camera.rect`・`cullingMask`）を正しく合成していない、
このツール自体の制約だと判断した（実際のゲームロジック・レイヤー設定は数値上正しい）。

**教訓**: 分割画面やレイヤー制限が絡む検証では`unity_capture_game_view`を信用せず、
**対象のカメラを直接`RenderTexture`へレンダリングして`ReadPixels`で読む**方式
（§7-12・§20で使った一時カメラと同じ考え方だが、今回は「一時カメラを新設」ではなく
**実在するプレイヤーカメラをそのまま流用**）に切り替えること。手順:
```
var cam = player.CameraRig.GetComponent<Camera>();
var rt = new RenderTexture(960, 540, 24);
var prevTarget = cam.targetTexture; var prevRect = cam.rect;
cam.targetTexture = rt; cam.rect = new Rect(0,0,1,1);
cam.Render();
// ReadPixels → EncodeToPNG → File.WriteAllBytes
cam.targetTexture = prevTarget; cam.rect = prevRect; // 必ず元に戻す
```
この方式で、遮蔽物（テスト用の`Sphere`をマークとカメラを結ぶ直線上に配置）を
挟んだときにP2の実カメラから見てマークが**完全に非表示**になること、
遮蔽物を消すと再び表示されることの両方を実測確認した。これが実際のゲームプレイでの
挙動を正しく反映する検証結果である（`unity_capture_game_view`ベースの検証は誤った
「まだ貫通している」という結果を示していたため、本節の最終的な結論はこちらを採用した）。

### 23-2. エモート中に動くバグの再発（§22-1の修正は不十分だった）

§22-1で`IsTaunting`により`CanAct`を止める修正を入れたが、`PlayerController.FixedUpdate`の
コメントにある通り「スタン中・選択中はノックバックの慣性を殺さないため速度に触れない」設計
そのままだと、**`CanAct`が`false`になっても既存の`Rigidbody`速度はそのまま残る**。
スタンはノックバック直後にかかることが多く慣性を残したいが、エモートは自発的な演出で
慣性を残す理由が無い。§22-1では「新しい移動入力を止める」ところまでしか直しておらず、
**発動前から歩いていた勢いが、エモート再生中もそのまま減衰しながら滑り続ける**ことは
直っていなかった。これが「修正したはずなのにまだ動く」の実体。

#### 対処
`PlayerTauntController.Update()`で、`taunting`が`false→true`に変わった瞬間
（エッジ検出、`wasTaunting`フィールドで判定）だけ`player.StopMotion()`
（`Rigidbody.linearVelocity`/`angularVelocity`を`Vector3.zero`にする、既存の公開メソッド。
選択中・敗北時などにも使われている定石）を呼ぶ。毎フレーム呼ぶと重力等も止まってしまうため、
突入した瞬間の1回だけに絞ってある。

#### 検証方法
`Rigidbody.linearVelocity`を`(5,0,0)`に設定 → `wasTaunting`をリフレクションで`false`に
初期化 → `Animator.SetTrigger("Taunt")` → `Animator.Update()`を5回手動で進めて
`Taunt`ステートへ遷移させる → `PlayerTauntController.Update()`をリフレクションで
手動呼び出し、という手順を**すべて1回のツール呼び出し内**で実行し（§21で判明した
「MCP経由だと自動フレーム更新とSetTriggerの効果にタイミングのズレが出る」問題を、
複数回のツール呼び出しに分けないことで回避）、`isTaunt=True`・`isTaunting=True`・
`velocity=(0,0,0)`を同時に確認した。速度が確かにこのタイミングでゼロになることを実測済み。

---

## 24. エモートの発動条件を地上限定に、移動でキャンセル可能に（2026-08-23）

依頼: 「エモートは地面でしか発動できない」「エモートは移動でキャンセルできる」
「エモートを発動した時に表示位置が移動している、これに関して詳しく教えて」の3件。
最後の1件は実装依頼ではなく**説明の依頼**だったため、まず原因を特定してから、
前の2件（対策そのもの）を実装した。

### 24-1. 「エモートで位置が動く」の詳しい原因（§22-1・§23-2の修正だけでは防げなかったパターン）

§22-1で`IsTaunting`により`CanAct`を止め、§23-2で発動の瞬間に`StopMotion()`を呼ぶ
（既存の速度を消す）ところまで直した。ここまでで「歩いていた勢いのまま滑る」パターンは
解決していたが、**もう一つ別の経路が残っていた**。

`PlayerController.FixedUpdate()`の中身:
```csharp
private void FixedUpdate()
{
    UpdatePhysicsMaterial();
    UpdateFlightGravity();
    ApplyExtraGravity();       // ← CanAct を見ずに毎回かかる

    if (CanAct)                // ← 新しい移動入力の適用はここだけ止まる
    {
        ...
    }
}
```
`ApplyExtraGravity()`（既定重力だけだと浮遊感が強いので追加している下向きの力）は
`CanAct`を見ずに**毎FixedUpdate無条件**に呼ばれる。スタン中のノックバックによる落下を
途中で止めないための仕様で、それ自体は正しい。しかし煽りエモートも同じ`CanAct=false`の
経路を通るため、**空中で発動すると、`StopMotion()`で速度をゼロにした直後から
また重力が積み上がり、着地するまで見た目のポーズのまま落下し続ける**。
地上で発動していれば、重力はかかっても床が受け止めるので位置は変わらず、
この経路は問題にならない——依頼にあった「地面限定にしてほしい」は、まさにこの経路を
根元から断つ要望だったと理解している。

つまり「エモートで位置が動く」には2つの原因があった:
1. 発動前の移動慣性が残ったまま滑る → §23-2で対策済み（`StopMotion()`）
2. **空中で発動すると重力で落下し続ける → 今回、地上限定にすることで対策**

### 24-2. 地上でしか発動できないようにする

`PlayerTauntController.Update()`のトリガー発火条件に`player.IsGrounded`を追加するだけ。
`IsGrounded`は`PlayerController`に既存の公開プロパティ（1フレームキャッシュ付きの
接地判定、`ApplyExtraGravity`等が既に使っている）で、新規の判定ロジックは不要だった。

```csharp
if (down && !wasDown && player.IsGrounded) animator.SetTrigger(TauntId);
```

十字キー下を空中で押しても、`wasDown`は更新されるがトリガーは発火しない。
押しっぱなしのまま着地しても、そのタイミングで改めて「新規に押した」エッジには
ならないため発火しない（＝着地後にもう一度押し直す必要がある）。過剰に発火しやすく
なる方向の緩さではないので、誤発動の心配は無いと判断した。

### 24-3. 移動でキャンセルできるようにする

`PlayerTauntController`に、`PlayerController.OnMove`と同じ`"Move"`アクションを
受け取る自前の`OnMove(InputValue value)`を追加した（Send Messagesは同じGameObject上の
全コンポーネントへ届くので、`PlayerController`側の`moveInput`を覗きに行く必要はなく、
並行して自分専用のコピーを受け取るだけで済む）。

`Update()`の先頭、Animatorの状態を読んだ直後に判定を追加:
```csharp
bool taunting = animator.GetCurrentAnimatorStateInfo(0).IsName("Taunt");

if (taunting && moveInput.sqrMagnitude > moveCancelThreshold * moveCancelThreshold)
{
    animator.Play(LocomotionUnarmedState, 0, 0f);
    taunting = false;
}

player.SetTaunting(taunting);
```
`animator.SetTrigger`（次の遷移を予約するだけ）ではなく`animator.Play(..., 0, 0f)`
（即座にそのステートへ強制的に切り替える）を使うことで、`exitTime`の遷移を待たずに
その場でLocomotionへ戻す。`taunting`をこの場で`false`へ書き換えてから
`player.SetTaunting(taunting)`に渡すので、**同じフレーム内で`CanAct`が復帰し、
移動の再開に体感できるラグが出ない**。`moveCancelThreshold`（既定0.2、十字キー下の
`DownThreshold`0.5より緩め）は「動かそうとした」判定の遊び。

### 検証方法
- 地上限定: コードレビューベース（`IsGrounded`は既存の実績あるプロパティで、
  追加した条件も単純な論理積のため）。空中に確実に留まらせた状態を安定して
  作るのがMCP経由のPlayモードでは難しく（テレポート直後は同フレーム内キャッシュにより
  `IsGrounded`が古い値のまま返ることがあり、かつ2階の床など足場が多いマップのため
  「本当に何も無い空中」を狙って作るのに手間取った）、実機での軽い確認を推奨
- 移動キャンセル: `Animator.SetTrigger("Taunt")`→手動`Update()`で`Taunt`ステートへ
  遷移させた後、`moveInput`をリフレクションで`(1,0)`に設定してから
  `PlayerTauntController.Update()`を1回呼び出し、**その1回の呼び出し内で**
  `Taunt`ステートを離れて`isTauntState=False`・`isTaunting=False`・`canAct=True`に
  戻ることを実測確認済み

---

## 25. 優位/劣位/互角マークの完全削除、エモートの「後方スライドしてから再生」を修正（2026-08-23）

依頼:「劣位、優位、互角を削除して」「エモートを使用すると後方にスライド移動してから
エモートする問題を解決して」の2件。

### 25-1. 優位/劣位/互角マークの削除

§17-4／§20／§22-1／§23-1で作り込んできた、相手の手を見比べて表示する
優位（緑）・劣位（赤）・互角（白）のTextMeshマーク一式を、ユーザーの明示的な指示により
丸ごと撤去した。ゲームの仕様として不要と判断されたための削除であり、不具合修正ではない。

- `Assets/_Game/Scripts/Player/HandAdvantageIndicator.cs`を`.meta`ごと削除
- `MagicHandSceneBuilder.cs`から以下を削除:
  - `BuildHandAdvantageIndicator(...)`（TextMesh＋縁取りTextMesh＋`HandAdvantageIndicator`の配線、約75行）
  - `WireHandAdvantageIndicators(PlayerController, PlayerController)`（`BuildScene()`から
    2回の`BuildPlayer`呼び出しの間で呼ばれていた配線口）とその呼び出し
  - `CreateTextMeshMaterial(string, Font)`（§23-1でZTest貫通対策として追加したURP Unlitマテリアル
    生成ヘルパー。呼び出し元が`BuildHandAdvantageIndicator`だけだったため道連れで削除）
  - `FindOutlineRenderers(GameObject)`（§22-1で縁取り色を優位/劣位で変えるために追加したヘルパー）
  - `BuildHandIndicator(...)`から`addOutline`まわりの引数・戻り値（`out Renderer[] outlineRenderers`）を除去し、
    元のシンプルな「グー/チョキ/パーの3体を用意して`PlayerHandIndicator`に渡すだけ」の形へ戻した
- 検証: `Grep`で`HandAdvantageIndicator|CreateTextMeshMaterial|FindOutlineRenderers`の
  参照が0件になったことを確認。コンパイルはエラー0で通過。`BuildScene()`実行後、
  Playモードで`FindObjectsByType<GameObject>()`を名前フィルタして
  「HandAdvantageIndicator」という名前のオブジェクトが0件であることを実測確認済み

### 25-2. エモートが「後方にスライドしてから再生される」問題

§24-2／§24-3までの対策（地上限定発動・移動でキャンセル）はいずれも
`animator.GetCurrentAnimatorStateInfo(0).IsName("Taunt")`が`true`になったことを見て
初めて`CanAct`を止める作りだった。これでは埋められない、**もう一つ別の隙間**が原因だった。

`AnyState → Taunt`の遷移には`hasExitTime=false, duration=0.08f`のブレンド時間がある。
このブレンド中、`Animator.GetCurrentAnimatorStateInfo(0)`は**遷移元（Locomotion）を
返し続ける**——Unityの仕様上、クロスフェード完了までは新ステートに切り替わったと
報告されない。つまり十字キー下を押した瞬間から約0.08秒間、`IsName("Taunt")`は
`false`のままで、`CanAct`は`true`のまま、直前の移動入力（例えば後ろへ下がりながら
押した場合はその後退速度）がそのままFixedUpdateに数フレーム通り続けてしまう。
これが「エモートを使うと後方にスライド移動してからエモートが再生される」の正体だった。

対策として`PlayerTauntController`に`committedToTaunt`フラグを導入し、
`animator.SetTrigger(TauntId)`を撃った**その場・同じUpdate呼び出し内**で`true`にする。
`CanAct`の凍結・`StopMotion()`はこのフラグ基準に切り替え、Animator側の遷移完了を
待たなくなった:

```csharp
if (down && !wasDown && player.IsGrounded && !committedToTaunt)
{
    animator.SetTrigger(TauntId);
    committedToTaunt = true;      // ← 遷移の完了を待たず、撃った瞬間に立てる
    hasEnteredTauntState = false;
}
...
player.SetTaunting(committedToTaunt);   // ← IsName("Taunt")ではなくこちらを見る
if (committedToTaunt && !wasTaunting) player.StopMotion();
```

`hasEnteredTauntState`は「まだ遷移待ち（committedToTaunt=true, inTauntState=false,
未突入）」と「再生し終えて自然にLocomotionへ戻った（committedToTaunt=true,
hasEnteredTauntState=true, inTauntStateが再びfalse）」を区別するために追加した。
一度でも`inTauntState`が`true`になったことを見た後でなければ、`inTauntState=false`を
「自然終了」と誤判定して`committedToTaunt`を早期に落としてしまうため。

§24-3の移動キャンセルも判定基準を`committedToTaunt`に差し替えた（`animator.Play`で
即座にLocomotionへ切り替える処理自体は変更なし）。

### 検証方法（Playモード、リフレクションで内部状態を操作）
3パターンをすべて実測確認:
1. **トリガー直後の即時凍結**: 接地状態のP2に後方速度`(0,0,-5)`を与えた状態で
   十字キー下の押下エッジをリフレクションで発生させ`Update()`を1回呼ぶ→
   `isTauntState=True isTaunting=True canAct=False velocity=(0,0,0)`。
   凍結が`inTauntState`待ちではなく`committedToTaunt`で即座に効くことを確認
2. **自然終了**: `animator.Update(0.05f)`を60回（クリップ長約2.27秒＋exitTime分の
   再生をシミュレート）進めた後`Update()`を1回→
   `isTauntState=False isTaunting=False canAct=True`。`hasEnteredTauntState`による
   自然終了の検出が正しく働くことを確認
3. **移動キャンセル**: 再度トリガーして`Taunt`へ突入（`isTauntState=True`）させた後、
   `moveInput`を`(1,0)`にして`Update()`を1回→
   `isTauntState=False isTaunting=False canAct=True`。新しい状態機構でも
   移動キャンセル経路が壊れていないことを確認

いずれもコンパイルエラー0、`unity_analyze_console_logs`でエラー0件を確認済み。

---

## 26. 優位/劣位/互角マークを復活、壁貫通の再検証（2026-08-23）

依頼:「優位/劣位/互角マークを復活。文字と色でわかりやすく、でも構造物を貫通して見えないように」。
§25で削除したばかりの機能を復元してほしいという依頼だった。あわせて「エモートを移動で解除に変更」
とも言われたが、確認したところ現状の実装（地上限定発動＋移動で即キャンセル）のままでよいとの
回答だったため、エモート側は変更していない。

### 26-1. 復元した内容

§25で消した`HandAdvantageIndicator.cs`と、`MagicHandSceneBuilder.cs`側の配線
（`BuildHandIndicator`のoutline対応、`BuildHandAdvantageIndicator`、
`WireHandAdvantageIndicators`、`FindOutlineRenderers`、`CreateTextMeshMaterial`、
各呼び出し箇所）を、削除前のコードそのまま復元した。ロジック自体（優位=緑/劣位=赤/互角=白の
文字表示、視聴者側にビルボードで正対、頭上の手表示の縁取りも同じ色に染める）に変更はない。

### 26-2. 「本当に壁を貫通しないか」を実写で検証し直した理由

§23（f69c588）で一度「壁貫通」を直したはずが、その後§25で機能ごと削除されるという経緯があった。
削除の理由が「直っていなかったから」なのか「単に不要と判断されたから」なのかは会話からは
判別できなかったため、復元にあたって**信じずに実機で検証し直す**方針にした。

検証は、既存のステージ上のオブジェクトだと構造物の高さや当たり判定の見極めに時間がかかったため、
Playモード中に`GameObject.CreatePrimitive(PrimitiveType.Cube)`で不透明な板を即席で生成し、
カメラとマークの間に置く方式にした。同じアングルで「板を置く前」「板を置いた後」の2枚を
`Camera.Render()`で実際に描き出して比較する、というシンプルな実験。

つまずいた点（テスト手順そのものの罠、機能側のバグではない）:
- `GameState`を`Title`から`InGame`へ直接`ChangeState`で飛ばしてPlayモード内でテスト環境を
  即席で作ったところ、`transform.position`で直接テレポートしても数フレーム後に元の座標
  （具体的には(12,0,0)）へ戻ってしまう現象に遭遇した。原因は`Rigidbody`。`transform.position`だけ
  書き換えても`Rigidbody`側の内部位置は変わらないため、次の`FixedUpdate`で物理側が
  元の位置に基づいて`transform`を上書きしていた。`Rigidbody.position`も同時に書き換える
  （かつ`linearVelocity`もゼロにする）ことで解決した。§24-2のクランプ処理
  （`ClampToFlightCeiling`）が`body.position`と`transform.position`を両方書き換えているのと
  同じ理由で、以後のPlayモード内テレポートは必ず両方セットする
- `GameManager`の試合タイマーが動いたままだと、検証中に試合が終了してロビーへ戻る等の
  ステート遷移が起き、そのたびに配置がリセットされる。`GameManager.enabled = false`で
  `Update`自体を止めてから検証した

### 26-3. 検証結果

不透明な板を挟まない状態（コントロール）では、マークの文字（縁取り付きの「劣位」など）が
キャラクターの体越しではなく実際に画面に描かれることを確認。次に同じカメラ位置・同じ向きのまま
マークとカメラの間に不透明な板（`PrimitiveType.Cube`、既定のOpaqueマテリアル）を置いて再描画した
ところ、**文字・縁取りともに完全に見えなくなった**（板の面だけが映り、貫通は無し）。

`CreateTextMeshMaterial`は`_ZTest`をマテリアルのプロパティとして持たない
（`Assets/_Game/Shaders/XRayMarker.shader`のコメントに記載の通り、URPのUnlitシェーダーは
`_ZTest`を公開していない）ため明示的な上書きはできないが、シェーダー自体の既定の深度テスト
（通常のLEqual相当）で十分に機能することが今回の実写検証で確認できた。壁を貫通させたい表示
（`RevealMarker`等）には別途`CreateXRayMaterial`（自作の`XRayMarker`シェーダー、ZTest Always）を
使っており、今回のマークとは意図的に逆の設定になっている。

### 検証方法まとめ
- Playモードで`GameState`を強制的に`InGame`へ、`GameManager.enabled=false`でタイマー等を停止
- 2人のプレイヤーを実際にグー/チョキで手を確定させ（`SetHand`）、開けた場所へ配置
- `HandAdvantageIndicator.LateUpdate()`をリフレクションで直接呼び、位置・文字・色を再計算させる
- プレイヤーのカメラ（`Camera_P2`）を手動で狙いを付けた位置・向きに動かし、`RenderTexture`へ
  `Camera.Render()`→`ReadPixels`→PNG保存という、本セッションで確立済みの実写検証手法をそのまま使用
- 「板なし」「板あり」の2枚を比較し、板ありで完全に非表示になることを目視確認

---

## 27. マーク位置を手アイテムと本体の間に、互角色をグレーに、カウントダウンSEを控えめに（2026-08-23）

依頼:「優位/劣位/互角の文字を手のアイテムと人の間に移動」「色が緑/赤/グレーにして」
「3,2,1のSEを0.01に変更」の3件。

### 27-1. マークの高さを変更

`HandAdvantageIndicator.IndicatorHeight`を1.3→2.0に変更。頭上の手表示（`HandIndicator`、
`playerRoot`からy=2.3）とキャラ本体（身長約1.92m）の間に収まる高さにした。
1.3は§22-1で「帽子のつばに頭が突き刺さる」不具合を避けるために選んだ値で、
胸〜頭部のあたりに出ていた。今回はそれより高い位置が指定されたため、
本体の頭上・手アイテムの下という新しい基準で選び直した。

### 27-2. 互角の色をグレーに

`EvenColor`を`(0.95, 0.95, 0.95)`（ほぼ白）から`(0.6, 0.6, 0.6)`（はっきりしたグレー）に変更。
優位（緑）・劣位（赤）はそのまま。この色は頭上の手表示の縁取り
（`ApplyHandOutlineColor`）にも連動しているため、変更は両方に反映される。

### 27-3. カウントダウンSEの音量を専用に下げる

既存の`SEPlayer`は全SE共通の`volume`（0.15）を`PlayOneShot`に渡す一枚岩の設計で、
「3-2-1のカウントダウン音だけ下げたい」という依頼には対応できなかった
（`volume`自体を下げると他の10種のSEも巻き添えで下がってしまう）。

`countdownVolume`（既定0.01、`BgmVolume`と同じ値）を専用に追加し、`PlayCountdown()`だけが
これを使うようにした。`Play(AudioClip)`はそのまま共通`volume`を使う経路として残し、
新しく`Play(AudioClip, float)`のオーバーロードを追加して`PlayCountdown()`から明示的に
音量を渡す形にした。ビルダー側（`BuildSePlayer`）にも`SeCountdownVolume`定数(0.01)を足し、
`SetFloat(player, "countdownVolume", SeCountdownVolume)`で配線した。

### 検証方法
- Playモードで`GameState`を`InGame`へ強制し`GameManager.enabled=false`でタイマー停止、
  両プレイヤーに同じ手（グー/グー）を`SetHand`で設定して互角状態を作り、
  `HandAdvantageIndicator.LateUpdate()`をリフレクションで直接呼んで再計算させた
- 実測: `indicator.transform.position.y = 2.05`（本体頭上・手アイテム下の高さに収まっている）、
  `TextMesh.color = RGBA(0.600, 0.600, 0.600, 1.000)`、`text = "互角"`を確認。
  `Camera.Render()`で実際に描き出し、キャラの頭上・手アイテムの下に文字が表示される見た目も確認
- `SEPlayer`インスタンスの`countdownVolume`フィールドをリフレクションで読み取り、`0.01`であることを確認

---

## 28. 優位/劣位マークが色分けされず黒文字で出ていた不具合を修正（2026-08-23）

§27の直後、ユーザーから2画面のスクリーンショットが送られてきた。「劣位」「優位」どちらも
文字が**黒**で表示されており、依頼していた「緑/赤/グレー」の色分けが全く反映されていなかった。

### 28-1. 原因

`HandAdvantageIndicator.LateUpdate()`は`TextMesh.color`（＝メッシュの頂点カラー）に
優位=緑・劣位=赤・互角=グレーを書き込んでいる。ここまでは§17〜§27を通じて変わっていない。

問題は§23で「壁を貫通して見える」不具合を直すために、マテリアルを既定の
`Font.material`（レガシーな"GUI/Text Shader"）から`Universal Render Pipeline/Unlit`へ
差し替えたこと。**標準のURP Unlitシェーダーは頂点カラーを一切参照しない**——
テクスチャの色（フォントのグリフテクスチャ、RGBは黒）を`_BaseColor`（白固定にしていた）で
乗算するだけで、`TextMesh.color`の値はどこにも使われていなかった。つまり§23の時点で
「壁貫通は直ったが、色分けが効かなくなる」という新しい不具合を埋め込んでいたことになる
（当時は文字の色を意識した検証をしていなかったため気づかなかった）。

旧来の"GUI/Text Shader"は`primary（頂点カラー） × texture.a（アルファのみ）`で合成しており、
テクスチャのRGB自体は使っていなかった。だからこそ`TextMesh.color`をそのまま出力に反映できていた。

### 28-2. 対策：頂点カラーを使う自作シェーダー

`Assets/_Game/Shaders/WorldTextVertexColor.shader`を新設。§7-10で壁貫通表示用に作った
`XRayMarker.shader`と同じ最小構成の考え方で、今回は**ZTestは通常のLEqualのまま**、
フラグメントシェーダーで「フォントテクスチャのアルファ×頂点カラー」を出力するようにした
（旧"GUI/Text Shader"と同じ合成方法を、URP・SRPで動く形で再現）。

```hlsl
half4 frag(Varyings IN) : SV_Target
{
    half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a;
    return half4(IN.color.rgb, IN.color.a * alpha);
}
```

`CreateTextMeshMaterial`をこの新シェーダーを使うように変更し、`_BaseColor`等URP Unlit固有の
プロパティ設定は不要になったため削除した。**既存のマテリアルアセット
（`M_HandAdvantageText.mat`）が旧シェーダーを参照したままだと直らない**ため、
マテリアルが既に存在する場合でも`material.shader`を毎回明示的に上書きするようにした
（§26で「既存アセットが古い設定のまま残る」罠を踏んだばかりだったので、同じ罠を
繰り返さないよう今回は最初から対策を入れてある）。

### 検証方法
- Playモードで`P1=グー・P2=チョキ`（劣位/赤を想定）、`P1=グー・P2=パー`（優位/緑を想定）の
  2パターンをそれぞれ`SetHand`で作り、`HandAdvantageIndicator.LateUpdate()`を
  リフレクションで呼んだ直後に`TextMesh.color`を確認
  - 劣位: `RGBA(1.000, 0.349, 0.349, 1.000)`（赤）
  - 優位: `RGBA(0.349, 1.000, 0.400, 1.000)`（緑）
- `Camera.Render()`で実際に描き出し、どちらも文字と縁取りが黒ではなく指定した色
  （赤／緑）で表示されることを画像で確認済み
- コンパイルエラー0、シェーダーのコンパイルエラー0、`unity_analyze_console_logs`でエラー0件を確認
- 検証中、別セッションが試合中だったらしくPlayモードに既に入っていた（`ItemSpawnManager`の
  無関係な`KeyNotFoundException`が10件残っていた）。自分の検証のためにPlayモードを
  一度抜けて入り直した。このエラー自体は本題と無関係な既存不具合として別タスクで報告済み
- いずれもコンパイルエラー0、`unity_analyze_console_logs`でエラー0件を確認済み

---

## 29. マークが頭上の手表示と重なって見える不具合を修正（2026-08-23）

§28の直後、ユーザーから「黒文字と重なっている黒文字は消して」という報告があった。
スクリーンショットでは「互角」の文字のすぐ後ろに、文字とは別の黒い矩形が重なって見えていた。

### 29-1. 原因の特定

まず§28で追加した縁取り（Outline）テキストが不透明な矩形として描画されているのではと疑い、
`outlineMeshRenderer.enabled = false`にして実写比較したが、黒い矩形は消えなかった
（縁取り自体は正常に機能していた）。

次に、黒い矩形の正体が実は**頭上の手表示（`PlayerHandIndicator`、グー/チョキ/パーの
専用モデル）そのもの**であることが、`Renderer.bounds`の実測で判明した。

```
頭上の手表示のバウンズ: 中心y=2.15、縦の半径0.48 → 実際にはy=1.67〜2.63まで占有
キャラの頭頂: 実測でy≈1.95
```

§27で「手のアイテムと人の間に表示してほしい」という依頼を受け、マークの高さを
1.3→2.0（頭上の手表示y=2.3のすぐ下）へ上げていた。しかし頭上の手表示は
§7-12以降の「もっと目立たせたい」という経緯で3倍サイズ（`HandIndicatorTargetSize=1.05`）
まで拡大されており、実測すると下端が**キャラの頭頂よりも低い位置（y=1.67）まで
垂れ下がっている**。つまり「手のアイテムの下端」と「人の頭」の間には元々すき間が無く、
文字通り両者に挟んで重ならせない配置は物理的に成立しなかった。y=2.0はこの頭上の手表示の
バウンズの内側（1.67〜2.63の範囲内）に収まってしまっており、必ず重なる配置だった。

### 29-2. 対策

1.4・1.45も試したが、帽子のつば（§22-1で1.5〜1.6付近と実測済み。つばは横に大きく
張り出す形状）に角度によっては隠れてしまい、今度は「読めなくなる」別の不具合を招く
おそれがあった。最終的に、頭上の手表示にも帽子のつばにも干渉しないことが
§17〜§26の長期間の実運用で確認済みだった**元の値（1.3、胸の高さ付近）に戻した**。

「間に表示する」という要望と、頭上の手表示が実際には非常に大きく頭に近い位置まで
垂れ下がっているという実装上の制約は両立しない。今回は「重ならないこと」を優先し、
高さは§26以前の値へ差し戻した。

### 検証方法
- Playモードで両プレイヤーに同じ手を設定して互角状態を作り、`HandAdvantageIndicator`の
  `MeshRenderer.bounds`と`PlayerHandIndicator`側の`guVisual`の`Renderer.bounds`を
  それぞれ実測し、両者のY範囲が重ならないことを数値で確認
  （手アイテム: y=1.67〜2.63、マーク: y=1.14〜1.46）
- `Camera.Render()`で実際に描き出し、「互角」の文字と頭上の黒い手アイコンが
  画面上ではっきり分離して見えることを確認済み
- コンパイルエラー0、`unity_analyze_console_logs`でエラー0件を確認済み
