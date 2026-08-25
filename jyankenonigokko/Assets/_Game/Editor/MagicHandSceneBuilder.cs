using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MagicHand.EditorTools
{
    /// <summary>
    /// プレースホルダー構成のプレイ可能シーンを一括生成するエディタ拡張。
    /// メニュー: MagicHand / Build Playable Scene
    ///
    /// 何度でも再実行でき、そのたびに MainScene を作り直す。
    /// 見た目や配置を手で調整したあとに再実行すると上書きされる点に注意。
    /// </summary>
    public static class MagicHandSceneBuilder
    {
        private const string GameRoot = "Assets/_Game";
        private const string ScenePath = GameRoot + "/Scenes/MainScene.unity";
        private const string PrefabPath = GameRoot + "/Prefabs/ItemPickup.prefab";
        private const string InputActionsPath = GameRoot + "/Input/MagicHandControls.inputactions";

        private const float ArenaHalfSize = 30f;

        /// <summary>2階の床の中心Y。ジャンプ（最高到達約3.4）では届かない高さにして、スロープ必須にする。</summary>
        private const float SecondFloorHeight = 5f;
        private const float SlabThickness = 0.6f;

        /// <summary>2階の床の上面。アイテムのスポーン地点などを載せる基準。</summary>
        private const float SecondFloorTop = SecondFloorHeight + SlabThickness / 2f;

        /// <summary>部分的に設ける3階（対角2箇所の見晴らし台）。2階からスロープで上がる。</summary>
        private const float ThirdFloorHeight = 10f;
        private const float ThirdFloorTop = ThirdFloorHeight + SlabThickness / 2f;

        /// <summary>準備ルームはアリーナと同じシーンの、干渉しない離れた場所に置く。</summary>
        private static readonly Vector3 LobbyOrigin = new Vector3(0f, -100f, 0f);

        /// <summary>
        /// アリーナ1つぶんの生成設定。ノーマルとイージーの2つを、同じ生成コードへ
        /// 別の設定で通すことで作る（コピペで2本持つと片方だけ直し忘れる事故が起きるため）。
        ///
        /// Scale は1F・壁・障害物など全体の水平（X/Z）縮尺。高さ（Y）はどちらも変えない
        /// （ジャンプ到達高さなど、既にチューニング済みの縦方向の値に触れないため）。
        /// UpperScale は2階（回廊・ハブ・橋・スロープ）だけにかかる縮尺で、
        /// 「マップ全体を1割小さく、2階はさらに縮小」を Scale と別の値にすることで表す。
        /// Offset はワールド上の設置場所。ノーマルと同じ原点に置くと重なってしまうため、
        /// イージーは準備ルームと同じ要領で遠く離れた場所へずらす。
        /// </summary>
        private readonly struct ArenaConfig
        {
            public readonly float Scale;
            public readonly float UpperScale;
            public readonly Vector3 Offset;
            public readonly bool IncludeThirdFloor;

            public ArenaConfig(float scale, float upperScale, Vector3 offset, bool includeThirdFloor)
            {
                Scale = scale;
                UpperScale = upperScale;
                Offset = offset;
                IncludeThirdFloor = includeThirdFloor;
            }

            public static readonly ArenaConfig Normal = new ArenaConfig(1f, 1f, Vector3.zero, true);

            // 全体を1割小さく(0.9)、2階はそこからさらに縮める(0.9*0.82)。3階は無し。
            public static readonly ArenaConfig Easy = new ArenaConfig(0.9f, 0.9f * 0.82f, new Vector3(0f, 0f, 600f), false);
        }

        /// <summary>
        /// 俯瞰カメラの俯角。
        /// 68度は真上に近く、キャラも段差もほぼ天面しか見えず立体感が出なかったので、
        /// 斜め見下ろしまで倒してある。世界のZ+を画面奥に向ける向きは変えていないので、
        /// 移動の基準方位（lobbyBasisYaw = 0）はそのままでよい。
        /// </summary>
        private const float LobbyCameraPitch = 45f;

        /// <summary>俯瞰カメラから注視点までの距離。</summary>
        private const float LobbyCameraDistance = 24f;

        /// <summary>
        /// 俯瞰カメラが見る先。
        ///
        /// この3つ（俯角・距離・注視点）は、次の2つを同時に満たすように選んである。
        /// どれか1つを動かすと片方が崩れるので、変えるときは両方を確かめること。
        ///
        /// ・手前: 湧き位置(z=-12)と開始の円(z=-10)が写る。画面下端が地面に当たるのは z=-14.4 で、
        ///   床の縁(-14)のすぐ外。これ以上引くと床の外の空が大きく写る
        /// ・奥: スロープの上の台（一番奥・一番高い点が x=-9, y=5.30, z=14）が写る。
        ///   台は俯角18.9度の方向にあり、画面上端の15度に対して3.9度の余裕がある
        /// </summary>
        private static readonly Vector3 LobbyCameraFocus = new Vector3(0f, 0f, -2f);

        // ---- Modular Arena（外部アセット）---------------------------------

        private const string ArenaRoot = "Assets/LoafbrrAssets/ModularArena";
        private const string ArenaMaterials = ArenaRoot + "/Materials";
        private const string ArenaWalls = ArenaRoot + "/Prefabs/wall";

        /// <summary>
        /// 客席の壁は 3m 幅・3m 高。円周をこの幅で割って並べる。
        /// アリーナ本体（60×60）の対角は約42.4なので、外周はそれより外側から始める。
        /// </summary>
        private const float ColosseumModuleWidth = 3f;
        private const float ColosseumModuleHeight = 3f;
        private const float ColosseumInnerRadius = 46f;

        /// <summary>客席はステージの壁(高さ15)の上から立ち上げる。下段は壁に隠れて見えないため。</summary>
        private const float ColosseumBaseHeight = 15f;

        /// <summary>
        /// 準備ルームの広さ（中心からの距離）。
        /// 練習用スロープは本編と同じ勾配22度を保つため走りが13必要で、これがほぼ下限。
        /// </summary>
        private const float LobbyHalfSize = 14f;

        /// <summary>
        /// 壁の高さ。高い練習台(10.30)からジャンプしても越えられない値にする必要がある。
        /// 到達highは 10.30 + 約3.4 = 13.7 なので、余裕を見て16。
        /// </summary>
        private const float LobbyWallHeight = 16f;

        [MenuItem("MagicHand/Build Playable Scene")]
        public static void BuildScene()
        {
            EnsureFolders();

            // ステージの素材は Modular Arena の石材から取る（複製して自分の管理下に置く）
            Material groundMat = CloneArenaMaterial("Ground_Soil_Mat", "M_ArenaSand");
            Material obstacleMat = CloneArenaMaterial("Floor_Brick_Stone_B_Mat", "M_ArenaObstacle");
            Material pillarMat = CloneArenaMaterial("Wall_Concrete_Mat", "M_ArenaPillar");
            Material wallMat = CloneArenaMaterial("Wall_Brick_Stone_Mat", "M_ArenaWall");
            Material platformMat = CloneArenaMaterial("Floor_Brick_Stone", "M_ArenaPlatform");
            Material placeholderMat = CreateMaterial("M_Placeholder", Color.white);
            Material zoneMat = CreateUnlitMaterial("M_StartZone", new Color(0.3f, 1f, 0.55f));
            Material rangeRingMat = CreateUnlitMaterial("M_RangeRing", Color.white);
            Material castEffectMat = CreateUnlitMaterial("M_CastEffect", Color.white);
            Material statusAuraMat = CreateUnlitMaterial("M_StatusAura", Color.white);
            Material speedArrowMat = CreateUnlitMaterial("M_SpeedArrow", Color.white);
            Material stunBoltMat = CreateUnlitMaterial("M_StunBolt", Color.white);
            Material searchWaveMat = CreateUnlitMaterial("M_SearchWave", Color.white);
            Material enemyMarkerMat = CreateXRayMaterial("M_MarkerEnemy", new Color(1f, 0.28f, 0.28f, 0.95f));
            Material broomHandleMat = CreateMaterial("M_BroomHandle", new Color(0.45f, 0.30f, 0.16f));
            Material broomBristleMat = CreateMaterial("M_BroomBristle", new Color(0.78f, 0.62f, 0.30f));

            RevealMarker enemyMarker = CreateRevealMarkerPrefab("RevealMarker_Enemy", enemyMarkerMat);
            CastEffect castEffectPrefab = CreateCastEffectPrefab("CastEffect", castEffectMat);
            SearchWaveEffect searchWavePrefab = CreateSearchWaveEffectPrefab("SearchWaveEffect", searchWaveMat);

            List<ItemDefinitionSO> handItems = CreateHandItems();
            List<ItemDefinitionSO> scrolls = CreateScrolls(enemyMarker, searchWavePrefab);
            List<ItemDefinitionSO> brooms = CreateBrooms();
            RandomScrollSO randomScroll = CreateRandomScroll(scrolls);
            RandomScrollSO randomScrollEasy = CreateRandomScrollEasy(scrolls);

            // 準備ルームの見本と設定一覧では、ほうきもスクロールと同じ扱いで並べる
            var lobbyItems = new List<ItemDefinitionSO>(scrolls);
            lobbyItems.AddRange(brooms);
            InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);

            if (actions == null)
            {
                Debug.LogError($"[MagicHand] Input Actions が見つかりません: {InputActionsPath}");
                return;
            }

            // NewScene は保存を促さずに現在のシーンを破棄するため、未保存の変更があれば中断する
            if (SceneManager.GetActiveScene().isDirty)
            {
                Debug.LogError("[MagicHand] 開いているシーンに未保存の変更があります。保存してから再実行してください。");
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // NewScene() をまたぐと、この直前に SaveAsPrefabAsset で作った castEffectPrefab への
            // 参照だけが（同じ手順で作った enemyMarker は無事なのに）壊れて見えなくなることを実測で確認済み。
            // 原因の特定より確実さを優先し、ここでパスから読み直して確実な参照に差し替える
            castEffectPrefab = AssetDatabase.LoadAssetAtPath<CastEffect>($"{GameRoot}/Prefabs/CastEffect.prefab");
            if (castEffectPrefab == null) Debug.LogError("[MagicHand] CastEffectプレハブの再読み込みに失敗しました");

            ItemPickup itemPrefab = CreateItemPrefab(placeholderMat, broomHandleMat, broomBristleMat);

            BuildLighting(scene);

            // ノーマルアリーナ（原点、等倍、3階あり）
            List<Bounds> blockers = BuildEnvironment(scene, "Stage", ArenaConfig.Normal,
                                                      groundMat, wallMat, obstacleMat, pillarMat, platformMat);
            BuildColosseum(scene, groundMat);
            RespawnManager respawns = BuildRespawnPoints(scene, blockers, ArenaConfig.Normal);
            ItemSpawnManager spawner = BuildItemSpawners(scene, "ItemSpawnManager", itemPrefab, handItems,
                                                         randomScroll, brooms, blockers, ArenaConfig.Normal,
                                                         handSpawnPoints: 25, handTarget: 20,
                                                         itemSpawnPoints: 50, itemTarget: 20);

            // イージーアリーナ（遠く離れた別地点、1割小さく・3階無し・2階はさらに縮小）
            List<Bounds> blockersEasy = BuildEnvironment(scene, "Stage_Easy", ArenaConfig.Easy,
                                                          groundMat, wallMat, obstacleMat, pillarMat, platformMat);
            RespawnManager respawnsEasy = BuildRespawnPoints(scene, blockersEasy, ArenaConfig.Easy);
            ItemSpawnManager spawnerEasy = BuildItemSpawners(scene, "ItemSpawnManager_Easy", itemPrefab, handItems,
                                                              randomScrollEasy, brooms, blockersEasy, ArenaConfig.Easy,
                                                              handSpawnPoints: 20, handTarget: 15,
                                                              itemSpawnPoints: 30, itemTarget: 15);

            AnimatorController playerAnimator = CreatePlayerAnimatorController();
            Material[] handMaterials = CreateHandMaterials();
            const string mageA = CharacterRoot + "/URP/Prefabs/Characters/Mages/Mage_01.prefab";
            const string mageB = CharacterRoot + "/URP/Prefabs/Characters/Mages/Mage_02.prefab";

            PlayerController player1 = BuildPlayer(scene, 0, "1P", placeholderMat, rangeRingMat,
                                                   new Rect(0f, 0f, 0.5f, 1f), true, mageA, playerAnimator, handMaterials,
                                                   enemyMarker, broomHandleMat, broomBristleMat, castEffectPrefab, statusAuraMat,
                                                   speedArrowMat, stunBoltMat, scrolls);
            PlayerController player2 = BuildPlayer(scene, 1, "2P", placeholderMat, rangeRingMat,
                                                   new Rect(0.5f, 0f, 0.5f, 1f), false, mageB, playerAnimator, handMaterials,
                                                   enemyMarker, broomHandleMat, broomBristleMat, castEffectPrefab, statusAuraMat,
                                                   speedArrowMat, stunBoltMat, scrolls);

            // 優位/劣位マークは「相手の手」を見比べる必要があるが、
            // 上の2回の BuildPlayer 呼び出し時点ではまだ相手が存在しない。両者が揃ってから配線する
            WireHandAdvantageIndicators(player1, player2);

            // 設定パネルの行数は「実際にON/OFFできる種類の数」に合わせる。
            // 手変更（グー/チョキ/パー）は基本アイテムなので設定には出さない（ItemSpawnGroup.includeInSettings）
            LobbyRefs lobby = BuildLobby(scene, groundMat, wallMat, platformMat, zoneMat, itemPrefab, lobbyItems,
                                        lobbyItems.Count);

            // 各プレイヤーに、自分の側の設定パネルを操作させる
            SetObject(player1.gameObject.AddComponent<LobbyMenuController>(), "panel", lobby.Panels[0]);
            SetObject(player2.gameObject.AddComponent<LobbyMenuController>(), "panel", lobby.Panels[1]);

            GameObject managerGo = NewGameObject(scene, "GameManager");
            GameManager manager = managerGo.AddComponent<GameManager>();
            MatchSettings settings = managerGo.AddComponent<MatchSettings>();

            SetObject(manager, "respawnManager", respawns);
            SetObject(manager, "itemSpawnManager", spawner);
            SetObject(manager, "respawnManagerEasy", respawnsEasy);
            SetObject(manager, "itemSpawnManagerEasy", spawnerEasy);
            SetObject(manager, "matchSettings", settings);
            SetList(manager, "players", new Object[] { player1, player2 });
            SetFloat(manager, "timer.duration", 300f);
            SetFloat(settings, "matchDuration", 300f);

            SetObject(manager, "lobbyStartZone", lobby.StartZone);
            SetObject(manager, "lobbyCamera", lobby.Camera);
            SetList(manager, "lobbySpawnPoints", lobby.SpawnPoints);
            // 俯瞰カメラは真後ろ(=世界のZ+)を画面奥に向けているので、基準方位は0でよい
            SetFloat(manager, "lobbyBasisYaw", 0f);

            BuildBgmPlayer(scene);
            BuildSePlayer(scene);

            BuildGlobalUI(scene);

            GameObject eventSystem = NewGameObject(scene, "EventSystem");
            eventSystem.AddComponent<EventSystem>();

            // プロジェクト全体の既定アクション（InputSystem.actions）がエディタの状態次第で
            // 一時的に壊れて例外を投げることがある（Point アクションが自分の InputActionAsset に
            // 属していないと言われる）。このゲームのUI操作は Button クリックよりキーボード／パッドの
            // 直接ポーリングが主なので、ここで失敗してもシーン生成全体を止める理由にならない
            try
            {
                eventSystem.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[MagicHand] EventSystemの既定UIアクション割り当てに失敗（無視して続行）: {e.Message}");
            }

            AssignInputActions(player1, player2);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            RegisterInBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[MagicHand] シーンを生成しました: {ScenePath}");
        }

        // ---- ステージ -------------------------------------------------------

        /// <summary>
        /// ステージを構築し、「リスポーン地点として避けたい構造物」の境界ボックス一覧を返す。
        /// 地面は全域を覆っているので当然ここには含めない（含めると全候補が弾かれる）。
        /// </summary>
        private static void BuildLighting(Scene scene)
        {
            GameObject light = NewGameObject(scene, "Directional Light");
            Light lightComponent = light.AddComponent<Light>();
            lightComponent.type = LightType.Directional;
            lightComponent.intensity = 1.1f;
            lightComponent.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static List<Bounds> BuildEnvironment(Scene scene, string stageName, ArenaConfig config,
                                                      Material ground, Material wall, Material obstacle,
                                                      Material pillar, Material platform)
        {
            var blockers = new List<Bounds>();
            float scale = config.Scale;
            Vector3 offset = config.Offset;

            GameObject stage = NewGameObject(scene, stageName);

            float arenaHalf = ArenaHalfSize * scale;

            GameObject floor = CreatePrimitive(PrimitiveType.Plane, "Ground", stage.transform, ground);
            floor.transform.position = offset;
            floor.transform.localScale = new Vector3(arenaHalf / 5f, 1f, arenaHalf / 5f);
            ApplyTiling(floor, 10f, false, 4f);

            // 外周の壁。3階の床(10.3)より高くして場外への落下を防ぐ
            float t = 1f;
            float h = 15f;
            CreateBox("Wall_North", stage.transform, wall, offset + new Vector3(0f, h / 2f, arenaHalf), new Vector3(arenaHalf * 2f, h, t));
            CreateBox("Wall_South", stage.transform, wall, offset + new Vector3(0f, h / 2f, -arenaHalf), new Vector3(arenaHalf * 2f, h, t));
            CreateBox("Wall_East", stage.transform, wall, offset + new Vector3(arenaHalf, h / 2f, 0f), new Vector3(t, h, arenaHalf * 2f));
            CreateBox("Wall_West", stage.transform, wall, offset + new Vector3(-arenaHalf, h / 2f, 0f), new Vector3(t, h, arenaHalf * 2f));

            // 上層は先に作る。スロープの通り道と重なる地上障害物を後から除外したいため
            List<Bounds> rampFootprints = BuildUpperFloors(stage.transform, platform, obstacle, blockers, config);

            // 飛び越えられる高さ（約1.2〜2.3m）の障害物。数を増やしてルート選択の駆け引きを生む
            GameObject obstacles = NewChild("Obstacles", stage.transform);
            // スロープの通り道（|x|が約11〜19 かつ |z|が約8〜23）を避けた配置。
            // 回廊の下（|x|が22以上）は柱代わりの遮蔽として活きるので積極的に使う。
            Vector3[] positions =
            {
                new Vector3(10f, 0f, 8f), new Vector3(-12f, 0f, 6f), new Vector3(4f, 0f, -14f),
                new Vector3(-8f, 0f, -10f), new Vector3(18f, 0f, -4f), new Vector3(-24f, 0f, -10f),
                new Vector3(0f, 0f, 18f), new Vector3(24f, 0f, 14f), new Vector3(-6f, 0f, 22f),
                new Vector3(25f, 0f, 6f), new Vector3(-25f, 0f, 4f), new Vector3(8f, 0f, -22f),
                new Vector3(-24f, 0f, 16f), new Vector3(20f, 0f, 4f), new Vector3(-4f, 0f, 12f),
                new Vector3(3f, 0f, -6f), new Vector3(-20f, 0f, -6f), new Vector3(24f, 0f, -6f),
                new Vector3(-8f, 0f, 20f), new Vector3(6f, 0f, -2f)
            };

            for (int i = 0; i < positions.Length; i++)
            {
                float height = 1.2f + (i % 4) * 0.35f;
                Vector3 size = new Vector3(2.5f + (i % 5), height, 2.5f + (i % 3) * 1.2f);
                Vector3 scaledPos = new Vector3(positions[i].x * scale, 0f, positions[i].z * scale);
                Vector3 center = offset + scaledPos + Vector3.up * (height / 2f);

                // スロープを塞ぐ位置の障害物は置かない（2階へ上がれなくなるため）
                if (IntersectsAny(new Bounds(center, size), rampFootprints)) continue;

                GameObject box = CreateBox($"Obstacle_{i}", obstacles.transform, obstacle, center, size);
                blockers.Add(box.GetComponent<Renderer>().bounds);
            }

            // 飛び越えられない「柱」。2階の床下まで届かせ、支柱として立体感を出す
            GameObject pillars = NewChild("Pillars", stage.transform);
            // 内側4本は吹き抜けの見通しを切る遮蔽、外側2本は橋の真下に立てて支柱に見せる
            Vector3[] pillarPositions =
            {
                new Vector3(8f, 0f, 9f), new Vector3(-8f, 0f, 9f),
                new Vector3(8f, 0f, -9f), new Vector3(-8f, 0f, -9f),
                new Vector3(0f, 0f, 20f), new Vector3(0f, 0f, -20f)
            };
            const float pillarHeight = SecondFloorHeight - SlabThickness / 2f;

            for (int i = 0; i < pillarPositions.Length; i++)
            {
                Vector3 size = new Vector3(3f, pillarHeight, 3f);
                Vector3 scaledPos = new Vector3(pillarPositions[i].x * scale, 0f, pillarPositions[i].z * scale);
                Vector3 center = offset + scaledPos + Vector3.up * (pillarHeight / 2f);

                if (IntersectsAny(new Bounds(center, size), rampFootprints)) continue;

                GameObject box = CreateBox($"Pillar_{i}", pillars.transform, pillar, center, size);
                blockers.Add(box.GetComponent<Renderer>().bounds);
            }

            return blockers;
        }

        /// <summary>
        /// 天井なしの2階部分を作る。構成は
        ///   ・外周をぐるりと回れるリング状の回廊
        ///   ・中央のハブ（広場）
        ///   ・ハブと回廊をつなぐ4本の橋
        /// で、橋と橋の間は大きく吹き抜けになっており、上から地上が見下ろせる／飛び降りられる。
        ///
        /// 2階の高さ(5.0)はジャンプでは届かないので、上がるには必ず4本のスロープを使う。
        /// 戻り値はスロープの占有領域（ここに地上障害物を置くと登れなくなるため除外に使う）。
        /// </summary>
        private static List<Bounds> BuildUpperFloors(Transform stageParent, Material platform, Material obstacle,
                                                     List<Bounds> blockers, ArenaConfig config)
        {
            GameObject layer = NewChild("SecondFloor", stageParent);
            var rampFootprints = new List<Bounds>();

            float s = config.UpperScale;
            Vector3 offset = config.Offset;

            const float y = SecondFloorHeight;
            const float thickness = SlabThickness;
            float ringWidth = 8f * s;
            float ringCenter = (ArenaHalfSize - 8f / 2f) * s;   // 26 (ノーマル時)
            float ringInner = (ArenaHalfSize - 8f) * s;         // 22 (ノーマル時)
            float span = ArenaHalfSize * 2f * s;                 // 60 (ノーマル時)

            // --- 外周回廊（角で重なって面がちらつかないよう、東西は南北の内側だけを埋める） ---
            AddSlab(layer.transform, platform, blockers, "Deck_North", offset + new Vector3(0f, y, ringCenter), new Vector3(span, thickness, ringWidth));
            AddSlab(layer.transform, platform, blockers, "Deck_South", offset + new Vector3(0f, y, -ringCenter), new Vector3(span, thickness, ringWidth));
            AddSlab(layer.transform, platform, blockers, "Deck_East", offset + new Vector3(ringCenter, y, 0f), new Vector3(ringWidth, thickness, ringInner * 2f));
            AddSlab(layer.transform, platform, blockers, "Deck_West", offset + new Vector3(-ringCenter, y, 0f), new Vector3(ringWidth, thickness, ringInner * 2f));

            // --- 中央ハブと、そこへ渡る4本の橋 ---
            float hubSize = 12f * s;
            float bridgeWidth = 6f * s;
            float bridgeLength = ringInner - hubSize / 2f;             // 16 (ノーマル時)
            float bridgeCenter = hubSize / 2f + bridgeLength / 2f;     // 14 (ノーマル時)

            AddSlab(layer.transform, platform, blockers, "Hub", offset + new Vector3(0f, y, 0f), new Vector3(hubSize, thickness, hubSize));
            AddSlab(layer.transform, platform, blockers, "Bridge_North", offset + new Vector3(0f, y, bridgeCenter), new Vector3(bridgeWidth, thickness, bridgeLength));
            AddSlab(layer.transform, platform, blockers, "Bridge_South", offset + new Vector3(0f, y, -bridgeCenter), new Vector3(bridgeWidth, thickness, bridgeLength));
            AddSlab(layer.transform, platform, blockers, "Bridge_East", offset + new Vector3(bridgeCenter, y, 0f), new Vector3(bridgeLength, thickness, bridgeWidth));
            AddSlab(layer.transform, platform, blockers, "Bridge_West", offset + new Vector3(-bridgeCenter, y, 0f), new Vector3(bridgeLength, thickness, bridgeWidth));

            // --- 地上と2階をつなぐスロープ（4象限に1本ずつ） ---
            float deckTop = y + thickness / 2f;
            float rampWidth = 6f * s;
            float rampRun = 13f * s;   // 高さ5.3 / 走り13 ≒ 22度。走って登れる勾配（ノーマル時）
            float rampX = 15f * s;

            AddRamp(layer.transform, platform, blockers, rampFootprints, "Ramp_NE",
                    offset + new Vector3(rampX, 0f, ringInner - rampRun), offset + new Vector3(rampX, deckTop, ringInner), rampWidth, thickness);
            AddRamp(layer.transform, platform, blockers, rampFootprints, "Ramp_NW",
                    offset + new Vector3(-rampX, 0f, ringInner - rampRun), offset + new Vector3(-rampX, deckTop, ringInner), rampWidth, thickness);
            AddRamp(layer.transform, platform, blockers, rampFootprints, "Ramp_SE",
                    offset + new Vector3(rampX, 0f, -ringInner + rampRun), offset + new Vector3(rampX, deckTop, -ringInner), rampWidth, thickness);
            AddRamp(layer.transform, platform, blockers, rampFootprints, "Ramp_SW",
                    offset + new Vector3(-rampX, 0f, -ringInner + rampRun), offset + new Vector3(-rampX, deckTop, -ringInner), rampWidth, thickness);

            if (config.IncludeThirdFloor)
            {
                BuildThirdFloor(stageParent, platform, blockers, rampFootprints, deckTop);
            }

            BuildUpperCover(layer.transform, obstacle, blockers, rampFootprints, deckTop, config);

            return rampFootprints;
        }

        /// <summary>
        /// 部分的な3階。対角2箇所（北東・南西）の角に見晴らし台を置き、
        /// 2階の回廊から伸びるスロープでつなぐ。3階同士は直接つながっていないので、
        /// 上を取っても回り込まれる余地が残る。
        /// </summary>
        private static void BuildThirdFloor(Transform stageParent, Material platform, List<Bounds> blockers,
                                            List<Bounds> rampFootprints, float secondDeckTop)
        {
            GameObject layer = NewChild("ThirdFloor", stageParent);

            const float y = ThirdFloorHeight;
            const float thickness = SlabThickness;
            const float inner = 17f;                                   // 見晴らし台の内側の辺
            float size = ArenaHalfSize - inner;                        // 13
            float center = (ArenaHalfSize + inner) / 2f;               // 23.5
            float top = y + thickness / 2f;
            const float rampWidth = 6f;

            AddSlab(layer.transform, platform, blockers, "Tower_NE",
                    new Vector3(center, y, center), new Vector3(size, thickness, size));
            AddSlab(layer.transform, platform, blockers, "Tower_SW",
                    new Vector3(-center, y, -center), new Vector3(size, thickness, size));

            // スロープは2階の回廊の上を走って見晴らし台の内側の辺に着く
            AddRamp(layer.transform, platform, blockers, rampFootprints, "Ramp3F_NE",
                    new Vector3(4f, secondDeckTop, center), new Vector3(inner, top, center), rampWidth, thickness);
            AddRamp(layer.transform, platform, blockers, rampFootprints, "Ramp3F_SW",
                    new Vector3(-4f, secondDeckTop, -center), new Vector3(-inner, top, -center), rampWidth, thickness);
        }

        /// <summary>
        /// 2階の回廊とハブに、飛び越えられる高さの遮蔽を置く。
        /// 見通しの良すぎる回廊で一方的に狙われるのを防ぎ、上層でも駆け引きが成立するようにする。
        /// </summary>
        private static void BuildUpperCover(Transform parent, Material obstacle, List<Bounds> blockers,
                                            List<Bounds> rampFootprints, float deckTop, ArenaConfig config)
        {
            GameObject cover = NewChild("UpperCover", parent);
            float s = config.UpperScale;
            Vector3 offset = config.Offset;

            // 回廊は幅8・橋は幅6なので、遮蔽は3以下にして必ず脇を通れるようにする
            Vector3[] spots =
            {
                new Vector3(-12f, 0f, 26f), new Vector3(-24f, 0f, 26f),
                new Vector3(12f, 0f, -26f), new Vector3(24f, 0f, -26f),
                new Vector3(26f, 0f, 12f), new Vector3(26f, 0f, -12f),
                new Vector3(-26f, 0f, 12f), new Vector3(-26f, 0f, -12f),
                new Vector3(4f, 0f, 4f), new Vector3(-4f, 0f, -4f)
            };

            for (int i = 0; i < spots.Length; i++)
            {
                float height = 1.3f + (i % 3) * 0.25f;
                Vector3 size = new Vector3(2.6f + (i % 2) * 0.6f, height, 2.6f + (i % 3) * 0.4f);
                Vector3 center = offset + new Vector3(spots[i].x * s, deckTop + height / 2f, spots[i].z * s);

                if (IntersectsAny(new Bounds(center, size), rampFootprints)) continue;

                GameObject box = CreateBox($"UpperCover_{i}", cover.transform, obstacle, center, size);
                blockers.Add(box.GetComponent<Renderer>().bounds);
            }
        }

        private static void AddSlab(Transform parent, Material material, List<Bounds> blockers,
                                    string name, Vector3 center, Vector3 size)
        {
            GameObject slab = CreateBox(name, parent, material, center, size);
            blockers.Add(slab.GetComponent<Renderer>().bounds);
        }

        private static void AddRamp(Transform parent, Material material, List<Bounds> blockers, List<Bounds> rampFootprints,
                                    string name, Vector3 bottom, Vector3 top, float width, float thickness)
        {
            GameObject ramp = CreateRamp(name, parent, material, bottom, top, width, thickness);
            Bounds bounds = ramp.GetComponent<Renderer>().bounds;

            blockers.Add(bounds);

            // 障害物排除用は、登り口の左右にわずかな余裕を持たせた領域で判定する
            // （広げすぎると本来問題ない障害物まで消えてマップが痩せる）
            Bounds footprint = bounds;
            footprint.Expand(new Vector3(2f, 0f, 2f));
            rampFootprints.Add(footprint);
        }

        private static bool IntersectsAny(Bounds bounds, List<Bounds> others)
        {
            foreach (Bounds other in others)
            {
                if (bounds.Intersects(other)) return true;
            }

            return false;
        }

        /// <summary>
        /// 地上の bottom 地点から高台の top 地点まで、なだらかに傾斜する板（ランプ）を作る。
        /// Quaternion.LookRotation(top-bottom方向, world up) で自然に「登り面」の姿勢になる。
        /// </summary>
        private static GameObject CreateRamp(string name, Transform parent, Material material,
                                             Vector3 bottom, Vector3 top, float width, float thickness)
        {
            GameObject ramp = CreatePrimitive(PrimitiveType.Cube, name, parent, material);

            Vector3 slope = top - bottom;
            float length = slope.magnitude;

            ramp.transform.position = (bottom + top) / 2f;
            ramp.transform.rotation = Quaternion.LookRotation(slope.normalized, Vector3.up);
            ramp.transform.localScale = new Vector3(width, thickness, length);

            return ramp;
        }

        /// <summary>
        /// リスポーン地点を配置する。
        ///
        /// 壁際に湧くと、真後ろに構える追従カメラが壁にめり込んで前に押し出され、
        /// 自キャラが画面いっぱいに映って手選択UIが読めなくなる。
        /// そのため「壁からカメラ距離ぶん以上離れた円周」に置き、
        /// さらに障害物・柱と重なる候補は角度をずらして避ける。
        /// </summary>
        private static RespawnManager BuildRespawnPoints(Scene scene, List<Bounds> blockers, ArenaConfig config)
        {
            GameObject root = NewGameObject(scene, "RespawnManager");
            RespawnManager manager = root.AddComponent<RespawnManager>();

            const float clearance = 2.5f;
            float scale = config.Scale;

            var points = new List<Object>();
            var placed = new List<Vector3>();

            // 1F 外周リング8。壁(±30)からの余裕 = 30 - 20 = 10 で、カメラ距離5＋最小距離3を上回る。
            // 2階の回廊が中心から29.5mあるので、17mだと「相手が中央」のとき
            // 抽選のしきい値に届かず1階が候補から外れてしまう
            AddRingRespawnPoints(root.transform, points, placed, blockers, "1F_Outer", 8, 20f * scale, 0f, clearance, config);

            // 1F 内周リング4。外周と角を半分ずらして重ならないようにする
            AddRingRespawnPoints(root.transform, points, placed, blockers, "1F_Inner", 4,
                                 9f * scale, Mathf.PI / 8f, clearance, config);

            // 2F の8箇所
            AddSecondFloorRespawnPoints(root.transform, points, placed, blockers, config);

            SetList(manager, "points", points.ToArray());
            return manager;
        }

        /// <summary>アリーナ中心を囲む円周上にリスポーン地点を並べる。</summary>
        private static void AddRingRespawnPoints(Transform parent, List<Object> points, List<Vector3> placed,
                                                 List<Bounds> blockers, string label, int count,
                                                 float radius, float angleOffset, float clearance, ArenaConfig config)
        {
            for (int i = 0; i < count; i++)
            {
                float baseAngle = angleOffset + i * Mathf.PI * 2f / count;
                Vector3 position = FindClearSpot(baseAngle, radius, clearance, blockers, placed, config);

                // アリーナ中心を向かせる（カメラは背後＝外周側に構えるので壁から離れる向き）
                Vector3 local = position - config.Offset;
                Vector3 toCenter = new Vector3(-local.x, 0f, -local.z);
                CreateRespawnPoint(parent, points, placed, $"RespawnPoint_{label}_{i}", position, toCenter);
            }
        }

        /// <summary>
        /// 2階のリスポーン地点。回廊4箇所と橋4箇所。
        ///
        /// 回廊は外壁まで4mしかないので、中心ではなく**通路に沿った向き**にしてある。
        /// 中心を向かせるとカメラが背後＝外壁の向こう側に回り込んでしまうため。
        /// 橋は左右どちらも開けているので中心向きでよい。
        ///
        /// 回廊は中心から26mあり、1Fの外周リング(17m)より遠い。
        /// リスポーンは「相手から最も遠い地点」を選ぶので、ここが無いと2階はまず選ばれない。
        /// </summary>
        private static void AddSecondFloorRespawnPoints(Transform parent, List<Object> points,
                                                        List<Vector3> placed, List<Bounds> blockers, ArenaConfig config)
        {
            float y = SecondFloorTop + 0.1f;
            float s = config.UpperScale;
            Vector3 offset = config.Offset;

            Vector3 P(float x, float z) => offset + new Vector3(x * s, y, z * s);

            // 回廊。位置は各辺の中央からずらして、橋との合流点を避けている。
            // 北と南は3階へのスロープ（Ramp3F_NE が北の x 3.9〜17.1、
            // Ramp3F_SW が南の x -17.5〜-4.5）を踏むので、辺の反対側の半分に置く
            AddSecondFloorPoint(parent, points, placed, blockers, "CorridorN", P(-14f, 26f), Vector3.right, config);
            AddSecondFloorPoint(parent, points, placed, blockers, "CorridorS", P(14f, -26f), Vector3.left, config);
            AddSecondFloorPoint(parent, points, placed, blockers, "CorridorE", P(26f, -14f), Vector3.forward, config);
            AddSecondFloorPoint(parent, points, placed, blockers, "CorridorW", P(-26f, 14f), Vector3.back, config);

            // 橋の中央。カメラは中心と反対＝橋の付け根側(19m)に構えるので橋の上に収まる
            AddSecondFloorPoint(parent, points, placed, blockers, "BridgeN", P(0f, 14f), Vector3.back, config);
            AddSecondFloorPoint(parent, points, placed, blockers, "BridgeS", P(0f, -14f), Vector3.forward, config);
            AddSecondFloorPoint(parent, points, placed, blockers, "BridgeE", P(14f, 0f), Vector3.left, config);
            AddSecondFloorPoint(parent, points, placed, blockers, "BridgeW", P(-14f, 0f), Vector3.right, config);
        }

        private static void AddSecondFloorPoint(Transform parent, List<Object> points, List<Vector3> placed,
                                                List<Bounds> blockers, string label,
                                                Vector3 nominal, Vector3 facing, ArenaConfig config)
        {
            Vector3 position = SlideUntilClear(nominal, facing, blockers, label, config);
            CreateRespawnPoint(parent, points, placed, $"RespawnPoint_2F_{label}", position, facing);
        }

        /// <summary>追従カメラがキャラの背後に構える距離（ThirdPersonCameraRig の distance と揃える）。</summary>
        private const float CameraDistance = 5f;

        /// <summary>
        /// 2階の地点が上層の遮蔽物に食い込むときに、通路に沿ってずらして逃がす。
        ///
        /// ずらす向きは「向いている方向」に限っている。
        /// カメラは背後に構えるので、前へ動かすぶんにはカメラが通路から外へ出ない。
        /// </summary>
        private static Vector3 SlideUntilClear(Vector3 nominal, Vector3 facing, List<Bounds> blockers, string label,
                                               ArenaConfig config)
        {
            Vector3 direction = facing.normalized;
            float maxSlide = 18f * config.UpperScale;

            // 回廊には 3m 角の遮蔽物が 12m 間隔で並んでいる。
            // 体とカメラの両方を空けようとすると 8m では足りず、
            // 遮蔽物1つぶんを跨げるところまで探す必要がある（回廊は56mあるので余裕はある）
            for (float offset = 0f; offset <= maxSlide; offset += 1f)
            {
                Vector3 candidate = nominal + direction * offset;
                if (IsRespawnSpotUsable(candidate, direction, blockers)) return candidate;
            }

            Debug.LogWarning($"[MagicHand] 2階のリスポーン地点をずらしきれませんでした: {label}");
            return nominal;
        }

        /// <summary>
        /// リスポーン地点として使えるか。立てる場所であることに加えて、
        /// **背後のカメラ位置も塞がっていない**ことを求める。
        ///
        /// 地点そのものが空いていても、背後に遮蔽物があるとカメラが手前へ押し出され、
        /// 自キャラが画面いっぱいに映って手選択UIが読めなくなる。
        /// 壁から離す理由と同じで、判定すべきはキャラとカメラの両方。
        /// </summary>
        private static bool IsRespawnSpotUsable(Vector3 candidate, Vector3 facing, List<Bounds> blockers)
        {
            const float bodyClearance = 2f;
            const float cameraClearance = 1.5f;

            if (!IsSpawnSpotClear(candidate, bodyClearance, blockers)) return false;

            Vector3 cameraSpot = candidate - facing.normalized * CameraDistance;
            return IsSpawnSpotClear(cameraSpot, cameraClearance, blockers);
        }

        private static void CreateRespawnPoint(Transform parent, List<Object> points, List<Vector3> placed,
                                               string name, Vector3 position, Vector3 facing)
        {
            GameObject go = NewChild(name, parent);
            go.transform.position = position;
            go.transform.rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);

            points.Add(go.AddComponent<RespawnPoint>());
            placed.Add(position);
        }

        /// <summary>
        /// 指定の角度・半径を起点に、障害物と重ならない地点を探す。
        /// 角度を左右へ少しずつずらし、それでも駄目なら半径を内側へ縮めて再挑戦する。
        /// 既に置いたリスポーン地点の近くも避ける。数を増やしたぶん、
        /// 内周と外周で同じ場所に固まると「相手から最も遠い地点」の選択肢が実質減るため。
        /// </summary>
        private static Vector3 FindClearSpot(float baseAngle, float radius, float clearance,
                                             List<Bounds> blockers, List<Vector3> placed, ArenaConfig config)
        {
            // 間隔は「散らばっていた方が良い」だけの条件で、障害物回避のような必須条件ではない。
            // 両立できないときは間隔を諦めてでも、構造物に埋まらない場所を優先する
            float[] separations = { 6f, 3f, 0f };

            foreach (float separation in separations)
            {
                for (float shrink = 0f; shrink <= 8f; shrink += 2f)
                {
                    float r = radius - shrink;

                    for (int step = 0; step <= 12; step++)
                    {
                        // 0, +7.5°, -7.5°, +15°, -15° ... と交互に振って元の角度からの逸脱を最小化する
                        float offset = Mathf.CeilToInt(step / 2f) * Mathf.Deg2Rad * 7.5f * (step % 2 == 0 ? 1f : -1f);
                        float angle = baseAngle + offset;
                        Vector3 candidate = config.Offset + new Vector3(Mathf.Cos(angle) * r, 0.1f, Mathf.Sin(angle) * r);

                        if (!IsClearOfBlockers(candidate, clearance, blockers)) continue;
                        if (!IsFarFromOthers(candidate, separation, placed)) continue;

                        // 中心を向くので、カメラは外周側＝中心と反対に構える
                        Vector3 local = candidate - config.Offset;
                        Vector3 facing = new Vector3(-local.x, 0f, -local.z).normalized;
                        if (!IsRespawnSpotUsable(candidate, facing, blockers)) continue;

                        return candidate;
                    }
                }
            }

            // どうしても見つからない場合は元の位置（起きないはずだが安全弁）
            Debug.LogWarning($"[MagicHand] 障害物のないリスポーン地点が見つかりませんでした (angle={baseAngle * Mathf.Rad2Deg:F0}°)");
            return config.Offset + new Vector3(Mathf.Cos(baseAngle) * radius, 0.1f, Mathf.Sin(baseAngle) * radius);
        }

        /// <summary>候補地点が、どの構造物からも clearance 以上離れているか。</summary>
        private static bool IsClearOfBlockers(Vector3 candidate, float clearance, List<Bounds> blockers)
        {
            // 胴体の高さで見る。高台の真下のような「頭上が塞がった」場所も弾きたいため
            Vector3 probe = candidate + Vector3.up * 1f;
            float sqrClearance = clearance * clearance;

            foreach (Bounds bounds in blockers)
            {
                if (bounds.SqrDistance(probe) < sqrClearance) return false;
            }

            return true;
        }

        private static ItemSpawnManager BuildItemSpawners(Scene scene, string name, ItemPickup prefab,
                                                          List<ItemDefinitionSO> handItems,
                                                          RandomScrollSO randomScroll,
                                                          List<ItemDefinitionSO> brooms,
                                                          List<Bounds> blockers, ArenaConfig config,
                                                          int handSpawnPoints, int handTarget,
                                                          int itemSpawnPoints, int itemTarget)
        {
            GameObject root = NewGameObject(scene, name);
            ItemSpawnManager manager = root.AddComponent<ItemSpawnManager>();

            Transform handRoot = NewChild("HandSpawnPoints", root.transform).transform;
            Transform itemRoot = NewChild("ItemSpawnPoints", root.transform).transform;

            // スポーン地点はマップ全域（1F/2F、ノーマルは3Fも）へランダムに散らす。
            // 毎回同じ地形になるよう乱数の種を固定し、生成後に元の状態へ戻す。
            Random.State previousRandom = Random.state;
            Random.InitState(20260812);

            var occupied = new List<Vector3>();
            SpawnArea[] ground = GroundSpawnAreas(config);
            SpawnArea[] second = SecondFloorSpawnAreas(config);
            SpawnArea[] third = config.IncludeThirdFloor ? ThirdFloorSpawnAreas(config) : System.Array.Empty<SpawnArea>();

            // 手変更アイテム。1F/2F/3Fへおおむね半々＋残りで配分する（3F無しのモードは1F/2Fのみ）
            int handGround = third.Length > 0 ? Mathf.RoundToInt(handSpawnPoints * 0.44f) : Mathf.RoundToInt(handSpawnPoints * 0.55f);
            int handSecond = third.Length > 0 ? Mathf.RoundToInt(handSpawnPoints * 0.40f) : handSpawnPoints - handGround;
            int handThird = handSpawnPoints - handGround - handSecond;
            ScatterSpawnPoints(handRoot, "HandSpawn_1F", handGround, ground, blockers, occupied);
            ScatterSpawnPoints(handRoot, "HandSpawn_2F", handSecond, second, blockers, occupied);
            if (handThird > 0) ScatterSpawnPoints(handRoot, "HandSpawn_3F", handThird, third, blockers, occupied);

            // アイテム（スクロール＋ほうき）。手変更と同じ比率で配分する
            int itemGround = third.Length > 0 ? Mathf.RoundToInt(itemSpawnPoints * 0.46f) : Mathf.RoundToInt(itemSpawnPoints * 0.55f);
            int itemSecond = third.Length > 0 ? Mathf.RoundToInt(itemSpawnPoints * 0.40f) : itemSpawnPoints - itemGround;
            int itemThird = itemSpawnPoints - itemGround - itemSecond;
            ScatterSpawnPoints(itemRoot, "ItemSpawn_1F", itemGround, ground, blockers, occupied);
            ScatterSpawnPoints(itemRoot, "ItemSpawn_2F", itemSecond, second, blockers, occupied);
            if (itemThird > 0) ScatterSpawnPoints(itemRoot, "ItemSpawn_3F", itemThird, third, blockers, occupied);

            Random.state = previousRandom;

            SetObject(manager, "itemPrefab", prefab);

            SerializedObject so = new SerializedObject(manager);
            SerializedProperty groups = so.FindProperty("groups");
            groups.arraySize = 2;

            // 手変更は勝敗を決める基本アイテムなので、準備ルームの設定パネルではON/OFFさせない
            ConfigureGroup(groups.GetArrayElementAtIndex(0), "HandItems", handRoot, handItems, handTarget, 10f, 10f,
                          collectCooldown: 0f, guaranteedItem: null, guaranteedCount: 0, includeInSettings: false);

            // ほうきはスクロールと同じ枠を使うが、常に1個は必ずほうきになるよう
            // 保証枠（guaranteedItem）で確保する。抽選テーブルにほうきは含めない
            // （含めると保証ぶんと合わせて2本以上出ることがある）。
            // 抽選テーブルには（Easyでは絞った）巻物候補を持つ RandomScrollSO を1個だけ入れる。
            // 「どの巻物か」は湧く瞬間ではなく拾った瞬間に決まる仕様のため
            ConfigureGroup(groups.GetArrayElementAtIndex(1), "Items", itemRoot,
                          new List<ItemDefinitionSO> { randomScroll }, itemTarget, 10f, 10f,
                          collectCooldown: 0f, guaranteedItem: brooms[0], guaranteedCount: 1);

            so.ApplyModifiedPropertiesWithoutUndo();
            return manager;
        }

        private static void ConfigureGroup(SerializedProperty group, string name, Transform root,
                                           List<ItemDefinitionSO> loot, int targetCount,
                                           float minDelay, float maxDelay, float collectCooldown = 0f,
                                           ItemDefinitionSO guaranteedItem = null, int guaranteedCount = 0,
                                           bool includeInSettings = true)
        {
            group.FindPropertyRelative("groupName").stringValue = name;
            group.FindPropertyRelative("spawnRoot").objectReferenceValue = root;
            group.FindPropertyRelative("targetCount").intValue = targetCount;
            group.FindPropertyRelative("minRespawnDelay").floatValue = minDelay;
            group.FindPropertyRelative("maxRespawnDelay").floatValue = maxDelay;
            group.FindPropertyRelative("collectCooldown").floatValue = collectCooldown;
            group.FindPropertyRelative("guaranteedItem").objectReferenceValue = guaranteedItem;
            group.FindPropertyRelative("guaranteedCount").intValue = guaranteedCount;
            group.FindPropertyRelative("includeInSettings").boolValue = includeInSettings;

            SerializedProperty points = group.FindPropertyRelative("spawnPoints");
            points.arraySize = root.childCount;
            for (int i = 0; i < root.childCount; i++)
            {
                points.GetArrayElementAtIndex(i).objectReferenceValue = root.GetChild(i);
            }

            SerializedProperty table = group.FindPropertyRelative("lootTable");
            table.arraySize = loot.Count;
            for (int i = 0; i < loot.Count; i++)
            {
                table.GetArrayElementAtIndex(i).objectReferenceValue = loot[i];
            }
        }

        /// <summary>床が存在する矩形領域。この中からランダムにスポーン地点を選ぶ。</summary>
        private struct SpawnArea
        {
            public readonly float MinX, MaxX, MinZ, MaxZ, Y;

            public SpawnArea(float minX, float minZ, float maxX, float maxZ, float y)
            {
                MinX = minX; MinZ = minZ; MaxX = maxX; MaxZ = maxZ; Y = y;
            }

            public float Weight => (MaxX - MinX) * (MaxZ - MinZ);

            public Vector3 RandomPoint() =>
                new Vector3(Random.Range(MinX, MaxX), Y, Random.Range(MinZ, MaxZ));
        }

        private const float SpawnItemHeight = 0.4f;

        private static SpawnArea[] GroundSpawnAreas(ArenaConfig config)
        {
            float s = config.Scale;
            Vector3 o = config.Offset;
            return new[]
            {
                new SpawnArea(o.x - 27f * s, o.z - 27f * s, o.x + 27f * s, o.z + 27f * s, o.y + SpawnItemHeight)
            };
        }

        /// <summary>2階の歩ける面（回廊4辺・ハブ・橋4本）。</summary>
        private static SpawnArea[] SecondFloorSpawnAreas(ArenaConfig config)
        {
            float y = SecondFloorTop + SpawnItemHeight + config.Offset.y;
            float s = config.UpperScale;
            float ox = config.Offset.x;
            float oz = config.Offset.z;

            return new[]
            {
                new SpawnArea(ox - 28f * s, oz + 23f * s, ox + 28f * s, oz + 29f * s, y),      // 北回廊
                new SpawnArea(ox - 28f * s, oz - 29f * s, ox + 28f * s, oz - 23f * s, y),      // 南回廊
                new SpawnArea(ox + 23f * s, oz - 21f * s, ox + 29f * s, oz + 21f * s, y),      // 東回廊
                new SpawnArea(ox - 29f * s, oz - 21f * s, ox - 23f * s, oz + 21f * s, y),      // 西回廊
                new SpawnArea(ox - 5f * s, oz - 5f * s, ox + 5f * s, oz + 5f * s, y),          // 中央ハブ
                new SpawnArea(ox - 2.5f * s, oz + 7f * s, ox + 2.5f * s, oz + 21f * s, y),     // 北の橋
                new SpawnArea(ox - 2.5f * s, oz - 21f * s, ox + 2.5f * s, oz - 7f * s, y),     // 南の橋
                new SpawnArea(ox + 7f * s, oz - 2.5f * s, ox + 21f * s, oz + 2.5f * s, y),     // 東の橋
                new SpawnArea(ox - 21f * s, oz - 2.5f * s, ox - 7f * s, oz + 2.5f * s, y)      // 西の橋
            };
        }

        /// <summary>3階の見晴らし台2箇所（ノーマルのみ）。</summary>
        private static SpawnArea[] ThirdFloorSpawnAreas(ArenaConfig config)
        {
            float y = ThirdFloorTop + SpawnItemHeight + config.Offset.y;
            float ox = config.Offset.x;
            float oz = config.Offset.z;

            return new[]
            {
                new SpawnArea(ox + 18f, oz + 18f, ox + 29f, oz + 29f, y),
                new SpawnArea(ox - 29f, oz - 29f, ox - 18f, oz - 18f, y)
            };
        }

        /// <summary>
        /// 指定領域の中へランダムにスポーン地点を撒く。
        /// 障害物に埋まる場所と、既存のスポーン地点に近すぎる場所は避ける。
        /// </summary>
        private static void ScatterSpawnPoints(Transform parent, string prefix, int count,
                                               SpawnArea[] areas, List<Bounds> blockers, List<Vector3> occupied)
        {
            const float clearance = 1.4f;
            const int attemptsPerPoint = 300;
            float separation = 5f;

            float totalWeight = 0f;
            foreach (SpawnArea area in areas) totalWeight += area.Weight;

            for (int i = 0; i < count; i++)
            {
                Vector3 chosen = Vector3.zero;
                bool found = false;

                for (int attempt = 0; attempt < attemptsPerPoint && !found; attempt++)
                {
                    // 詰まってきたら間隔の条件を緩めて必ず所定数を確保する
                    if (attempt > 0 && attempt % 60 == 0) separation *= 0.75f;

                    Vector3 candidate = PickArea(areas, totalWeight).RandomPoint();

                    if (!IsSpawnSpotClear(candidate, clearance, blockers)) continue;
                    if (!IsFarFromOthers(candidate, separation, occupied)) continue;

                    chosen = candidate;
                    found = true;
                }

                if (!found)
                {
                    Debug.LogWarning($"[MagicHand] スポーン地点を確保できませんでした: {prefix}_{i}");
                    continue;
                }

                occupied.Add(chosen);
                NewChild($"{prefix}_{i}", parent).transform.position = chosen;
                separation = 5f;
            }
        }

        private static SpawnArea PickArea(SpawnArea[] areas, float totalWeight)
        {
            float roll = Random.Range(0f, totalWeight);

            foreach (SpawnArea area in areas)
            {
                roll -= area.Weight;
                if (roll <= 0f) return area;
            }

            return areas[areas.Length - 1];
        }

        /// <summary>
        /// アイテムが構造物に埋まらないか判定する。
        /// 「その地点に立ったとき身体が occupy する高さ帯」と重なる物だけを見るので、
        /// 足元の床スラブ自体は障害物として数えない。
        /// </summary>
        private static bool IsSpawnSpotClear(Vector3 point, float clearance, List<Bounds> blockers)
        {
            const float bodyHeight = 1.8f;
            float bandTop = point.y + bodyHeight;
            float sqrClearance = clearance * clearance;

            foreach (Bounds bounds in blockers)
            {
                if (bounds.max.y <= point.y + 0.05f) continue;   // 床など、足元より下にある
                if (bounds.min.y >= bandTop) continue;           // 頭上を跨ぐ床や橋

                float dx = point.x - Mathf.Clamp(point.x, bounds.min.x, bounds.max.x);
                float dz = point.z - Mathf.Clamp(point.z, bounds.min.z, bounds.max.z);

                if (dx * dx + dz * dz < sqrClearance) return false;
            }

            return true;
        }

        private static bool IsFarFromOthers(Vector3 point, float separation, List<Vector3> occupied)
        {
            float sqrSeparation = separation * separation;

            foreach (Vector3 other in occupied)
            {
                if ((other - point).sqrMagnitude < sqrSeparation) return false;
            }

            return true;
        }

        // ---- プレイヤー -----------------------------------------------------

        private static PlayerController BuildPlayer(Scene scene, int index, string label, Material material,
                                                    Material ringMaterial, Rect viewport, bool audioListener,
                                                    string characterPrefabPath, AnimatorController animatorController,
                                                    Material[] handMaterials, RevealMarker exposureMarker,
                                                    Material broomHandleMat, Material broomBristleMat,
                                                    CastEffect castEffectPrefab, Material statusAuraMat,
                                                    Material speedArrowMat, Material stunBoltMat,
                                                    List<ItemDefinitionSO> scrolls)
        {
            GameObject root = NewGameObject(scene, $"Player{index + 1}");
            root.transform.position = new Vector3(index == 0 ? -6f : 6f, 0f, 0f);

            Rigidbody body = root.AddComponent<Rigidbody>();
            body.mass = 1f;
            body.linearDamping = 0.5f;
            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;

            CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 1f, 0f);
            capsule.height = 2f;
            capsule.radius = 0.5f;

            // 接地中と空中で摩擦を使い分ける（PlayerController が切り替える）
            PhysicsMaterial groundedMaterial = CreatePlayerPhysicsMaterial("PM_PlayerGrounded", 0.6f, PhysicsMaterialCombine.Average);
            PhysicsMaterial airborneMaterial = CreatePlayerPhysicsMaterial("PM_PlayerAirborne", 0f, PhysicsMaterialCombine.Minimum);
            capsule.sharedMaterial = groundedMaterial;

            SphereCollider hitbox = root.AddComponent<SphereCollider>();
            hitbox.isTrigger = true;
            hitbox.center = new Vector3(0f, 1f, 0f);
            hitbox.radius = 0.9f;

            // 自分だけに見せたい表示（範囲円）用のレイヤー。相手のカメラからは除外する
            int ownLayer = EnsureLayer($"P{index + 1}Only");
            int rivalLayer = EnsureLayer($"P{(index == 0 ? 2 : 1)}Only");

            // カメラ（分割画面）
            GameObject cameraGo = NewGameObject(scene, $"Camera_P{index + 1}");
            Camera camera = cameraGo.AddComponent<Camera>();
            camera.rect = viewport;
            camera.depth = index;
            // 視野角を絞ると被写体が大きく写り、圧迫感・臨場感が増す（望遠寄り）
            camera.fieldOfView = 52f;
            if (rivalLayer >= 0) camera.cullingMask &= ~(1 << rivalLayer);
            if (audioListener) cameraGo.AddComponent<AudioListener>();

            ThirdPersonCameraRig rig = cameraGo.AddComponent<ThirdPersonCameraRig>();
            SetObject(rig, "target", root.transform);

            PlayerController controller = root.AddComponent<PlayerController>();

            // PlayerController の [RequireComponent(typeof(ScrollStock))] で既に付いているので、
            // ここで AddComponent すると2つ目ができてしまい、設定した方とゲームが実際に使う方
            // （GetComponent が拾う最初の1つ）がズレる。PlayerFlight と同じ「無ければ足す」形にする
            ScrollStock scrollStock = root.GetComponent<ScrollStock>();
            if (scrollStock == null) scrollStock = root.AddComponent<ScrollStock>();

            root.AddComponent<PlayerCombat>();

            SetObject(scrollStock, "castEffectPrefab", castEffectPrefab);

            SetInt(controller, "playerIndex", index);
            SetString(controller, "displayName", label);
            SetObject(controller, "cameraRig", rig);
            SetInt(controller, "ownViewLayer", ownLayer);
            SetObject(controller, "groundedMaterial", groundedMaterial);
            SetObject(controller, "airborneMaterial", airborneMaterial);

            // カプセルの代わりにキャラクターモデルを載せる。当たり判定はカプセルコライダーのまま
            GameObject visual = CreateCharacterVisual(root.transform, characterPrefabPath, animatorController, controller);

            // 高度上限はここから測る。準備ルームは本編の遥か下(-100)に置かれているので別に持たせる
            // PlayerController の RequireComponent で既に付いているが、念のため
            PlayerFlight flight = root.GetComponent<PlayerFlight>();
            if (flight == null) flight = root.AddComponent<PlayerFlight>();

            SetObject(flight, "player", controller);
            SetObject(flight, "exposureMarkerPrefab", exposureMarker);
            SetFloat(flight, "arenaFloorY", 0f);
            SetFloat(flight, "lobbyFloorY", LobbyOrigin.y);

            if (visual != null) AttachBroom(visual.transform, controller, broomHandleMat, broomBristleMat);

            PlayerVisual playerVisual = root.AddComponent<PlayerVisual>();
            SetObject(playerVisual, "player", controller);
            SetList(playerVisual, "handMaterials", System.Array.ConvertAll(handMaterials, m => (Object)m));

            // 色を変えるのは体だけ。杖まで手の色に染まると持ち物との区別がつかなくなる
            SetList(playerVisual, "targetRenderers",
                    visual != null
                        ? System.Array.ConvertAll(visual.GetComponentsInChildren<SkinnedMeshRenderer>(), r => (Object)r)
                        : new Object[0]);

            BuildRangeIndicator(root.transform, controller, ringMaterial, ownLayer);
            BuildHandIndicator(root.transform, controller, material, rivalLayer, out Renderer[] handOutlineRenderers);
            BuildHandAdvantageIndicator(root.transform, controller, rivalLayer, handOutlineRenderers);
            BuildStatusAura(root.transform, controller, statusAuraMat);
            BuildSpeedUpEffect(root.transform, controller, speedArrowMat);
            BuildStunEffect(root.transform, controller, stunBoltMat);
            BuildBlinkIndicator(scene, controller, ringMaterial, ownLayer);
            BuildCarryLabel(root.transform, controller, label);
            ConfigurePlayerInput(root);
            BuildPlayerUI(scene, index, controller, camera, scrolls);

            return controller;
        }

        /// <summary>
        /// 準備ルームで頭上に出す所持表示。
        /// プレイヤーの子に置き、PlayerCarryLabel が位置を追従させる。
        /// </summary>
        private static void BuildCarryLabel(Transform playerRoot, PlayerController player, string displayName)
        {
            Text label = CreateWorldLabel(playerRoot, "CarryLabel", displayName,
                                          playerRoot.position + new Vector3(0f, 2.9f, 0f), Color.white);

            PlayerCarryLabel component = label.gameObject.AddComponent<PlayerCarryLabel>();
            SetObject(component, "player", player);
            SetObject(component, "label", label);
            SetObject(component, "followTarget", playerRoot);

            // GameObject は常に有効のまま。表示切り替えは Text の enabled で行う
            label.enabled = false;
        }

        /// <summary>範囲を持つスクロール所持中に足元へ出す効果範囲の輪を作る。</summary>
        private static void BuildRangeIndicator(Transform playerRoot, PlayerController player, Material material, int layer)
        {
            GameObject go = NewChild("ScrollRangeIndicator", playerRoot);
            if (layer >= 0) go.layer = layer;

            LineRenderer ring = go.AddComponent<LineRenderer>();
            ring.sharedMaterial = material;
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.alignment = LineAlignment.TransformZ;
            ring.widthMultiplier = 0.18f;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.enabled = false;

            ScrollRangeIndicator indicator = go.AddComponent<ScrollRangeIndicator>();
            SetObject(indicator, "player", player);
            SetObject(indicator, "ring", ring);
        }

        /// <summary>
        /// 何か時間制限つきの効果がかかっている間、足元に出し続ける輪を作る。
        /// ScrollRangeIndicator と違い相手にも見せたいので、レイヤーは絞らない。
        /// </summary>
        private static void BuildStatusAura(Transform playerRoot, PlayerController player, Material material)
        {
            GameObject go = NewChild("StatusAura", playerRoot);

            LineRenderer ring = go.AddComponent<LineRenderer>();
            ring.sharedMaterial = material;
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.alignment = LineAlignment.TransformZ;
            ring.widthMultiplier = 0.10f;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.enabled = false;

            StatusAura aura = go.AddComponent<StatusAura>();
            SetObject(aura, "player", player);
            SetObject(aura, "ring", ring);
        }

        /// <summary>
        /// 頭上に出す専用モデルの目標サイズ。元は地面のアイテムより一回り小さい 0.35 だったが、
        /// 「頭上表示をもっと目立たせたい」という依頼で3倍の 1.05 にした（2026-08-22）。
        /// </summary>
        private const float HandIndicatorTargetSize = 1.05f;

        /// <summary>
        /// 頭上に出す「相手のカメラだけに見える」今の手の表示。
        ///
        /// レイヤーは rivalLayer（このプレイヤーのカメラが cullingMask から除外している、
        /// つまり相手のカメラだけが映すレイヤー）に固定する。自分のカメラには映らないので、
        /// 自分視点の邪魔にはならない。色だけでなく形（グー/チョキ/パー専用モデル）でも
        /// 相手の手を見分けられるようにする狙い。
        /// </summary>
        private static void BuildHandIndicator(Transform playerRoot, PlayerController player, Material fallbackMaterial, int rivalLayer,
                                               out Renderer[] outlineRenderers)
        {
            GameObject root = NewChild("HandIndicator", playerRoot);
            root.transform.localPosition = new Vector3(0f, 2.3f, 0f);

            // addOutline: true で地面のアイテムと同じ反転殻の縁取りを付ける。
            // 色は既定（白）のままにせず、HandAdvantageIndicator が優位/劣位/互角と
            // 同じ色に毎フレーム染め直す（地面の手アイテムの縁取りとは別マテリアルなので、
            // 白のままの地面側には影響しない）
            GameObject guVisual = CreateHandVisual(GuVisualPrefabPath, "GuVisual", root.transform, fallbackMaterial,
                                                    addOutline: true);
            GameObject chokiVisual = CreateHandVisual(ChokiVisualPrefabPath, "ChokiVisual", root.transform, fallbackMaterial,
                                                       addOutline: true);
            // パーの本は正面から見ると縦長（背表紙が上）になるので、地面のアイテムと同じく横向きに寝かせる
            GameObject paVisual = CreateHandVisual(PaVisualPrefabPath, "PaVisual", root.transform, fallbackMaterial,
                                                    new Vector3(0f, 0f, 90f), addOutline: true);

            // CreateHandVisual は地面用アイテムと同じ高さ(0.6)持ち上げるので、ここでは打ち消す
            guVisual.transform.localPosition = Vector3.zero;
            chokiVisual.transform.localPosition = Vector3.zero;
            paVisual.transform.localPosition = Vector3.zero;

            // 頭上表示と地面のアイテムでは目標サイズが違うので、その比率でスケールし直す
            float shrink = HandIndicatorTargetSize / HandVisualTargetSize;
            guVisual.transform.localScale *= shrink;
            chokiVisual.transform.localScale *= shrink;
            paVisual.transform.localScale *= shrink;

            guVisual.SetActive(false);
            chokiVisual.SetActive(false);
            paVisual.SetActive(false);

            PlayerHandIndicator indicator = root.AddComponent<PlayerHandIndicator>();
            SetObject(indicator, "player", player);
            SetObject(indicator, "guVisual", guVisual);
            SetObject(indicator, "chokiVisual", chokiVisual);
            SetObject(indicator, "paVisual", paVisual);

            if (rivalLayer >= 0) SetLayerRecursively(root, rivalLayer);

            // 3形状ぶんの縁取りレンダラーをまとめる。同時に見えるのは今の手ぶん1つだけなので、
            // 3つまとめて色替え・表示切替しても問題ない
            var outlineList = new List<Renderer>();
            outlineList.AddRange(FindOutlineRenderers(guVisual));
            outlineList.AddRange(FindOutlineRenderers(chokiVisual));
            outlineList.AddRange(FindOutlineRenderers(paVisual));
            outlineRenderers = outlineList.ToArray();
        }

        /// <summary>AddOutlineShellが作った"Outline"子オブジェクト配下のレンダラーを集める。</summary>
        private static Renderer[] FindOutlineRenderers(GameObject visual)
        {
            Transform outline = visual.transform.Find("Outline");
            return outline != null ? outline.GetComponentsInChildren<Renderer>(true) : System.Array.Empty<Renderer>();
        }

        /// <summary>
        /// 相手の頭上表示（<see cref="PlayerHandIndicator"/>）と本体の間に出す、
        /// 「自分が相手の手に勝っているか」を表す文字（優位／劣位／互角）。
        ///
        /// この時点ではまだ相手（viewer）が存在しない（両プレイヤーは順番に作られる）ため、
        /// owner だけ渡して作っておき、viewer は両者ができてから
        /// <see cref="WireHandAdvantageIndicators"/> で後付けする。
        /// レイヤーは頭上表示と同じ rivalLayer（自分には見えず、相手にだけ見える）を使う。
        /// </summary>
        private static void BuildHandAdvantageIndicator(Transform playerRoot, PlayerController owner, int rivalLayer,
                                                         Renderer[] handOutlineRenderers)
        {
            GameObject go = NewChild("HandAdvantageIndicator", playerRoot);
            // 位置（高さ・視聴者側への浮かせ）は HandAdvantageIndicator.LateUpdate が毎フレーム
            // 計算する。以前はここで固定のローカル座標(0,1.3,0.4)を置いていたが、
            // 「ownerのローカル前方」基準だとownerが視聴者に背を向けたときにマークが
            // 体の裏側へ回り込んで隠れてしまっていた（「相手キャラの中心」から外れて見える不具合）。
            // 視聴者へ向かう方向を毎フレーム計算する方式にしたので、ここでの初期位置は問わない

            // 最初は図形（三角形/ひし形）のメッシュだったが、「優位、劣位、互角のまま文字で表示」の
            // 依頼で TextMesh に差し替えた。図形版で得た知見（LineRenderer は billboard の
            // alignment計算がカメラ角度次第で破綻する）はそのまま活きていて、文字も
            // TextMeshのalignment機能ではなく HandAdvantageIndicator 側で毎フレーム
            // transform全体をQuaternion.LookRotationで向けるやり方にしてある
            TextMesh textMesh = go.AddComponent<TextMesh>();
            textMesh.font = BuiltinFont();
            textMesh.fontSize = 64;
            // characterSize=0.1だと実測で幅1.28×高さ0.7（旧三角形マークの0.32×0.16の4倍）になり、
            // 帽子のつば（1.5〜1.6付近）に頭が突き刺さって角度によって隠れる不具合が出たため縮小した
            textMesh.characterSize = 0.045f;
            textMesh.fontStyle = FontStyle.Bold;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.text = string.Empty;

            // TextMesh の既定マテリアル（Font.material、"GUI/Text Shader"）はIMGUI向けの
            // レガシーシェーダーで、URPでは深度テストをまともに行わず壁越しでも常に見えてしまう
            // （「優位/劣位/互角の文字が壁を貫通して見える」不具合の原因）。フォントのテクスチャは
            // そのまま使い、URP対応のUnlitシェーダーへ差し替えて通常の深度テストを効かせる
            Material textMaterial = CreateTextMeshMaterial("M_HandAdvantageText", textMesh.font);

            MeshRenderer meshRenderer = go.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = textMaterial;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.enabled = false;

            // 縁取り：TextMeshにはUIのOutlineコンポーネントが使えないため、
            // ひとまわり大きい黒文字を少し奥（ローカルZ+＝ビルボードでカメラと逆側）に重ねて
            // 縁のように見せる（中心アンカーが同じなので、拡大した分が全方向に均等にはみ出す）
            GameObject outlineGo = NewChild("Outline", go.transform);
            outlineGo.transform.localPosition = new Vector3(0f, 0f, 0.01f);

            TextMesh outlineTextMesh = outlineGo.AddComponent<TextMesh>();
            outlineTextMesh.font = BuiltinFont();
            outlineTextMesh.fontSize = 64;
            outlineTextMesh.characterSize = 0.045f * 1.35f;
            outlineTextMesh.fontStyle = FontStyle.Bold;
            outlineTextMesh.anchor = TextAnchor.MiddleCenter;
            outlineTextMesh.alignment = TextAlignment.Center;
            outlineTextMesh.text = string.Empty;
            outlineTextMesh.color = Color.black;

            MeshRenderer outlineMeshRenderer = outlineGo.GetComponent<MeshRenderer>();
            outlineMeshRenderer.sharedMaterial = textMaterial;
            outlineMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            outlineMeshRenderer.receiveShadows = false;
            outlineMeshRenderer.enabled = false;

            HandAdvantageIndicator indicator = go.AddComponent<HandAdvantageIndicator>();
            SetObject(indicator, "owner", owner);
            SetObject(indicator, "textMesh", textMesh);
            SetObject(indicator, "meshRenderer", meshRenderer);
            SetObject(indicator, "outlineTextMesh", outlineTextMesh);
            SetObject(indicator, "outlineMeshRenderer", outlineMeshRenderer);

            if (handOutlineRenderers != null)
            {
                SetList(indicator, "handOutlineRenderers", System.Array.ConvertAll(handOutlineRenderers, r => (Object)r));
            }

            if (rivalLayer >= 0) SetLayerRecursively(go, rivalLayer);
        }

        /// <summary>
        /// 両プレイヤーが揃ってから、それぞれの優位/劣位マークへ相手（viewer）を差し込む。
        /// </summary>
        private static void WireHandAdvantageIndicators(PlayerController player1, PlayerController player2)
        {
            HandAdvantageIndicator indicator1 = player1.GetComponentInChildren<HandAdvantageIndicator>(true);
            HandAdvantageIndicator indicator2 = player2.GetComponentInChildren<HandAdvantageIndicator>(true);

            if (indicator1 != null) SetObject(indicator1, "viewer", player2);
            if (indicator2 != null) SetObject(indicator2, "viewer", player1);
        }

        /// <summary>レイヤーを子孫まで再帰的に揃える。カメラの cullingMask で丸ごと絞りたいときに使う。</summary>
        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = layer;
            }
        }

        private const int SpeedArrowCount = 5;

        /// <summary>
        /// スピードUp中、自分の周りを駆け上がる小さな上矢印を並べる。
        /// LineAlignment.View にしておくと、どちらのカメラから見ても矢印が正面を向く。
        /// </summary>
        private static void BuildSpeedUpEffect(Transform playerRoot, PlayerController player, Material material)
        {
            GameObject root = NewChild("SpeedUpEffect", playerRoot);
            var arrows = new LineRenderer[SpeedArrowCount];

            for (int i = 0; i < SpeedArrowCount; i++)
            {
                GameObject go = NewChild($"Arrow_{i}", root.transform);

                LineRenderer arrow = go.AddComponent<LineRenderer>();
                arrow.sharedMaterial = material;
                arrow.useWorldSpace = false;
                arrow.loop = false;
                arrow.alignment = LineAlignment.View;
                arrow.widthMultiplier = 0f;
                arrow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                arrow.receiveShadows = false;
                arrow.positionCount = 3;

                // 上向きの山形（^）。杖なし・矢印アイコンと同じ「くの字」の形
                const float halfWidth = 0.07f;
                const float height = 0.22f;
                arrow.SetPosition(0, new Vector3(-halfWidth, 0f, 0f));
                arrow.SetPosition(1, new Vector3(0f, height, 0f));
                arrow.SetPosition(2, new Vector3(halfWidth, 0f, 0f));

                arrow.enabled = false;
                arrows[i] = arrow;
            }

            SpeedUpEffect effect = root.AddComponent<SpeedUpEffect>();
            SetObject(effect, "player", player);
            SetList(effect, "arrows", System.Array.ConvertAll(arrows, a => (Object)a));
        }

        private const int StunBoltCount = 5;
        private const int StunBoltSegments = 5;

        /// <summary>
        /// スタン中、体の周りにビリビリと明滅する稲妻を並べる。
        /// 形と表示タイミングは StunEffect が毎回乱数で作り直すので、ここでは
        /// 点の数（positionCount）だけ確保しておけばよい。
        /// </summary>
        private static void BuildStunEffect(Transform playerRoot, PlayerController player, Material material)
        {
            GameObject root = NewChild("StunEffect", playerRoot);
            var bolts = new LineRenderer[StunBoltCount];

            for (int i = 0; i < StunBoltCount; i++)
            {
                GameObject go = NewChild($"Bolt_{i}", root.transform);

                LineRenderer bolt = go.AddComponent<LineRenderer>();
                bolt.sharedMaterial = material;
                bolt.useWorldSpace = false;
                bolt.loop = false;
                bolt.alignment = LineAlignment.View;
                bolt.widthMultiplier = 0f;
                bolt.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                bolt.receiveShadows = false;
                bolt.positionCount = StunBoltSegments;
                bolt.enabled = false;

                bolts[i] = bolt;
            }

            StunEffect effect = root.AddComponent<StunEffect>();
            SetObject(effect, "player", player);
            SetList(effect, "bolts", System.Array.ConvertAll(bolts, b => (Object)b));
        }

        /// <summary>
        /// ワープの着地点に出す薄い輪。
        /// 着地点はプレイヤーの回転から離れた場所に出すので、プレイヤーの子にはしない。
        /// </summary>
        private static void BuildBlinkIndicator(Scene scene, PlayerController player, Material material, int layer)
        {
            GameObject go = NewGameObject(scene, $"BlinkTarget_{player.PlayerIndex + 1}P");
            if (layer >= 0) go.layer = layer;

            LineRenderer ring = go.AddComponent<LineRenderer>();
            ring.sharedMaterial = material;
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.alignment = LineAlignment.TransformZ;
            ring.widthMultiplier = 0.12f;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.enabled = false;

            BlinkTargetIndicator indicator = go.AddComponent<BlinkTargetIndicator>();
            SetObject(indicator, "player", player);
            SetObject(indicator, "ring", ring);
        }

        /// <summary>
        /// 名前でレイヤーを引き、無ければ空きスロットへ登録して番号を返す。
        /// ProjectSettings/TagManager.asset を書き換える点に注意。
        /// </summary>
        private static int EnsureLayer(string layerName)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return -1;

            SerializedObject tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");

            // 0〜7 はUnity予約なので 8 以降を見る
            for (int i = 8; i < layers.arraySize; i++)
            {
                if (layers.GetArrayElementAtIndex(i).stringValue == layerName) return i;
            }

            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue)) continue;

                slot.stringValue = layerName;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                return i;
            }

            Debug.LogWarning($"[MagicHand] レイヤーの空きがありません: {layerName}");
            return -1;
        }

        private static void ConfigurePlayerInput(GameObject root)
        {
            PlayerInput input = root.AddComponent<PlayerInput>();
            SerializedObject so = new SerializedObject(input);

            so.FindProperty("m_DefaultActionMap").stringValue = PlayerController.GameplayMap;
            // 既定のコントロールスキームを空のままにしておくと、Unity側の通常の自動ペアリング
            // （OnEnable内）が一度も走らず、PlayerInputのInputUserが未初期化のまま残ることがある。
            // その状態で外部から SwitchCurrentControlScheme を呼んで初めてペアリングさせようとすると
            // "Invalid user" 例外で失敗する（実測で2人のうちどちらか片方に必ず起きることを確認済み）。
            // Keyboardを既定にしておけば、Unity自身の通常の自動ペアリングでInputUserが先に
            // 初期化される。ControllerPriorityAssignerは起動直後にこれを上書きするので、
            // 実際にキーボードで固定されるわけではない
            so.FindProperty("m_DefaultControlScheme").stringValue = "Keyboard";
            so.FindProperty("m_NotificationBehavior").enumValueIndex = (int)PlayerNotifications.SendMessages;
            so.FindProperty("m_NeverAutoSwitchControlSchemes").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---- キャラクターモデル ---------------------------------------------

        private const string CharacterRoot = "Assets/Shokubutsu Studio/Free Low Poly Cubic Humans";
        private const string CharacterAnimations = CharacterRoot + "/Animations";
        private const string CharacterTexture = CharacterRoot + "/Textures/Texture_A.png";
        private const string CharacterMaterial = CharacterRoot + "/URP/Materials/Texture_A URP.mat";

        /// <summary>
        /// 画素が肌かどうかを、色相・彩度・明度で判定する。
        ///
        /// 最初は肌色との距離で見ていたが、テクスチャの色は肌から布まで連続していて
        /// 距離では切れ目が無く、灰色の布（201,201,201）まで肌に混ざってしまった。
        /// 肌だけが持つ特徴は「オレンジ寄りの色相・ほどよい彩度・明るい」の3つで、
        /// 灰色（彩度0）や茶色の靴・木（明度0.5未満）はこれで確実に外れる。
        /// </summary>
        private static bool IsSkinPixel(Color32 c)
        {
            Color.RGBToHSV(new Color(c.r / 255f, c.g / 255f, c.b / 255f), out float h, out float s, out float v);

            float hue = h * 360f;
            return hue >= 5f && hue <= 35f    // オレンジ寄り。紫のローブや青い服は外れる
                   && s >= 0.10f && s <= 0.55f // 無彩色の布と、原色の布を除く
                   && v >= 0.55f;              // 茶色の靴・木・髪を除く
        }

        /// <summary>
        /// キャラクターのテクスチャから、肌はそのままに衣装だけを手の色へ置き換えた画像を作る。
        ///
        /// 体全体が1メッシュ1マテリアルなので、色を乗算すると肌まで染まってしまう。
        /// そのため手ごとにテクスチャを作り分け、マテリアルごと差し替える方式を採っている。
        ///
        /// 衣装は元の色をいったん明度に落としてから色を乗せる。
        /// Mage_02 のように元から青いローブでも、乗算では濁るだけで色が変わらないため。
        /// </summary>
        private static Texture2D CreateHandTintedTexture(HandType hand)
        {
            string path = $"{GameRoot}/Textures/Texture_A_{hand}.png";
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            // Textures フォルダは初回生成時にはまだ無い。
            // AssetDatabase 経由ではなく File.WriteAllBytes で書くため、先に実体を作っておく
            if (!AssetDatabase.IsValidFolder($"{GameRoot}/Textures"))
            {
                AssetDatabase.CreateFolder(GameRoot, "Textures");
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(CharacterTexture);
            if (importer == null)
            {
                Debug.LogError($"[MagicHand] キャラクターのテクスチャが見つかりません: {CharacterTexture}");
                return null;
            }

            // 読み取りは生成時だけ必要なので、終わったら元の設定へ戻す
            bool wasReadable = importer.isReadable;
            TextureImporterCompression wasCompression = importer.textureCompression;

            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(CharacterTexture);
            Color32[] pixels = source.GetPixels32();
            Color tint = hand.ToColor();

            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 c = pixels[i];

                if (IsSkinPixel(c)) continue;   // 肌は触らない

                // 明度だけ残して色を差し替える
                float value = Mathf.Max(c.r, Mathf.Max(c.g, c.b)) / 255f;
                pixels[i] = new Color32(
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(tint.r * value) * 255f),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(tint.g * value) * 255f),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(tint.b * value) * 255f),
                    c.a);
            }

            Texture2D output = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            output.SetPixels32(pixels);
            output.Apply();

            System.IO.File.WriteAllBytes(path, output.EncodeToPNG());
            Object.DestroyImmediate(output);

            importer.isReadable = wasReadable;
            importer.textureCompression = wasCompression;
            importer.SaveAndReimport();

            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>手ごとのマテリアル4種（未選択＋グー・チョキ・パー）を用意する。</summary>
        private static Material[] CreateHandMaterials()
        {
            Material source = AssetDatabase.LoadAssetAtPath<Material>(CharacterMaterial);
            if (source == null)
            {
                Debug.LogError($"[MagicHand] キャラクターのマテリアルが見つかりません: {CharacterMaterial}");
                return new Material[0];
            }

            HandType[] hands = { HandType.None, HandType.Gu, HandType.Choki, HandType.Pa };
            var materials = new Material[hands.Length];

            for (int i = 0; i < hands.Length; i++)
            {
                // 未選択のときは元の見た目のまま
                if (hands[i] == HandType.None)
                {
                    materials[i] = source;
                    continue;
                }

                string path = $"{GameRoot}/Materials/M_Character_{hands[i]}.mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (material == null)
                {
                    material = new Material(source);
                    AssetDatabase.CreateAsset(material, path);
                }

                Texture2D tinted = CreateHandTintedTexture(hands[i]);
                if (tinted != null) material.SetTexture("_BaseMap", tinted);

                EditorUtility.SetDirty(material);
                materials[i] = material;
            }

            return materials;
        }

        /// <summary>
        /// 素手と杖持ちの2系統の移動モーションを Armed で切り替え、被弾モーションを重ねた
        /// AnimatorController を生成する。付属の CLP Controller は武器構えの遷移が前提なので使わない。
        ///
        /// 杖持ちには魔法使い（Staff/Mage_*）ではなく槍・長柄（Spear and Halberd/*）のクリップを使う。
        /// 魔法使いの走りは杖を掲げる前提で腕を高く上げてしまい（腕の高さ 1.14〜1.29）、
        /// 立てて持たせている杖と噛み合わなかった。
        /// 長柄のクリップは待機・歩き・走りのすべてで腕が 0.47〜0.61 に収まり、
        /// 長い柄を体の横に立てて持ったまま走る見た目になる。
        ///
        /// 素手の待機クリップだけはアセットに存在しないため、魔法使いの待機で代用している。
        /// 立ち止まっているときは腕の差が出にくく、破綻が目立たないため。
        /// </summary>
        private static AnimatorController CreatePlayerAnimatorController()
        {
            string path = $"{GameRoot}/Animations/PlayerAnimator.controller";
            AnimatorController existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (existing != null) return existing;

            AnimationClip freeIdle = LoadClip("Staff/Mage_Idle");
            AnimationClip staffIdle = LoadClip("Spear and Halberd/Spear_Halberd_Idle");
            AnimationClip staffWalk = LoadClip("Spear and Halberd/Spear_Halberd_Walk");
            AnimationClip staffRun = LoadClip("Spear and Halberd/Spear_Halberd_Run");
            AnimationClip freeWalk = LoadClip("NPC/NPC_Walk");
            AnimationClip freeRun = LoadClip("NPC/NPC_Run");
            AnimationClip hit = LoadClip("Hit/Damage_Hit_01");

            if (freeIdle == null || staffIdle == null || staffWalk == null || staffRun == null)
            {
                Debug.LogError("[MagicHand] キャラクターのアニメーションクリップが見つかりません");
                return null;
            }

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Armed", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Flying", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Taunt", AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            AnimatorState unarmed = CreateLocomotionState(controller, "Locomotion_Unarmed",
                                                          freeIdle, freeWalk ?? staffWalk, freeRun ?? staffRun);
            AnimatorState armed = CreateLocomotionState(controller, "Locomotion_Armed",
                                                        staffIdle, staffWalk, staffRun);

            machine.defaultState = unarmed;

            AnimatorStateTransition toArmed = unarmed.AddTransition(armed);
            toArmed.AddCondition(AnimatorConditionMode.If, 0f, "Armed");
            toArmed.hasExitTime = false;
            toArmed.duration = 0.15f;

            AnimatorStateTransition toUnarmed = armed.AddTransition(unarmed);
            toUnarmed.AddCondition(AnimatorConditionMode.IfNot, 0f, "Armed");
            toUnarmed.hasExitTime = false;
            toUnarmed.duration = 0.15f;

            AnimationClip ride = CreateBroomRideClip();
            if (ride != null)
            {
                AnimatorState flying = machine.AddState("Flying");
                flying.motion = ride;

                // どの状態からでも即座に乗る。走行中に発動しても走りモーションを引きずらない
                AnimatorStateTransition toFlying = machine.AddAnyStateTransition(flying);
                toFlying.AddCondition(AnimatorConditionMode.If, 0f, "Flying");
                toFlying.hasExitTime = false;
                toFlying.duration = 0.12f;
                toFlying.canTransitionToSelf = false;

                // 着地時にはほうきを使い切っていて Armed も false なので素手へ戻す
                AnimatorStateTransition fromFlying = flying.AddTransition(unarmed);
                fromFlying.AddCondition(AnimatorConditionMode.IfNot, 0f, "Flying");
                fromFlying.hasExitTime = false;
                fromFlying.duration = 0.18f;
            }

            if (hit != null)
            {
                AnimatorState hitState = machine.AddState("Hit");
                hitState.motion = hit;

                AnimatorStateTransition toHit = machine.AddAnyStateTransition(hitState);
                toHit.AddCondition(AnimatorConditionMode.If, 0f, "Hit");
                toHit.duration = 0.08f;
                toHit.canTransitionToSelf = false;

                AnimatorStateTransition back = hitState.AddTransition(unarmed);
                back.hasExitTime = true;
                back.exitTime = 0.9f;
                back.duration = 0.15f;
            }

            AnimationClip taunt = LoadTauntClip();
            if (taunt != null)
            {
                AnimatorState tauntState = machine.AddState("Taunt");
                tauntState.motion = taunt;

                AnimatorStateTransition toTaunt = machine.AddAnyStateTransition(tauntState);
                toTaunt.AddCondition(AnimatorConditionMode.If, 0f, "Taunt");
                toTaunt.hasExitTime = false;
                toTaunt.duration = 0.08f;
                toTaunt.canTransitionToSelf = false;

                AnimatorStateTransition backFromTaunt = tauntState.AddTransition(unarmed);
                backFromTaunt.hasExitTime = true;
                backFromTaunt.exitTime = 0.9f;
                backFromTaunt.duration = 0.15f;
            }

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private const string TauntClipPath = GameRoot + "/aoriemo-------------to/emo-toAnimation.anim";

        /// <summary>
        /// 煽りエモートのクリップを読み込む。
        ///
        /// 「再生中にキャラの位置が動いて見える」不具合は、クリップのボーンカーブではなく
        /// 単に**移動が止まっていなかったこと**が原因だった（実測でルートボーン・胸のボーン
        /// いずれも位置カーブに実質的なズレが無いことを確認済み）。エモート中にプレイヤーが
        /// 移動入力を続けると、身振りのポーズのままキャラが物理的に滑っていくため「動いた」ように
        /// 見えていた。対処は<see cref="PlayerController.IsTaunting"/>で
        /// <see cref="PlayerController.CanAct"/>を止めること（<see cref="PlayerTauntController"/>側）。
        /// </summary>
        private static AnimationClip LoadTauntClip()
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(TauntClipPath);
            if (clip == null)
            {
                Debug.LogWarning($"[MagicHand] 煽りエモートのクリップが見つかりません: {TauntClipPath}");
            }

            return clip;
        }

        private const string MagePrefabPath = CharacterRoot + "/URP/Prefabs/Characters/Mages/Mage_01.prefab";

        /// <summary>体を進行方向に対して何度ひねって横座りにするか。</summary>
        private const float BroomSideSaddleTurn = 90f;

        /// <summary>
        /// ほうきの柄が通る高さ（キャラの足元から）。
        /// ローブの裾が y=0.48 なので、そのすぐ下に柄を通して腰かけて見せる。
        /// 低くすると体と柄の間に隙間が空き、立ったまま柄が足元にあるだけの絵になる。
        /// </summary>
        private const float BroomSeatHeight = 0.44f;

        /// <summary>柄に置く手の、柄からの前後の間隔。</summary>
        private const float BroomGripSpacing = 0.32f;

        /// <summary>体の正面側（横座りなので進行方向から見て真横）へ足を垂らす距離。</summary>
        private const float BroomFootForward = 0.30f;

        /// <summary>
        /// ほうきに横座りした姿勢のクリップを1つ作る。アセットに乗り物のモーションが無いため自作する。
        ///
        /// 土台は長柄（Spear_Halberd_Idle）の待機姿勢を借りる。
        /// 腕を下ろして柄を握った形が既に出来ているので、そこから体をひねるだけで済む。
        /// 角度をゼロから指定する方式は失敗した。このモデルの休止姿勢は**腕が真横に伸びたTポーズ**で、
        /// 左右軸まわりに回しても腕が自分の軸で回るだけで下りてこない。
        /// 脚もローブと一体で、動かせるのは足先の塊だけしかない。
        ///
        /// 姿勢は1ポーズだけ。上昇・下降・旋回の手応えは PlayerBroomVisual が
        /// キャラごと傾けて出すので、ここでは「乗っている形」だけを作れば足りる。
        /// </summary>
        private static AnimationClip CreateBroomRideClip()
        {
            string path = $"{GameRoot}/Animations/Broom_Ride.anim";
            AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null) return existing;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MagePrefabPath);
            AnimationClip basePose = LoadClip("Spear and Halberd/Spear_Halberd_Idle");

            if (prefab == null || basePose == null)
            {
                Debug.LogError("[MagicHand] 搭乗モーションの土台にするプレハブかクリップが見つかりません");
                return null;
            }

            GameObject posed = (GameObject)Object.Instantiate(prefab);
            posed.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            try
            {
                basePose.SampleAnimation(posed, 0f);
                ArrangeSideSaddlePose(posed.transform);

                var clip = new AnimationClip { frameRate = 30f };

                foreach (string bonePath in RideBonePaths)
                {
                    CaptureBone(clip, posed.transform, bonePath);
                }

                clip.EnsureQuaternionContinuity();

                AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);

                AssetDatabase.CreateAsset(clip, path);
                return clip;
            }
            finally
            {
                Object.DestroyImmediate(posed);
            }
        }

        private static readonly string[] RideBonePaths =
        {
            "Main_Rig",
            "Main_Rig/Spine00",
            "Main_Rig/Spine00/Head",
            "Main_Rig/Spine00/Arm_L",
            "Main_Rig/Spine00/Arm_R",
            "Main_Rig/Foot_L",
            "Main_Rig/Foot_R"
        };

        /// <summary>
        /// 土台の姿勢を取ったモデルを、実際に横座りへ組み替える。
        ///
        /// 角度を計算で当てるのはやめて、モデルを本当に動かしてから丸ごと写し取る方式にしている。
        /// このリグはボーンごとにローカル軸の向きがばらばらなうえ、
        /// クリップがボーンの位置まで動かすので、角度指定だけでは狙った形にならない。
        /// 逆に位置を動かせるということは、**手や足の置き場所をワールド座標で直に指定できる**ということで、
        /// 柄の上に手を乗せるような細かい調整はそちらの方がはるかに扱いやすい。
        /// </summary>
        private static void ArrangeSideSaddlePose(Transform model)
        {
            Transform mainRig = model.Find("Main_Rig");
            Transform head = model.Find("Main_Rig/Spine00/Head");
            Transform armL = model.Find("Main_Rig/Spine00/Arm_L");
            Transform armR = model.Find("Main_Rig/Spine00/Arm_R");
            Transform footL = model.Find("Main_Rig/Foot_L");
            Transform footR = model.Find("Main_Rig/Foot_R");

            if (mainRig == null || head == null) return;

            // 体を横に向ける。顔は進行方向のままにしたいので、ひねる前の向きを控えておく
            Quaternion headFacing = head.rotation;
            mainRig.rotation = Quaternion.AngleAxis(BroomSideSaddleTurn, Vector3.up) * mainRig.rotation;
            head.rotation = headFacing;

            // 柄は進行方向(+Z)へ伸びているので、手は柄の上に前後に分けて置く
            float gripHeight = BroomSeatHeight + 0.06f;
            if (armL != null) armL.position = new Vector3(0f, gripHeight, BroomGripSpacing);
            if (armR != null) armR.position = new Vector3(0f, gripHeight, -BroomGripSpacing);

            // 足は体の正面側＝進行方向から見た横へ垂らす
            if (footL != null) footL.position = new Vector3(BroomFootForward, BroomSeatHeight - 0.10f, 0.13f);
            if (footR != null) footR.position = new Vector3(BroomFootForward, BroomSeatHeight - 0.14f, -0.13f);
        }

        /// <summary>組み終わったモデルから、1本のボーンの位置と向きをそのままクリップへ焼く。</summary>
        private static void CaptureBone(AnimationClip clip, Transform model, string bonePath)
        {
            Transform bone = model.Find(bonePath);
            if (bone == null)
            {
                Debug.LogWarning($"[MagicHand] 搭乗モーションのボーンが見つかりません: {bonePath}");
                return;
            }

            SetConstantRotationCurve(clip, bonePath, bone.localRotation);
            SetConstantPositionCurve(clip, bonePath, bone.localPosition);
        }

        /// <summary>1ポーズなので、始点と終点に同じ値を置いた定数カーブにする。</summary>
        private static void SetConstantRotationCurve(AnimationClip clip, string bonePath, Quaternion rotation)
        {
            const float length = 1f;

            clip.SetCurve(bonePath, typeof(Transform), "localRotation.x", ConstantCurve(rotation.x, length));
            clip.SetCurve(bonePath, typeof(Transform), "localRotation.y", ConstantCurve(rotation.y, length));
            clip.SetCurve(bonePath, typeof(Transform), "localRotation.z", ConstantCurve(rotation.z, length));
            clip.SetCurve(bonePath, typeof(Transform), "localRotation.w", ConstantCurve(rotation.w, length));
        }

        private static void SetConstantPositionCurve(AnimationClip clip, string bonePath, Vector3 position)
        {
            const float length = 1f;

            clip.SetCurve(bonePath, typeof(Transform), "localPosition.x", ConstantCurve(position.x, length));
            clip.SetCurve(bonePath, typeof(Transform), "localPosition.y", ConstantCurve(position.y, length));
            clip.SetCurve(bonePath, typeof(Transform), "localPosition.z", ConstantCurve(position.z, length));
        }

        private static AnimationCurve ConstantCurve(float value, float length)
            => AnimationCurve.Linear(0f, value, length, value);

        /// <summary>止まっている→歩き→走り を速度でつなぐブレンドツリーを1つ作る。</summary>
        private static AnimatorState CreateLocomotionState(AnimatorController controller, string name,
                                                           AnimationClip idle, AnimationClip walk, AnimationClip run)
        {
            AnimatorState state = controller.CreateBlendTreeInController(name, out BlendTree tree);
            tree.blendParameter = "Speed";
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, 0.45f);
            tree.AddChild(run, 1f);

            return state;
        }

        private static AnimationClip LoadClip(string relativePath)
            => AssetDatabase.LoadAssetAtPath<AnimationClip>($"{CharacterAnimations}/{relativePath}.anim");

        /// <summary>
        /// カプセルの代わりにキャラクターモデルを載せる。
        /// 当たり判定は今までどおりカプセルコライダーのままなので、挙動は一切変わらない。
        /// </summary>
        private static GameObject CreateCharacterVisual(Transform playerRoot, string prefabPath,
                                                        AnimatorController controller, PlayerController player)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (source == null)
            {
                Debug.LogError($"[MagicHand] キャラクタープレハブが見つかりません: {prefabPath}");
                return null;
            }

            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(source, playerRoot);
            visual.name = "Character";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;

            Animator animator = visual.GetComponent<Animator>();
            if (animator == null) animator = visual.AddComponent<Animator>();

            // モデル側のアバターを使う（FBX から生成済み）
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>($"{CharacterRoot}/Models/Characters/Mages/Mage_01.fbx");
            Animator modelAnimator = model != null ? model.GetComponent<Animator>() : null;
            if (modelAnimator != null) animator.avatar = modelAnimator.avatar;

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            PlayerAnimatorDriver driver = visual.AddComponent<PlayerAnimatorDriver>();
            SetObject(driver, "player", player);
            SetObject(driver, "body", player.GetComponent<Rigidbody>());
            SetObject(driver, "animator", animator);

            AttachStaff(visual.transform, player);

            return visual;
        }

        /// <summary>
        /// スクロール所持中だけ出す杖を用意する。
        ///
        /// 杖は腕のボーンの子にはしない。
        /// このリグの腕は肩から先が1本の棒（Arm_R）で、しかもボーンに 0.28 倍の
        /// スケールが入っている。子にすると杖が縮んだうえ、腕の振りに合わせて
        /// 2.3mの杖が地面を突き抜けたり空へ跳ね上がったりする。
        /// 位置合わせは実行時に PlayerStaffVisual が毎フレーム行い、
        /// ここでは寸法の実測値を渡すところまでを受け持つ。
        /// </summary>
        private static void AttachStaff(Transform characterRoot, PlayerController player)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>($"{CharacterRoot}/URP/Prefabs/Weapons/Staffs/Staff_01.prefab");
            if (source == null)
            {
                Debug.LogError("[MagicHand] 杖のプレハブが見つかりません");
                return;
            }

            Transform arm = FindDescendant(characterRoot, "Arm_R");
            if (arm == null)
            {
                Debug.LogError("[MagicHand] 杖を取り付ける腕のボーン(Arm_R)が見つかりません");
                return;
            }

            GameObject staff = (GameObject)PrefabUtility.InstantiatePrefab(source, characterRoot);
            staff.name = "Staff";
            staff.transform.localPosition = Vector3.zero;
            staff.transform.localRotation = Quaternion.identity;
            staff.transform.localScale = Vector3.one;

            foreach (Collider collider in staff.GetComponentsInChildren<Collider>())
            {
                Object.DestroyImmediate(collider);
            }

            // 制御役は杖ではなくキャラ側に置く。杖を非アクティブにすると
            // 杖に付けたコンポーネントごと止まり、二度と出せなくなるため
            PlayerStaffVisual visual = characterRoot.gameObject.AddComponent<PlayerStaffVisual>();
            SetObject(visual, "player", player);
            SetObject(visual, "staff", staff);
            SetObject(visual, "arm", arm);
            SetVector(visual, "pivotToBottom", CalculatePivotToBottom(staff.transform));
            SetFloat(visual, "groundClearance", StaffGroundClearance);
            SetFloat(visual, "forwardOffset", StaffForwardOffset);
            SetFloat(visual, "tiltForward", StaffTiltForward);
            SetFloat(visual, "tiltSide", StaffTiltSide);

            staff.SetActive(false);
        }


        /// <summary>
        /// ほうきを用意する。持ち歩きは杖と同じ持ち方、飛行中は股下へ移す。
        ///
        /// 杖と同じくキャラのルート直下に置く。腕のボーンの子にすると
        /// 0.28倍のスケールを継承して縮むうえ、腕の振りで振り回されてしまうため。
        /// </summary>
        private static void AttachBroom(Transform characterRoot, PlayerController player,
                                        Material handleMaterial, Material bristleMaterial)
        {
            Transform arm = FindDescendant(characterRoot, "Arm_R");
            if (arm == null)
            {
                Debug.LogError("[MagicHand] ほうきを持たせる腕のボーン(Arm_R)が見つかりません");
                return;
            }

            GameObject broom = CreateBroomModel(characterRoot, "Broom", handleMaterial, bristleMaterial);

            PlayerBroomVisual visual = characterRoot.gameObject.AddComponent<PlayerBroomVisual>();
            SetObject(visual, "player", player);
            SetObject(visual, "broom", broom);
            SetObject(visual, "arm", arm);
            SetVector(visual, "pivotToBottom", CalculatePivotToBottom(broom.transform));
            SetFloat(visual, "groundClearance", StaffGroundClearance);
            SetFloat(visual, "forwardOffset", StaffForwardOffset);
            SetFloat(visual, "tiltForward", StaffTiltForward);
            SetFloat(visual, "tiltSide", StaffTiltSide);
            // 柄の高さは搭乗ポーズと同じ値を使う。ずれると手が柄から浮く
            SetVector(visual, "ridePosition", new Vector3(0f, BroomSeatHeight, 0f));

            // 柄を前、穂を後ろへ。モデルは柄が+Yなので、X軸まわりに90度倒すと柄が+Z（前）を向く
            SetVector(visual, "rideRotation", new Vector3(90f, 0f, 0f));

            broom.SetActive(false);
        }

        /// <summary>
        /// 杖の原点から石突き（下端の中心）へ向かうベクトルを、杖のローカル座標で測る。
        ///
        /// 実行時はこれを使って「石突きを足元に合わせる」位置決めをする。
        /// 杖の原点は柄の途中にあり、モデルによって位置が違うので実測するしかない。
        /// </summary>
        private static Vector3 CalculatePivotToBottom(Transform item)
        {
            MeshFilter[] filters = item.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length == 0) return Vector3.down;

            // メッシュの bounds はそのメッシュ自身の座標系なので、
            // 持ち物のルートの座標系へ移してから囲む。
            // ほうきのように複数のパーツを組み合わせたモデルでは、
            // 各パーツの位置と回転を無視すると寸法が丸ごとずれる
            Bounds local = default;
            bool first = true;

            foreach (MeshFilter filter in filters)
            {
                if (filter.sharedMesh == null) continue;

                Matrix4x4 toItem = item.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Bounds mesh = filter.sharedMesh.bounds;

                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = toItem.MultiplyPoint3x4(new Vector3(
                        (corner & 1) == 0 ? mesh.min.x : mesh.max.x,
                        (corner & 2) == 0 ? mesh.min.y : mesh.max.y,
                        (corner & 4) == 0 ? mesh.min.z : mesh.max.z));

                    if (first)
                    {
                        local = new Bounds(point, Vector3.zero);
                        first = false;
                    }
                    else
                    {
                        local.Encapsulate(point);
                    }
                }
            }

            if (first) return Vector3.down;

            return new Vector3(local.center.x, local.min.y, local.center.z);
        }

        /// <summary>石突きを足元からどれだけ浮かせるか。走行時に地面へ潜るのを防ぐ。</summary>
        private const float StaffGroundClearance = 0.10f;

        private const float StaffForwardOffset = 0.12f;
        private const float StaffTiltForward = 12f;
        private const float StaffTiltSide = -6f;

        private static Transform FindDescendant(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) return t;
            }

            return null;
        }

        /// <summary>
        /// InputActionAsset の参照付けは全アセット生成が終わったあと最後に行う。
        /// 途中でアセットが再インポートされると、先に取得したインスタンス参照が無効になり
        /// 代入しても null として保存されてしまうため。
        ///
        /// 1P・2Pに同じ InputActionAsset を割り当てると、Unity が再生時に自動で複製
        /// （"(Clone)"）を作って2人目に使わせる。この自動複製された側だけ
        /// PlayerInput.SwitchCurrentControlScheme が "Invalid user" 例外で毎回失敗することを
        /// 実測で確認した（`ControllerPriorityAssigner`、失敗するのは必ず複製された側）。
        /// 2P用に最初から別の永続アセット（ファイルを複製したもの）を持たせることで、
        /// 実行時の自動複製そのものを起こさせないようにする
        /// </summary>
        private static void AssignInputActions(params PlayerController[] players)
        {
            string path = AssetDatabase.GUIDToAssetPath(AssetDatabase.AssetPathToGUID(InputActionsPath));
            InputActionAsset primaryActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);

            if (primaryActions == null)
            {
                Debug.LogError($"[MagicHand] Input Actions を解決できませんでした: {InputActionsPath}");
                return;
            }

            string secondaryPath = $"{GameRoot}/Input/MagicHandControls_P2.inputactions";
            if (AssetDatabase.LoadAssetAtPath<InputActionAsset>(secondaryPath) == null)
            {
                AssetDatabase.CopyAsset(path, secondaryPath);
                AssetDatabase.Refresh();
            }

            InputActionAsset secondaryActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(secondaryPath);
            if (secondaryActions == null)
            {
                Debug.LogError($"[MagicHand] 2P用のInput Actions複製に失敗しました: {secondaryPath}");
                return;
            }

            for (int i = 0; i < players.Length; i++)
            {
                PlayerController player = players[i];
                InputActionAsset actions = i == 0 ? primaryActions : secondaryActions;

                PlayerInput input = player.GetComponent<PlayerInput>();
                SerializedObject so = new SerializedObject(input);
                so.FindProperty("m_Actions").objectReferenceValue = actions;
                so.ApplyModifiedPropertiesWithoutUndo();

                bool assigned = new SerializedObject(input).FindProperty("m_Actions").objectReferenceValue != null;
                if (!assigned)
                {
                    Debug.LogError($"[MagicHand] {player.name} の PlayerInput に Actions を割り当てられませんでした。");
                }
            }
        }

        // ---- UI -------------------------------------------------------------

        private static void BuildPlayerUI(Scene scene, int index, PlayerController player, Camera camera,
                                          List<ItemDefinitionSO> scrolls)
        {
            GameObject canvasGo = NewGameObject(scene, $"UI_P{index + 1}");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            // ThirdPersonCameraRig.minDistance（既定3）より必ず小さくする。
            // でないとリスポーン直後などカメラが障害物で寄った瞬間にキャラがUIより手前に描画されてしまう。
            canvas.planeDistance = 2f;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(960f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // --- HUD ---
            GameObject hud = NewUIChild("HUDPanel", canvasGo.transform, Vector2.zero, Vector2.one);

            Text timer = CreateText(hud.transform, "TimerText", "3:00", 64, TextAnchor.UpperCenter,
                                    new Vector2(0.16f, 0.84f), new Vector2(0.84f, 0.99f));

            // 得点は「画面の境（＝分割画面の仕切り線）」側へ寄せる。
            // 1Pは左半分なので境は自分の右端、2Pは右半分なので境は自分の左端。
            // 両者の得点表示が画面中央で隣り合うので、一目で自分と相手の差が分かる。
            // 境ぎりぎりまで詰めると仕切りの「-」に数字がくっついて見えるので、少し余白を空ける
            bool isLeftHalf = index == 0;
            TextAnchor scoreAnchor = isLeftHalf ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            Vector2 scoreMin = isLeftHalf ? new Vector2(0.28f, 0.80f) : new Vector2(0.04f, 0.80f);
            Vector2 scoreMax = isLeftHalf ? new Vector2(0.96f, 0.94f) : new Vector2(0.72f, 0.94f);
            Text score = CreateText(hud.transform, "ScoreText", "YOU 0 - 0 RIVAL", 64, scoreAnchor,
                                    scoreMin, scoreMax);
            // 手とアイテムは試合中いちばん見る情報なので、分割画面でも読める大きさを取る
            BuildHandDisplay(hud.transform, out Image handFrame, out Image handIcon, out Text hand);
            BuildItemBox(hud.transform, out Image itemFrame, out Image itemIcon, out Text itemName, out Text itemUnusableMark);
            Text[] statusRows = BuildStatusRows(hud.transform);

            // 飛行と滑空は残り時間で表せないので効果一覧とは別に出す
            // 効果一覧は下から 0.22 に 0.075 刻みで4行ぶん積まれるので、その上に置く
            Text flight = CreateText(hud.transform, "FlightText", string.Empty, 44, TextAnchor.LowerRight,
                                     new Vector2(0.14f, 0.53f), new Vector2(0.97f, 0.60f));
            flight.color = new Color(1f, 0.72f, 0.35f);
            flight.enabled = false;

            // 負けた瞬間は視点ごと吹き飛ばされて、当たったことに気づきにくい。
            // 画面の中央に大きく出して、何が起きたかを伝える
            Text defeat = CreateText(hud.transform, "DefeatText", "負けてしまった", 72, TextAnchor.MiddleCenter,
                                     new Vector2(0.02f, 0.58f), new Vector2(0.98f, 0.72f));
            defeat.color = new Color(1f, 0.4f, 0.4f);
            AddTextOutline(defeat);
            defeat.enabled = false;

            Text countdown = CreateText(hud.transform, "CountdownText", "3", 140, TextAnchor.MiddleCenter,
                                        new Vector2(0.1f, 0.35f), new Vector2(0.9f, 0.7f));
            countdown.gameObject.SetActive(false);

            // 残り1分／残り10秒になった瞬間だけ、画面中心に大きく出す通知。
            // Selectionの数字カウントダウン（CountdownText）とは出るタイミングが重ならないので、
            // ほぼ同じ位置に置いてよい
            Text timeAnnounce = CreateText(hud.transform, "TimeAnnounceText", string.Empty, 64, TextAnchor.MiddleCenter,
                                           new Vector2(0.06f, 0.40f), new Vector2(0.94f, 0.56f));
            AddTextOutline(timeAnnounce);
            timeAnnounce.enabled = false;

            Text playerLabel = CreateText(hud.transform, "PlayerLabel", $"{index + 1}P", 40, TextAnchor.UpperRight,
                                          new Vector2(0.7f, 0.9f), new Vector2(0.97f, 0.99f));
            playerLabel.color = index == 0 ? new Color(1f, 0.85f, 0.4f) : new Color(0.5f, 0.85f, 1f);

            BuildOffscreenArrow(hud.transform, player, camera);

            InGameHUD hudComponent = canvasGo.AddComponent<InGameHUD>();
            SetObject(hudComponent, "player", player);
            SetObject(hudComponent, "panel", hud);
            SetObject(hudComponent, "timerText", timer);
            SetObject(hudComponent, "scoreText", score);
            SetObject(hudComponent, "handText", hand);
            SetObject(hudComponent, "handFrame", handFrame);
            SetObject(hudComponent, "handIcon", handIcon);
            SetObject(hudComponent, "guIcon", RenderHandIcon("Icon_HandGu", GuVisualPrefabPath));
            SetObject(hudComponent, "chokiIcon", RenderHandIcon("Icon_HandChoki", ChokiVisualPrefabPath));
            // パーの本は正面から撮ると縦長（背表紙が上）になるので、90度ロールして横向きに見せる
            SetObject(hudComponent, "paIcon", RenderHandIcon("Icon_HandPa", PaVisualPrefabPath, 90f));
            SetObject(hudComponent, "itemFrame", itemFrame);
            SetObject(hudComponent, "itemIcon", itemIcon);
            SetObject(hudComponent, "itemName", itemName);
            SetObject(hudComponent, "itemUnusableMark", itemUnusableMark);
            SetList(hudComponent, "statusRows", System.Array.ConvertAll(statusRows, t => (Object)t));
            SetObject(hudComponent, "countdownText", countdown);
            SetObject(hudComponent, "timeAnnounceText", timeAnnounce);
            SetObject(hudComponent, "flightText", flight);
            SetObject(hudComponent, "defeatText", defeat);

            // --- 手選択 ---
            GameObject selection = NewUIChild("SelectionPanel", canvasGo.transform, Vector2.zero, Vector2.one);
            AddBackground(selection, new Color(0.03f, 0.03f, 0.05f, 0.88f));

            CreateText(selection.transform, "Title", "手を選べ！", 52, TextAnchor.MiddleCenter,
                       new Vector2(0.05f, 0.82f), new Vector2(0.95f, 0.93f));

            // 制限時間を切るとランダムで決まる。何も出さずに勝手に決まると理不尽なので必ず見せる。
            // カードは 0.70 から下へ並ぶので、タイトルとの間に置く
            Text remaining = CreateText(selection.transform, "RemainingText", "残り 5.0", 44, TextAnchor.MiddleCenter,
                                        new Vector2(0.05f, 0.72f), new Vector2(0.95f, 0.81f));

            CreateText(selection.transform, "Hint", "十字キーで選択　／　✕/A で決定　／　時間切れはランダム", 24,
                       TextAnchor.MiddleCenter, new Vector2(0.03f, 0.1f), new Vector2(0.97f, 0.18f));

            MagicHand.HandType[] order = { MagicHand.HandType.Gu, MagicHand.HandType.Choki, MagicHand.HandType.Pa };
            var backgrounds = new Object[order.Length];
            var outlines = new Object[order.Length];

            for (int i = 0; i < order.Length; i++)
            {
                float top = 0.70f - i * 0.17f;
                CreateHandChoiceCard(selection.transform, order[i],
                                     new Vector2(0.22f, top - 0.13f), new Vector2(0.78f, top),
                                     out Image background, out Outline outline);

                backgrounds[i] = background;
                outlines[i] = outline;
            }

            SelectionUI selectionUI = canvasGo.AddComponent<SelectionUI>();
            SetObject(selectionUI, "player", player);
            SetObject(selectionUI, "panel", selection);
            SetObject(selectionUI, "remainingText", remaining);
            SetList(selectionUI, "cardBackgrounds", backgrounds);
            SetList(selectionUI, "cardOutlines", outlines);
            selection.SetActive(false);

            BuildOptionsPanel(canvasGo.transform, player, scrolls);
        }

        /// <summary>
        /// 試合中に Start / Esc で開く感度調整。開いた本人の画面にだけ出る。
        /// 試合は止めないので、視界を塞がないよう画面隅の小さな板に留める。
        ///
        /// デバッグパネルはこれとは別の板として同じ隅に用意する。
        /// オプションを開いている間はデバッグパネルを隠すので、
        /// 同じ十字キー入力を2枚のパネルが取り合うことはない（InGameOptionsMenu側で排他制御）。
        /// </summary>
        private static void BuildOptionsPanel(Transform canvas, PlayerController player, List<ItemDefinitionSO> scrolls)
        {
            GameObject options = NewUIChild("OptionsPanel", canvas, new Vector2(0.04f, 0.55f), new Vector2(0.62f, 0.80f));
            AddBackground(options, new Color(0.03f, 0.03f, 0.05f, 0.72f));

            Text sensitivity = CreateText(options.transform, "SensitivityRow", string.Empty, 22,
                                          TextAnchor.MiddleLeft, new Vector2(0.05f, 0.81f), new Vector2(0.98f, 0.97f));
            Text invert = CreateText(options.transform, "InvertRow", string.Empty, 22,
                                     TextAnchor.MiddleLeft, new Vector2(0.05f, 0.65f), new Vector2(0.98f, 0.81f));
            Text fov = CreateText(options.transform, "FovRow", string.Empty, 22,
                                  TextAnchor.MiddleLeft, new Vector2(0.05f, 0.49f), new Vector2(0.98f, 0.65f));
            Text controlsRow = CreateText(options.transform, "ControlsRow", string.Empty, 22,
                                          TextAnchor.MiddleLeft, new Vector2(0.05f, 0.33f), new Vector2(0.98f, 0.49f));
            Text endGameRow = CreateText(options.transform, "EndGameRow", string.Empty, 22,
                                         TextAnchor.MiddleLeft, new Vector2(0.05f, 0.17f), new Vector2(0.98f, 0.33f));
            CreateText(options.transform, "Hint", "十字キー: 上下=項目 左右=変更・開閉 / Start で閉じる", 16,
                       TextAnchor.MiddleLeft, new Vector2(0.05f, 0.02f), new Vector2(0.98f, 0.15f));

            // 内容は準備ルームの操作説明と同じ表をそのまま流用する
            GameObject controlsPanel = BuildControlsHelp(canvas);

            // --- デバッグパネル（クリエイティブ飛行1行＋巻物5種の付与＝6行） ---
            GameObject debugPanel = NewUIChild("DebugPanel", canvas, new Vector2(0.04f, 0.30f), new Vector2(0.62f, 0.80f));
            AddBackground(debugPanel, new Color(0.06f, 0.02f, 0.02f, 0.72f));

            const int debugRowCount = 6;
            var debugRowTexts = new Text[debugRowCount];
            const float debugTop = 0.95f;
            const float debugBottom = 0.05f;
            float debugRowHeight = (debugTop - debugBottom) / debugRowCount;

            for (int i = 0; i < debugRowCount; i++)
            {
                float rowTop = debugTop - i * debugRowHeight;
                debugRowTexts[i] = CreateText(debugPanel.transform, $"Row_{i}", string.Empty, 20, TextAnchor.MiddleLeft,
                                              new Vector2(0.05f, rowTop - debugRowHeight), new Vector2(0.98f, rowTop));
            }

            var grantable = new List<ScrollEffectSO>();
            foreach (ItemDefinitionSO item in scrolls)
            {
                if (item is ScrollEffectSO effect) grantable.Add(effect);
            }

            // 行0=クリエイティブ飛行、行1〜5=巻物5種の付与
            var grantTexts = new Text[grantable.Count];
            for (int i = 0; i < grantTexts.Length && i + 1 < debugRowTexts.Length; i++)
            {
                grantTexts[i] = debugRowTexts[i + 1];
            }

            InGameOptionsMenu menu = player.gameObject.AddComponent<InGameOptionsMenu>();
            SetObject(menu, "player", player);
            SetObject(menu, "playerInput", player.GetComponent<PlayerInput>());
            SetObject(menu, "panel", options);
            SetObject(menu, "sensitivityText", sensitivity);
            SetObject(menu, "invertText", invert);
            SetObject(menu, "fovText", fov);
            SetObject(menu, "controlsText", controlsRow);
            SetObject(menu, "endGameText", endGameRow);
            SetObject(menu, "controlsPanel", controlsPanel);
            SetObject(menu, "debugPanel", debugPanel);
            SetObject(menu, "creativeFlightText", debugRowTexts[0]);
            SetList(menu, "grantItemTexts", System.Array.ConvertAll(grantTexts, t => (Object)t));
            SetList(menu, "grantableScrolls", grantable.ToArray());

            options.SetActive(false);
            debugPanel.SetActive(false);

            // オプション/デバッグパネルが開いていない間だけ、十字キー下で煽りエモートを再生する
            PlayerTauntController taunt = player.gameObject.AddComponent<PlayerTauntController>();
            SetObject(taunt, "player", player);
            SetObject(taunt, "optionsMenu", menu);
            SetObject(taunt, "animator", player.GetComponentInChildren<Animator>());
        }

        /// <summary>
        /// 手選択パネルの縦並びカード1枚。
        /// 選択中の強調（不透明・拡大・縁取り）は SelectionUI が実行時に切り替えるので、
        /// ここでは切り替え対象の Image と Outline を作って返すところまでを担う。
        /// </summary>
        private static void CreateHandChoiceCard(Transform parent, MagicHand.HandType hand,
                                                 Vector2 anchorMin, Vector2 anchorMax,
                                                 out Image background, out Outline outline)
        {
            GameObject card = NewUIChild($"Choice_{hand}", parent, anchorMin, anchorMax);

            background = card.AddComponent<Image>();
            background.color = hand.ToColor();

            outline = card.AddComponent<Outline>();
            outline.effectColor = Color.white;
            outline.effectDistance = new Vector2(4f, -4f);
            outline.enabled = false;

            CreateText(card.transform, "Label", hand.ToLabel(), 40, TextAnchor.MiddleCenter,
                       Vector2.zero, Vector2.one).color = Color.black;
        }

        // ---- 準備ルーム -----------------------------------------------------

        /// <summary>準備ルームを組み立て、GameManager へ渡す参照をまとめて返す。</summary>
        private class LobbyRefs
        {
            public LobbyStartZone StartZone;
            public Camera Camera;
            public Transform[] SpawnPoints;
            public LobbySettingsPanel[] Panels;
        }

        /// <summary>
        /// アリーナから十分離れた場所に準備ルームを併設する。
        /// 別シーンにしないのは、シーンロードも設定値の受け渡しも要らず、
        /// State に応じてプレイヤーとカメラをワープさせるだけで済むため。
        /// </summary>
        private static LobbyRefs BuildLobby(Scene scene, Material floorMat, Material wallMat,
                                            Material platformMat, Material zoneMat,
                                            ItemPickup itemPrefab, List<ItemDefinitionSO> scrolls,
                                            int totalItemCount)
        {
            var refs = new LobbyRefs();

            GameObject root = NewGameObject(scene, "Lobby");
            root.transform.position = LobbyOrigin;

            const float half = LobbyHalfSize;

            // 床は壁(±14)より広く敷く。
            //
            // 俯瞰カメラは南の壁の外側から見下ろしていて、その壁は描画を切ってある。
            // 部屋ぴったりの床だと、画面の下端と左右の隅で床が尽きて背景の空が見えてしまう。
            // 実測でレイは x±30 / z-14 まで届くので、そこを覆う大きさにしてある。
            // 広げたぶんは壁の外側なので、プレイヤーがそこへ立つことはない
            const float floorHalf = 34f;

            GameObject floor = CreatePrimitive(PrimitiveType.Plane, "LobbyFloor", root.transform, floorMat);
            floor.transform.localPosition = Vector3.zero;
            floor.transform.localScale = new Vector3(floorHalf / 5f, 1f, floorHalf / 5f);

            // 引き伸ばした板にそのまま貼るとテクスチャも伸びるので、実寸に合わせて敷き直す。
            // Plane はスケール1のとき一辺10、床なので縦方向は奥行き(Z)を使う
            ApplyTiling(floor, 10f, false);

            const float wallHeight = LobbyWallHeight;
            const float wallThickness = 1f;
            CreateBox("LobbyWall_N", root.transform, wallMat, LobbyOrigin + new Vector3(0f, wallHeight / 2f, half), new Vector3(half * 2f, wallHeight, wallThickness));
            CreateBox("LobbyWall_E", root.transform, wallMat, LobbyOrigin + new Vector3(half, wallHeight / 2f, 0f), new Vector3(wallThickness, wallHeight, half * 2f));
            CreateBox("LobbyWall_W", root.transform, wallMat, LobbyOrigin + new Vector3(-half, wallHeight / 2f, 0f), new Vector3(wallThickness, wallHeight, half * 2f));

            // 手前(南)の壁だけ描かず、当たり判定だけ残す。
            // 俯瞰カメラは南の外側から見下ろすので、描くと高さ16mの壁が部屋を丸ごと隠してしまう。
            // 見えなくてもプレイヤーは出られないし、床(±14)の縁が壁と同じ位置にあるので、
            // どこまで歩けるかは床の切れ目で分かる。
            // 準備ルームを映すのはこの俯瞰カメラだけなので、無くなったことは他の角度からも見えない
            CreateInvisibleWall("LobbyWall_S", root.transform, wallMat,
                                LobbyOrigin + new Vector3(0f, wallHeight / 2f, -half),
                                new Vector3(half * 2f, wallHeight, wallThickness));

            BuildLobbyPractice(root.transform, platformMat);
            refs.StartZone = BuildLobbyStartZone(root.transform, zoneMat);
            refs.SpawnPoints = BuildLobbySpawnPoints(root.transform);
            List<ItemPickup> samples = BuildLobbySamples(root.transform, itemPrefab, scrolls);
            refs.Camera = BuildLobbyCamera(scene);
            refs.Panels = BuildLobbyUI(scene, refs.Camera, refs.StartZone, totalItemCount, samples);

            return refs;
        }

        /// <summary>
        /// ジャンプと登りの練習台。本編と同じ高さ・同じ勾配にしてあるので、
        /// 「この段差は飛べる／飛べない」を試合前に体で覚えられる。
        /// </summary>
        private static void BuildLobbyPractice(Transform parent, Material material)
        {
            GameObject group = NewChild("Practice", parent);

            const float thickness = SlabThickness;
            float lowTop = SecondFloorTop;    // 本編2階と同じ 5.30
            float lowCenter = lowTop - thickness / 2f;

            // 低い台（本編2階の高さ）と、地上から上がるスロープ。北西の一角にまとめる。
            //
            // 本編3階の高さ(10.30)の台も以前は置いていたが、俯瞰カメラの画角から
            // 上へはみ出して見えなかったため撤去した。高さを足すならカメラの引きも要る。
            CreateRamp("Lobby_RampLow", group.transform, material,
                       LobbyOrigin + new Vector3(-9f, 0f, -4f),
                       LobbyOrigin + new Vector3(-9f, lowTop, 9f), 6f, thickness);
            CreateBox("Lobby_PlatformLow", group.transform, material,
                      LobbyOrigin + new Vector3(-9f, lowCenter, 11.5f), new Vector3(8f, thickness, 5f));

            // ジャンプで越えられる高さの段差。中央に並べて手前から試せるようにする
            for (int i = 0; i < 3; i++)
            {
                float height = 1.2f + i * 0.5f;
                CreateBox($"Lobby_Step_{i}", group.transform, material,
                          LobbyOrigin + new Vector3(-1f + i * 4f, height / 2f, 4f),
                          new Vector3(2.6f, height, 2.6f));
            }
        }

        /// <summary>
        /// スタート地点は円形の平たい台座。人数に応じて色が変わるので、
        /// 説明を読まなくても乗るべき場所と揃ったかどうかが分かる。
        /// </summary>
        private static LobbyStartZone BuildLobbyStartZone(Transform parent, Material material)
        {
            const float radius = 3f;
            Vector3 center = LobbyOrigin + new Vector3(0f, 0f, -10f);

            // Cylinder プリミティブは直径1・高さ2 なので、半径ぶん広げて薄く潰す
            GameObject marker = CreatePrimitive(PrimitiveType.Cylinder, "StartZone", parent, material);
            marker.transform.position = center + Vector3.up * 0.05f;
            marker.transform.localScale = new Vector3(radius * 2f, 0.05f, radius * 2f);
            Object.DestroyImmediate(marker.GetComponent<Collider>());

            LobbyStartZone zone = marker.AddComponent<LobbyStartZone>();
            SetFloat(zone, "radius", radius);
            SetFloat(zone, "height", 4f);
            SetObject(zone, "targetRenderer", marker.GetComponent<Renderer>());

            return zone;
        }

        private static Transform[] BuildLobbySpawnPoints(Transform parent)
        {
            GameObject group = NewChild("LobbySpawnPoints", parent);
            var points = new Transform[2];

            // スタート地点（中央 z=-10 半径3）から外れた位置に湧かせる。
            // 入室した瞬間に円へ乗っていると、そのまま試合が始まってしまうため
            for (int i = 0; i < points.Length; i++)
            {
                GameObject go = NewChild($"LobbySpawn_{i + 1}P", group.transform);
                go.transform.position = LobbyOrigin + new Vector3(i == 0 ? -9f : -5f, 0.1f, -12f);
                go.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
                points[i] = go.transform;
            }

            return points;
        }

        /// <summary>
        /// アイテムの見本。種類ごとに場所が決まっていて、拾ってもすぐ復活するので
        /// 試したい効果を狙って何度でも確認できる。
        /// </summary>
        private static List<ItemPickup> BuildLobbySamples(Transform parent, ItemPickup prefab, List<ItemDefinitionSO> scrolls)
        {
            GameObject group = NewChild("ItemSamples", parent);

            // 見本はスクロールのみ。手変更アイテムは拾っても色が変わるだけで
            // 試して確かめる要素がなく、置いても場所と文字を消費するだけのため外している。
            // 練習スロープ（x -12〜-6）を避けて東寄りに並べる
            return PlaceSampleRow(group.transform, prefab, scrolls, -4f, 3.4f);
        }

        private static List<ItemPickup> PlaceSampleRow(Transform parent, ItemPickup prefab, List<ItemDefinitionSO> items,
                                                        float z, float spacing)
        {
            var placed = new List<ItemPickup>();
            if (prefab == null || items == null) return placed;

            float startX = 12f - (items.Count - 1) * spacing;

            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] == null) continue;

                Vector3 position = LobbyOrigin + new Vector3(startX + i * spacing, 0.4f, z);
                ItemPickup sample = Object.Instantiate(prefab, position, Quaternion.identity, parent);
                sample.name = $"Sample_{items[i].DisplayName}";

                SetObject(sample, "definition", items[i]);
                SetBool(sample, "respawnInPlace", true);
                SetFloat(sample, "respawnDelay", 1f);
                placed.Add(sample);

                // 何の見本なのかを名札で示す。ただし8枚が常時出ると文字だらけになるので、
                // 近づいたときだけ表示する
                Text nameplate = CreateWorldLabel(parent, $"Label_{items[i].DisplayName}",
                                                  items[i].DisplayName, position + new Vector3(0f, 1.6f, 0f),
                                                  items[i].DisplayColor);

                ProximityLabel proximity = nameplate.gameObject.AddComponent<ProximityLabel>();
                SetObject(proximity, "label", nameplate);
                nameplate.enabled = false;
            }

            return placed;
        }

        /// <summary>
        /// ワールド空間に置くテキスト。俯瞰カメラは固定なので、
        /// その俯角に合わせて一度傾けておけば常に正面から読める。
        /// </summary>
        private static Text CreateWorldLabel(Transform parent, string name, string content,
                                             Vector3 position, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(LobbyCameraPitch, 0f, 0f);

            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(240f, 90f);
            rect.localScale = Vector3.one * 0.02f;

            Text text = go.AddComponent<Text>();
            text.text = content;
            text.font = BuiltinFont();
            text.fontSize = 34;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            Outline outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(2f, -2f);

            return text;
        }

        /// <summary>見えないが通れない壁。描画だけ切って当たり判定は残す。</summary>
        private static void CreateInvisibleWall(string name, Transform parent, Material material,
                                                Vector3 center, Vector3 size)
        {
            GameObject wall = CreateBox(name, parent, material, center, size);

            MeshRenderer renderer = wall.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.enabled = false;
        }

        private static Camera BuildLobbyCamera(Scene scene)
        {
            // 位置は「注視点から俯角の方向へ距離ぶん引いた場所」で決める。
            // 高さで決めると、俯角を変えたときに見える大きさまで一緒に変わってしまうため。
            //
            // 俯角50度だとカメラは南の壁の外側へ出る。壁は描画を切ってあるので視界は塞がらない
            float pitch = LobbyCameraPitch * Mathf.Deg2Rad;
            float height = LobbyCameraDistance * Mathf.Sin(pitch);
            float backOffset = LobbyCameraDistance * Mathf.Cos(pitch);

            GameObject go = NewGameObject(scene, "Camera_Lobby");
            go.transform.position = LobbyOrigin + LobbyCameraFocus + new Vector3(0f, height, -backOffset);
            go.transform.rotation = Quaternion.Euler(LobbyCameraPitch, 0f, 0f);

            Camera camera = go.AddComponent<Camera>();
            camera.fieldOfView = 60f;
            camera.depth = 5f;
            camera.enabled = false;

            return camera;
        }

        /// <summary>左右に1P/2Pのパネルを並べる。共通設定は1P側にだけ載せる。</summary>
        private static LobbySettingsPanel[] BuildLobbyUI(Scene scene, Camera lobbyCamera, LobbyStartZone startZone,
                                                         int itemCount, List<ItemPickup> itemSamples)
        {
            GameObject canvasGo = NewGameObject(scene, "UI_Lobby");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = lobbyCamera;
            canvas.planeDistance = 2f;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            CreateText(canvasGo.transform, "LobbyHint",
                       "移動=左スティック　ジャンプ=✕/A　スクロール=□/X　メニュー=十字キー",
                       22, TextAnchor.LowerCenter, new Vector2(0.1f, 0.02f), new Vector2(0.9f, 0.07f));

            // 開始の合図。俯瞰カメラのCanvasにしか出せないのでここに置く
            Text countdown = CreateText(canvasGo.transform, "LobbyCountdown", "3", 150, TextAnchor.MiddleCenter,
                                        new Vector2(0.3f, 0.4f), new Vector2(0.7f, 0.75f));
            countdown.gameObject.SetActive(false);

            Text status = CreateText(canvasGo.transform, "LobbyStatus", string.Empty, 34, TextAnchor.MiddleCenter,
                                     new Vector2(0.25f, 0.12f), new Vector2(0.75f, 0.2f));

            // 今どちらのモードかを常に画面中央に大きく出す。
            // 設定パネルの小さな文字だけだと切り替え忘れに気付きにくいため。
            // 画面上部は俯瞰カメラに近い練習台の3Dモデルに隠れることがあるため、
            // 開始状況の表示（LobbyStatus）のすぐ上、画面中央のよく見える高さに置く
            Text mode = CreateText(canvasGo.transform, "LobbyMode", string.Empty, 36, TextAnchor.MiddleCenter,
                                   new Vector2(0.25f, 0.22f), new Vector2(0.75f, 0.30f));
            AddTextOutline(mode);

            LobbyHUD hud = canvasGo.AddComponent<LobbyHUD>();
            SetObject(hud, "startZone", startZone);
            SetObject(hud, "countdownText", countdown);
            SetObject(hud, "statusText", status);
            SetObject(hud, "modeText", mode);

            BuildItemDescriptionPanel(canvasGo.transform, itemSamples);

            GameObject controls = BuildControlsHelp(canvasGo.transform);

            // 中央のプレイ領域を広く残すため、パネルは幅20%に抑える（中央に60%残る）
            var panels = new LobbySettingsPanel[2];
            panels[0] = CreateSettingsPanel(canvasGo.transform, 0, true, itemCount,
                                            new Vector2(0.015f, 0.16f), new Vector2(0.21f, 0.97f));
            panels[1] = CreateSettingsPanel(canvasGo.transform, 1, false, itemCount,
                                            new Vector2(0.79f, 0.72f), new Vector2(0.985f, 0.97f));

            LobbyControlsHelp help = canvasGo.AddComponent<LobbyControlsHelp>();
            SetObject(help, "panel", controls);
            SetList(help, "settingsPanels", new Object[] { panels[0], panels[1] });

            return panels;
        }

        /// <summary>
        /// アイテムの見本に近づいたとき、画面の決まった位置に効果の説明を出す。
        /// 世界空間の名札は距離次第で大きさが変わってしまうため、
        /// 画面固定のUIとして別に用意し、常に読みやすい大きさで表示する。
        ///
        /// 1Pと2Pは別々のアイテムを見ていることが多いので、1枚にまとめず
        /// 画面の左（1P）・右（2P）に分けてそれぞれ独立に表示する。
        /// </summary>
        private static void BuildItemDescriptionPanel(Transform parent, List<ItemPickup> samples)
        {
            // 画面上寄りは俯瞰カメラに近い練習台の3Dモデルに隠れることがあるため、
            // LobbyMode（画面中央）のすぐ上、隠れないことを確認済みの高さに置く。
            // 説明文は折り返す前提で枠を広げていたが、短い説明文（スタン等）だと
            // 枠の下側が大きく空いて間延びして見えたため、必要な分だけに縮めた
            GameObject panelP1 = CreateItemDescriptionSide(parent, "ItemDescriptionPanel_1P", "1P",
                                                            new Vector2(0.02f, 0.325f), new Vector2(0.40f, 0.475f),
                                                            new Color(1f, 0.85f, 0.4f),
                                                            out Text nameP1, out Text descP1);
            GameObject panelP2 = CreateItemDescriptionSide(parent, "ItemDescriptionPanel_2P", "2P",
                                                            new Vector2(0.60f, 0.325f), new Vector2(0.98f, 0.475f),
                                                            new Color(0.5f, 0.85f, 1f),
                                                            out Text nameP2, out Text descP2);

            // 本体（parent）に付ける。パネル自身に付けると、非表示にした瞬間
            // コンポーネントごと止まってしまい、二度と自分で再表示できなくなるため
            ItemDescriptionDisplay display = parent.gameObject.AddComponent<ItemDescriptionDisplay>();
            SetList(display, "panels", new Object[] { panelP1, panelP2 });
            SetList(display, "nameTexts", new Object[] { nameP1, nameP2 });
            SetList(display, "descriptionTexts", new Object[] { descP1, descP2 });

            if (samples != null)
            {
                SetList(display, "samples", samples.ConvertAll(s => (Object)s).ToArray());
            }

            panelP1.SetActive(false);
            panelP2.SetActive(false);
        }

        private static GameObject CreateItemDescriptionSide(Transform parent, string name, string playerLabel,
                                                             Vector2 anchorMin, Vector2 anchorMax, Color accent,
                                                             out Text nameText, out Text descriptionText)
        {
            GameObject panel = NewUIChild(name, parent, anchorMin, anchorMax);
            AddBackground(panel, new Color(0.03f, 0.04f, 0.07f, 0.85f));

            // どちらのプレイヤー用か分かるよう、隅に小さく色分けしたラベルを出す
            Text label = CreateText(panel.transform, "PlayerLabel", playerLabel, 16, TextAnchor.UpperLeft,
                                    new Vector2(0.03f, 0.90f), new Vector2(0.4f, 1.0f));
            label.color = accent;

            nameText = CreateText(panel.transform, "Name", string.Empty, 28, TextAnchor.UpperCenter,
                                  new Vector2(0.04f, 0.68f), new Vector2(0.96f, 0.88f));
            descriptionText = CreateText(panel.transform, "Description", string.Empty, 18, TextAnchor.UpperCenter,
                                         new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.64f));

            // 説明文は長いものだと1行に収まらず枠からはみ出ていたため、折り返す。
            // 縦方向は Overflow のままにして、2〜3行になっても切り詰めず全文を出す
            descriptionText.horizontalOverflow = HorizontalWrapMode.Wrap;

            return panel;
        }

        /// <summary>
        /// 操作説明の表。行ごとに「何をするか」「パッド」「キーボード」を縦に揃える。
        ///
        /// 以前はタイトル画面に3行の文章で詰め込んでいたが、
        /// どのボタンが何に対応するのかを目で追いにくかった。
        /// 列を分けて並べれば、自分の使う入力の列だけを上から読める。
        /// </summary>
        private static GameObject BuildControlsHelp(Transform parent)
        {
            GameObject panel = NewUIChild("ControlsHelp", parent, new Vector2(0.24f, 0.10f), new Vector2(0.76f, 0.95f));
            AddBackground(panel, new Color(0.03f, 0.04f, 0.07f, 0.93f));

            CreateText(panel.transform, "Title", "操作説明", 40, TextAnchor.MiddleCenter,
                       new Vector2(0.04f, 0.90f), new Vector2(0.96f, 0.98f));

            // パッドの表記は PS と Xbox を併記する。
            // 記号だけ（✕・□）だと Xbox のパッドを持っている人には どのボタンか伝わらない。
            // 繋がっている機種を見て出し分ける手もあるが、2人が別々の機種を使う場合に破綻するので、
            // 常に両方載せて誰が見ても分かる形にしてある
            string[,] table =
            {
                { "移動",            "左スティック",          "W A S D" },
                { "視点",          "右スティック",          "矢印キー" },
                { "ジャンプ",          "✕ / A",                "Space" },
                { "巻物を使う", "□ / X",                "E" },
                { "飛ぶ（上／下）",     "R1・R2・L1・L2 / RB・RT・LB・LT", "Space ／ Shift" },
                { "手を選ぶ",         "十字キー → ✕ / A",     "矢印キー → Space" },
                { "メニュー操作", "十字キー",             "I J K L" },
                { "オプション",         "Start / Menu",         "Esc" }
            };

            // 見出し
            CreateHelpRow(panel.transform, "Head", 0.83f, "", "パッド   PS / Xbox", "キーボード", 24,
                          new Color(1f, 0.85f, 0.4f));

            for (int i = 0; i < table.GetLength(0); i++)
            {
                float top = 0.78f - i * 0.082f;
                CreateHelpRow(panel.transform, $"Row_{i}", top, table[i, 0], table[i, 1], table[i, 2], 24,
                              new Color(0.88f, 0.9f, 0.95f));
            }

            CreateText(panel.transform, "Note",
                       "パッドが1台のときは、空いた側にキーボードが自動で割り当てられます\n"
                       + "十字キーで「操作説明」に合わせて左右で閉じる",
                       20, TextAnchor.MiddleCenter, new Vector2(0.04f, 0.03f), new Vector2(0.96f, 0.13f));

            panel.SetActive(false);
            return panel;
        }

        /// <summary>操作説明の1行。3列を同じ高さに並べる。</summary>
        private static void CreateHelpRow(Transform parent, string name, float top,
                                          string action, string pad, string keyboard, int size, Color color)
        {
            const float height = 0.075f;

            // パッド列は PS と Xbox を併記するぶん長くなるので、他より広く取る
            Text a = CreateText(parent, $"{name}_Action", action, size, TextAnchor.MiddleLeft,
                                new Vector2(0.04f, top - height), new Vector2(0.34f, top));
            Text p = CreateText(parent, $"{name}_Pad", pad, size, TextAnchor.MiddleLeft,
                                new Vector2(0.35f, top - height), new Vector2(0.70f, top));
            Text k = CreateText(parent, $"{name}_Key", keyboard, size, TextAnchor.MiddleLeft,
                                new Vector2(0.71f, top - height), new Vector2(0.98f, top));

            a.color = color;
            p.color = color;
            k.color = color;
        }

        private static LobbySettingsPanel CreateSettingsPanel(Transform parent, int playerIndex, bool shared,
                                                              int itemCount, Vector2 anchorMin, Vector2 anchorMax)
        {
            GameObject panel = NewUIChild($"SettingsPanel_{playerIndex + 1}P", parent, anchorMin, anchorMax);

            // 背景はパネル本体ではなく子に置く。行の枠は最大数ぶん確保してあるので、
            // 畳んでいるときに背景まで伸びたままだと中身の無い板が残ってしまう。
            // 子にしておけば LobbySettingsPanel が行数に合わせて縮められる
            GameObject backgroundGo = NewUIChild("Background", panel.transform, Vector2.zero, Vector2.one);
            AddBackground(backgroundGo, new Color(0.04f, 0.05f, 0.09f, 0.82f));

            Text title = CreateText(panel.transform, "Title", $"{playerIndex + 1}P 設定", 34, TextAnchor.UpperLeft,
                                    new Vector2(0.06f, 0.88f), new Vector2(0.97f, 0.99f));
            title.color = playerIndex == 0 ? new Color(1f, 0.85f, 0.4f) : new Color(0.5f, 0.85f, 1f);

            // 1P側はアイテム設定を開いたときに一番伸びる。
            // 内訳は 感度・反転・視野角・アイテム説明・制限時間・イージーモード・操作説明・
            // アイテム設定の見出し＋アイテムの数＋戻る。
            // 数を直書きするとアイテムを増やしたときに行が足りず、末尾が表示されなくなるので、
            // 実際のアイテム数から出すこと（12種になったとき8種ぶんの13行のままで3種消えていた）
            int rowCount = shared ? 8 + itemCount + 1 : 5;
            var rows = new Object[rowCount];
            float rowHeight = 0.84f / rowCount;

            for (int i = 0; i < rowCount; i++)
            {
                float top = 0.86f - i * rowHeight;
                Text row = CreateText(panel.transform, $"Row_{i}", string.Empty, 24, TextAnchor.MiddleLeft,
                                      new Vector2(0.05f, top - rowHeight), new Vector2(0.98f, top));
                rows[i] = row;
            }

            LobbySettingsPanel component = panel.AddComponent<LobbySettingsPanel>();
            SetInt(component, "playerIndex", playerIndex);
            SetBool(component, "includeSharedSettings", shared);
            SetObject(component, "titleText", title);
            SetObject(component, "background", backgroundGo.GetComponent<RectTransform>());
            SetList(component, "rowTexts", rows);

            return component;
        }

        /// <summary>
        /// BGM再生用の AudioSource を1つ置く。
        /// タイトルと試合中(準備ルーム～サドンデス)で曲を出し分けるだけなので、
        /// AudioSource は1つで足りる（BGMPlayer側が状態を見て切り替える）。
        /// </summary>
        private static void BuildBgmPlayer(Scene scene)
        {
            AudioClip titleClip = AssetDatabase.LoadAssetAtPath<AudioClip>(BgmTitlePath);
            AudioClip playClip = AssetDatabase.LoadAssetAtPath<AudioClip>(BgmPlayPath);

            if (titleClip == null) Debug.LogError($"[MagicHand] BGM(タイトル)が見つかりません: {BgmTitlePath}");
            if (playClip == null) Debug.LogError($"[MagicHand] BGM(試合中)が見つかりません: {BgmPlayPath}");

            GameObject go = NewGameObject(scene, "BGMPlayer");
            go.AddComponent<AudioSource>();
            BGMPlayer player = go.AddComponent<BGMPlayer>();

            SetObject(player, "titleClip", titleClip);
            SetObject(player, "playClip", playClip);
            SetFloat(player, "volume", BgmVolume);
        }

        /// <summary>
        /// 効果音（SE）再生用の AudioSource を1つ置く。
        /// 重なって鳴っても構わないので PlayOneShot で1つの AudioSource に任せる（SEPlayer側）。
        /// </summary>
        private static void BuildSePlayer(Scene scene)
        {
            AudioClip startButton = AssetDatabase.LoadAssetAtPath<AudioClip>(SeStartButtonPath);
            AudioClip countdown = AssetDatabase.LoadAssetAtPath<AudioClip>(SeCountdownPath);
            AudioClip defeat = AssetDatabase.LoadAssetAtPath<AudioClip>(SeDefeatPath);
            AudioClip draw = AssetDatabase.LoadAssetAtPath<AudioClip>(SeDrawPath);
            AudioClip itemPickup = AssetDatabase.LoadAssetAtPath<AudioClip>(SeItemPickupPath);
            AudioClip stun = AssetDatabase.LoadAssetAtPath<AudioClip>(SeStunPath);
            AudioClip blink = AssetDatabase.LoadAssetAtPath<AudioClip>(SeBlinkPath);
            AudioClip speedUp = AssetDatabase.LoadAssetAtPath<AudioClip>(SeSpeedUpPath);
            AudioClip charm = AssetDatabase.LoadAssetAtPath<AudioClip>(SeCharmPath);
            AudioClip broom = AssetDatabase.LoadAssetAtPath<AudioClip>(SeBroomPath);

            if (startButton == null) Debug.LogError($"[MagicHand] SE(スタートボタン)が見つかりません: {SeStartButtonPath}");
            if (countdown == null) Debug.LogError($"[MagicHand] SE(カウントダウン)が見つかりません: {SeCountdownPath}");
            if (defeat == null) Debug.LogError($"[MagicHand] SE(倒れた)が見つかりません: {SeDefeatPath}");
            if (draw == null) Debug.LogError($"[MagicHand] SE(あいこ)が見つかりません: {SeDrawPath}");
            if (itemPickup == null) Debug.LogError($"[MagicHand] SE(アイテム取得)が見つかりません: {SeItemPickupPath}");
            if (stun == null) Debug.LogError($"[MagicHand] SE(スタン)が見つかりません: {SeStunPath}");
            if (blink == null) Debug.LogError($"[MagicHand] SE(ワープ)が見つかりません: {SeBlinkPath}");
            if (speedUp == null) Debug.LogError($"[MagicHand] SE(スピードUP)が見つかりません: {SeSpeedUpPath}");
            if (charm == null) Debug.LogError($"[MagicHand] SE(チェンジ)が見つかりません: {SeCharmPath}");
            if (broom == null) Debug.LogError($"[MagicHand] SE(ほうき)が見つかりません: {SeBroomPath}");

            GameObject go = NewGameObject(scene, "SEPlayer");
            go.AddComponent<AudioSource>();
            SEPlayer player = go.AddComponent<SEPlayer>();

            SetObject(player, "startButtonClip", startButton);
            SetObject(player, "countdownClip", countdown);
            SetObject(player, "defeatClip", defeat);
            SetObject(player, "drawClip", draw);
            SetObject(player, "itemPickupClip", itemPickup);
            SetObject(player, "stunClip", stun);
            SetObject(player, "blinkClip", blink);
            SetObject(player, "speedUpClip", speedUp);
            SetObject(player, "charmClip", charm);
            SetObject(player, "broomClip", broom);
            SetFloat(player, "volume", SeVolume);
            SetFloat(player, "countdownVolume", SeCountdownVolume);
        }

        private static void BuildGlobalUI(Scene scene)
        {
            GameObject canvasGo = NewGameObject(scene, "UI_Global");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // --- タイトル ---
            GameObject title = NewUIChild("TitlePanel", canvasGo.transform, Vector2.zero, Vector2.one);

            // タイトル画面は1枚絵。題字もSTARTの台座も絵の中に描かれているので、
            // コードで文字や板を重ねない。重ねると絵の同じ要素と二重になる
            AddTitleArtwork(title);

            // 操作説明はここには置かない。1枚絵を隠してしまううえ、
            // 試合の直前に読み返したくなるのはタイトルではなく準備ルーム。
            // 準備ルームの設定パネルに「操作説明」の行を用意してある

            // 絵に描かれた START の台座にぴったり重ねる。板も文字も出さず、当たり判定だけ置く。
            // 位置は元画像から実測（横 0.320〜0.676、下から 0.029〜0.261）
            Button startButton = CreateInvisibleButton(title.transform, "StartButton",
                                                       new Vector2(0.320f, 0.029f), new Vector2(0.676f, 0.261f));

            TitleUI titleUI = canvasGo.AddComponent<TitleUI>();
            SetObject(titleUI, "panel", title);
            SetObject(titleUI, "startButton", startButton);

            // --- リザルトへの目隠し ---
            //
            // 勝敗の表示は Victory シーンが受け持つので、ここには結果画面を置かない。
            // ただし LoadScene はその場では切り替わらず、呼んだフレームの描画は最後まで走る。
            // 何も被せないと、勝負がついた直後の試合画面が1フレーム出てから切り替わる。
            // 全面を黒で覆う板を1枚だけ用意して、遷移を決めた瞬間に出す
            GameObject cover = NewUIChild("SceneCover", canvasGo.transform, Vector2.zero, Vector2.one);
            AddBackground(cover, Color.black);
            cover.SetActive(false);

            SceneCover sceneCover = canvasGo.AddComponent<SceneCover>();
            SetObject(sceneCover, "cover", cover);

            BuildTieBreakUI(canvasGo);
            BuildFinishUI(canvasGo);
            BuildStartUI(canvasGo);
            BuildScoreDashUI(canvasGo);
        }

        /// <summary>
        /// 得点表示の仕切り線に置く「-」。各プレイヤーのHUD（InGameHUD.scoreText）は
        /// 自分の点だけを画面の内側の縁（＝仕切り線）ぎりぎりに出すので、
        /// ここで挟む「-」と合わせて初めて「3 - 2」のように読める。
        /// 縦位置はScoreTextのアンカー（0.86〜0.94）に合わせてある
        /// </summary>
        private static void BuildScoreDashUI(GameObject canvasGo)
        {
            GameObject panel = NewUIChild("ScoreDashPanel", canvasGo.transform, Vector2.zero, Vector2.one);

            Text dash = CreateText(panel.transform, "Dash", "-", 64, TextAnchor.UpperCenter,
                                   new Vector2(0.43f, 0.80f), new Vector2(0.57f, 0.94f));
            AddTextOutline(dash);

            ScoreDashUI ui = canvasGo.AddComponent<ScoreDashUI>();
            SetObject(ui, "panel", panel);

            panel.SetActive(false);
        }

        /// <summary>
        /// 試合が終わった瞬間からResultへ移るまでの一瞬挟む画面。
        /// 通常の時間切れ・サドンデスの決着・同点分岐の「結果発表」、すべてここを必ず通る。
        /// </summary>
        private static void BuildFinishUI(GameObject canvasGo)
        {
            GameObject panel = NewUIChild("FinishPanel", canvasGo.transform, Vector2.zero, Vector2.one);
            AddBackground(panel, new Color(0.05f, 0.05f, 0.09f, 0.9f));

            CreateText(panel.transform, "Title", "FINISH", 96, TextAnchor.MiddleCenter,
                       new Vector2(0.1f, 0.4f), new Vector2(0.9f, 0.6f));

            FinishUI ui = canvasGo.AddComponent<FinishUI>();
            SetObject(ui, "panel", panel);

            panel.SetActive(false);
        }

        /// <summary>
        /// 3-2-1のあとの「START」。分割画面それぞれに出すと二つ並んでしまうので、
        /// Finishと同じ共有Canvas側に一つだけ置く。試合はもう見えているべきなので、
        /// Finishと違って背景は敷かず、縁取りだけで文字を読ませる
        /// </summary>
        private static void BuildStartUI(GameObject canvasGo)
        {
            GameObject panel = NewUIChild("StartPanel", canvasGo.transform, Vector2.zero, Vector2.one);

            Text title = CreateText(panel.transform, "Title", "START", 120, TextAnchor.MiddleCenter,
                                    new Vector2(0.1f, 0.4f), new Vector2(0.9f, 0.6f));
            AddTextOutline(title);

            StartUI ui = canvasGo.AddComponent<StartUI>();
            SetObject(ui, "panel", panel);

            panel.SetActive(false);
        }

        /// <summary>
        /// 時間切れで同点だったときの分岐画面。結果発表の手前に挟む。
        /// どちらのプレイヤーからも選べるので、ボタンの案内も両方の入力を併記する。
        /// </summary>
        /// <summary>十字キーで選ぶ選択肢の1行。押せるボタンではなく、色と印で選択中を示すだけの板。</summary>
        private static void CreateChoiceRow(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                                            out Image background, out Text label)
        {
            GameObject go = NewUIChild(name, parent, anchorMin, anchorMax);

            background = go.AddComponent<Image>();
            background.color = new Color(0.18f, 0.24f, 0.38f);
            background.raycastTarget = false;

            label = CreateText(go.transform, "Label", string.Empty, 36, TextAnchor.MiddleCenter,
                               Vector2.zero, Vector2.one);
        }

        private static void BuildTieBreakUI(GameObject canvasGo)
        {
            GameObject panel = NewUIChild("TieBreakPanel", canvasGo.transform, Vector2.zero, Vector2.one);
            AddBackground(panel, new Color(0.05f, 0.05f, 0.09f, 0.9f));

            CreateText(panel.transform, "Title", "時間切れ", 80, TextAnchor.MiddleCenter,
                       new Vector2(0.1f, 0.72f), new Vector2(0.9f, 0.88f));

            Text score = CreateText(panel.transform, "ScoreText", "0  -  0   同点！", 52, TextAnchor.MiddleCenter,
                                    new Vector2(0.1f, 0.60f), new Vector2(0.9f, 0.72f));
            score.color = new Color(1f, 0.85f, 0.4f);

            // 手の選択と同じく十字キーで選ぶので、押すボタンは行ごとに書かない
            CreateChoiceRow(panel.transform, "Choice_SuddenDeath", new Vector2(0.28f, 0.40f), new Vector2(0.72f, 0.52f),
                            out Image sdBackground, out Text sdLabel);
            CreateChoiceRow(panel.transform, "Choice_Result", new Vector2(0.28f, 0.24f), new Vector2(0.72f, 0.36f),
                            out Image resultBackground, out Text resultLabel);

            CreateText(panel.transform, "Hint",
                       "十字キーで選択　✕/A（Space）で決定\n"
                       + "サドンデスは先に1点取った方が勝ち　／　どちらのプレイヤーでも選べます",
                       26, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.09f), new Vector2(0.95f, 0.2f));

            TieBreakUI ui = canvasGo.AddComponent<TieBreakUI>();
            SetObject(ui, "panel", panel);
            SetObject(ui, "scoreText", score);
            SetList(ui, "choiceBackgrounds", new Object[] { sdBackground, resultBackground });
            SetList(ui, "choiceLabels", new Object[] { sdLabel, resultLabel });

            panel.SetActive(false);
        }

        // ---- アセット生成 ---------------------------------------------------

        private static List<ItemDefinitionSO> CreateHandItems()
        {
            var list = new List<ItemDefinitionSO>();

            list.Add(CreateHandItem("HandItem_Gu", HandType.Gu, "グー"));
            list.Add(CreateHandItem("HandItem_Choki", HandType.Choki, "チョキ"));
            list.Add(CreateHandItem("HandItem_Pa", HandType.Pa, "パー"));

            return list;
        }

        private static ItemDefinitionSO CreateHandItem(string fileName, HandType hand, string displayName)
        {
            HandItemSO asset = LoadOrCreate<HandItemSO>(fileName);
            SetString(asset, "displayName", displayName);
            SetColor(asset, "displayColor", hand.ToColor());
            SetString(asset, "description", $"取得すると手が「{hand.ToLabel()}」に変わる。");
            SetEnum(asset, "hand", (int)hand);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static List<ItemDefinitionSO> CreateScrolls(RevealMarker enemyMarker, SearchWaveEffect searchWavePrefab)
        {
            var list = new List<ItemDefinitionSO>();

            SpeedBoostEffectSO speed = LoadOrCreate<SpeedBoostEffectSO>("Scroll_SpeedBoost");
            SetString(speed, "displayName", "スピードUp");
            SetColor(speed, "displayColor", new Color(1f, 0.85f, 0.25f));
            SetString(speed, "description", "一定時間、移動速度が上がる。");
            SetFloat(speed, "speedMultiplier", 1.5f);
            SetFloat(speed, "duration", 5f);
            SetObject(speed, "icon", CreateIcon("Icon_SpeedBoost", IconShape.DoubleArrowUp, Color.white));
            EditorUtility.SetDirty(speed);
            list.Add(speed);

            StunTrapEffectSO stun = LoadOrCreate<StunTrapEffectSO>("Scroll_StunTrap");
            SetString(stun, "displayName", "スタン");
            SetColor(stun, "displayColor", new Color(0.85f, 0.35f, 1f));
            SetString(stun, "description", "周囲の相手を短時間スタンさせる。スタン中の相手はワープ以外のアイテムを使用できなくなる。");
            SetFloat(stun, "radius", 5f);
            SetFloat(stun, "stunDuration", 1.5f);
            SetObject(stun, "icon", CreateIcon("Icon_Stun", IconShape.Bolt, Color.white));
            EditorUtility.SetDirty(stun);
            list.Add(stun);

            TeleportEffectSO teleport = LoadOrCreate<TeleportEffectSO>("Scroll_Teleport");
            SetString(teleport, "displayName", "ワープ");
            SetColor(teleport, "displayColor", new Color(0.35f, 1f, 0.9f));
            SetString(teleport, "description", "前方へ瞬間移動する。");
            SetFloat(teleport, "distance", 14f);
            SetObject(teleport, "icon", CreateIcon("Icon_Teleport", IconShape.ArrowRight, Color.white));
            EditorUtility.SetDirty(teleport);
            list.Add(teleport);

            HandScrambleEffectSO charm = LoadOrCreate<HandScrambleEffectSO>("Scroll_HandScramble");
            SetString(charm, "displayName", "チェンジ");
            SetColor(charm, "displayColor", new Color(0.4f, 0.9f, 0.4f));
            SetString(charm, "description", "周囲の相手の手を、自分が勝てる手に変えてしまう。");
            SetFloat(charm, "radius", 8f);
            SetObject(charm, "icon", CreateIcon("Icon_Charm", IconShape.Swirl, Color.white));
            EditorUtility.SetDirty(charm);
            list.Add(charm);

            RevealEffectSO revealEnemy = LoadOrCreate<RevealEffectSO>("Scroll_RevealEnemy");
            SetString(revealEnemy, "displayName", "サーチ");
            SetColor(revealEnemy, "displayColor", new Color(1f, 0.35f, 0.35f));
            SetString(revealEnemy, "description", "10秒間、相手の位置が壁越しに見えるようになる。");
            SetFloat(revealEnemy, "duration", 10f);
            SetObject(revealEnemy, "markerPrefab", enemyMarker);
            SetObject(revealEnemy, "wavePrefab", searchWavePrefab);
            SetObject(revealEnemy, "icon", CreateIcon("Icon_Search", IconShape.Eye, Color.white));
            EditorUtility.SetDirty(revealEnemy);
            list.Add(revealEnemy);

            return list;
        }

        /// <summary>
        /// 湧きの抽選テーブルに入れる「中身が決まっていない巻物」。
        /// 5種類の巻物を候補として持たせておき、実際にどれになるかは拾った瞬間に決める。
        /// </summary>
        private static RandomScrollSO CreateRandomScroll(List<ItemDefinitionSO> scrolls)
        {
            RandomScrollSO asset = LoadOrCreate<RandomScrollSO>("Scroll_Random");
            SetString(asset, "displayName", "巻物");
            SetString(asset, "description", "拾うと5種類のうちどれかの魔法が手に入る。");

            var candidates = new List<ScrollEffectSO>();
            foreach (ItemDefinitionSO item in scrolls)
            {
                if (item is ScrollEffectSO effect) candidates.Add(effect);
            }

            SetList(asset, "candidates", candidates.ToArray());
            EditorUtility.SetDirty(asset);
            return asset;
        }

        /// <summary>
        /// イージーモード用の「中身が決まっていない巻物」。
        /// チェンジ（相手の手を強制変更）とワープ（瞬間移動）は駆け引きが複雑になりすぎるため、
        /// 練習向けのイージーモードでは候補から外す。
        /// </summary>
        private static RandomScrollSO CreateRandomScrollEasy(List<ItemDefinitionSO> scrolls)
        {
            RandomScrollSO asset = LoadOrCreate<RandomScrollSO>("Scroll_Random_Easy");
            SetString(asset, "displayName", "巻物");
            SetString(asset, "description", "拾うと3種類のうちどれかの魔法が手に入る（イージーモード）。");

            var candidates = new List<ScrollEffectSO>();
            foreach (ItemDefinitionSO item in scrolls)
            {
                if (item is TeleportEffectSO || item is HandScrambleEffectSO) continue;
                if (item is ScrollEffectSO effect) candidates.Add(effect);
            }

            SetList(asset, "candidates", candidates.ToArray());
            EditorUtility.SetDirty(asset);
            return asset;
        }

        /// <summary>
        /// ほうきだけの抽選テーブル。
        ///
        /// スクロールと同じ枠に入れていたときは出現率が候補数の逆数に固定されてしまい、
        /// 常に複数本が場に出ていた。飛行は移動と索敵と逃走を一度に解決してしまうので、
        /// 「どちらが取るか」を賭ける対象であってほしい。
        /// そのため専用のグループへ分け、マップ上に1本までとしている。
        /// </summary>
        private static List<ItemDefinitionSO> CreateBrooms()
        {
            var list = new List<ItemDefinitionSO>();

            BroomEffectSO broom = LoadOrCreate<BroomEffectSO>("Scroll_Broom");
            SetString(broom, "displayName", "ほうき");
            SetColor(broom, "displayColor", new Color(0.78f, 0.62f, 0.30f));
            SetString(broom, "description", "5秒間 自由に飛べる。着地すると3秒間 位置が相手にバレて足が遅くなる。使用中はアイテムも手変更アイテムも拾えない。");
            SetObject(broom, "icon", CreateIcon("Icon_Broom", IconShape.Broom, Color.white));
            EditorUtility.SetDirty(broom);
            list.Add(broom);

            return list;
        }

        private static T LoadOrCreate<T>(string fileName) where T : ScriptableObject
        {
            string path = $"{GameRoot}/ScriptableObjects/{fileName}.asset";
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);

            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }

            return asset;
        }

        private static ItemPickup CreateItemPrefab(Material material, Material broomHandleMat, Material broomBristleMat)
        {
            GameObject root = new GameObject("ItemPickup");

            SphereCollider trigger = root.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 0.6f, 0f);
            trigger.radius = 0.8f;

            // グー／チョキ／パーはそれぞれ専用モデル（Assets/Te）、スクロール＝巻物、
            // ほうき＝ほうき、で形状そのものから見分けられるようにする。
            // さらに白い縁取りを付けて、地面に落ちている巻物（無地）と一目で区別できるようにする
            GameObject guVisual = CreateHandVisual(GuVisualPrefabPath, "GuVisual", root.transform, material,
                                                    addOutline: true);
            GameObject chokiVisual = CreateHandVisual(ChokiVisualPrefabPath, "ChokiVisual", root.transform, material,
                                                       addOutline: true);
            // パーの本は正面から見ると縦長（背表紙が上）になるので、横向きに寝かせる
            GameObject paVisual = CreateHandVisual(PaVisualPrefabPath, "PaVisual", root.transform, material,
                                                    new Vector3(0f, 0f, 90f), addOutline: true);

            GameObject scrollVisual = CreateScrollVisual(root.transform, material);

            // ほうきは落ちているときも立てて見せる。柄が上なので、
            // 全長の半分だけ持ち上げれば穂の先が地面に接する。
            // 遠くからでも見つけやすいよう、地面の見た目だけ元の2倍の大きさにする
            // （プレイヤーが持っているときの杖代わりの見た目は変えない）
            const float groundBroomScale = 2f;
            GameObject broomVisual = CreateBroomModel(root.transform, "BroomVisual", broomHandleMat, broomBristleMat);
            broomVisual.transform.localScale = Vector3.one * groundBroomScale;
            broomVisual.transform.localPosition = new Vector3(0f, BroomLength * groundBroomScale / 2f, 0f);
            broomVisual.transform.localRotation = Quaternion.Euler(18f, 0f, 12f);

            // ほうきはマップに1本だけなので、遠くからでも位置が分かるよう薄いビーコンを立てる
            Material beaconMat = CreateTransparentUnlitMaterial("M_BroomBeacon", new Color(0.78f, 0.62f, 0.30f, 0.25f));
            GameObject beaconVisual = CreateBeacon(root.transform, beaconMat);

            ItemPickup pickup = root.AddComponent<ItemPickup>();
            SetObject(pickup, "guVisual", guVisual);
            SetObject(pickup, "chokiVisual", chokiVisual);
            SetObject(pickup, "paVisual", paVisual);
            SetObject(pickup, "scrollVisual", scrollVisual);
            SetObject(pickup, "broomVisual", broomVisual);
            SetObject(pickup, "beaconVisual", beaconVisual);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            return saved.GetComponent<ItemPickup>();
        }

        private const string ScrollModelPath = "Assets/cgtrader_optimized_r1.fbx";

        /// <summary>拾えるスクロールの見た目の長さ。遠くからでも見つけやすいよう、元の2倍にしてある。</summary>
        private const float ScrollVisualLength = 1.4f;

        /// <summary>
        /// 巻物を斜めに傾ける角度。長軸がZなので、傾けるのはX軸まわり。
        /// Z軸まわりに回しても円筒が自転するだけで見た目が変わらない。
        /// </summary>
        private static readonly Vector3 ScrollVisualTilt = new Vector3(28f, 0f, 12f);

        /// <summary>
        /// 拾えるスクロールの見た目を、巻物のモデルで作る。
        ///
        /// 色は染めない。巻物は元の紙と紐の色のまま出す。
        /// 中身が何かは拾うまで分からない方がよく、色分けはHUDの表示が受け持つ。
        ///
        /// モデルが見つからないときはキューブに退避する。
        /// アセットが未取り込みでもシーン生成自体は通るようにしておきたい。
        /// </summary>
        private static GameObject CreateScrollVisual(Transform parent, Material fallbackMaterial)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(ScrollModelPath);
            if (source == null)
            {
                Debug.LogWarning($"[MagicHand] スクロールのモデルが見つかりません: {ScrollModelPath}");

                GameObject cube = CreatePrimitive(PrimitiveType.Cube, "ScrollVisual", parent, fallbackMaterial);
                cube.transform.localPosition = new Vector3(0f, 0.6f, 0f);
                cube.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
                cube.transform.localRotation = Quaternion.Euler(35f, 45f, 0f);
                Object.DestroyImmediate(cube.GetComponent<Collider>());

                return cube;
            }

            GameObject visual = (GameObject)Object.Instantiate(source, parent);
            visual.name = "ScrollVisual";
            visual.transform.localRotation = Quaternion.Euler(ScrollVisualTilt);

            foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider);
            }

            // モデルの実寸（長さ1.64）に依存したくないので、実測してから縮尺を決める。
            // 傾けたあとのAABBではなく、傾ける前の長軸で測りたいので回転を戻して測る
            Quaternion tilted = visual.transform.localRotation;
            visual.transform.localRotation = Quaternion.identity;

            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (longest > 0f)
            {
                visual.transform.localScale *= ScrollVisualLength / longest;
            }

            visual.transform.localRotation = tilted;
            visual.transform.localPosition = new Vector3(0f, 0.6f, 0f);

            return visual;
        }

        /// <summary>
        /// 手変更アイテム（グー/チョキ/パー）の見た目。地面の球（旧仕様=0.55）の4倍の大きさ
        /// （2026-08-22: 0.55→1.1→2.2 と2回の「2倍に」依頼を経てこの値）。
        /// 頭上の相手向け表示（<see cref="HandIndicatorTargetSize"/>）はこの値に対する比率で
        /// 縮小しているため、ここを変えても頭上表示の大きさは変わらない。
        /// </summary>
        private const float HandVisualTargetSize = 2.2f;

        /// <summary>
        /// グー／チョキ／パーの専用モデル（Assets/Te）を読み込んで正規化する。
        ///
        /// 3体はそれぞれ元のシーンでの縮尺・位置をそのまま prefab 化されており
        /// （岩の塊5倍 / 交差した剣2倍 / 単一メッシュ0.1倍、というように互いにバラバラ）、
        /// 数値をそのまま使うと大きさが揃わない。CreateScrollVisual と同じ要領で、
        /// 実際のバウンディングボックスを測ってから目標の大きさへ縮尺し直す。
        /// </summary>
        private static GameObject CreateHandVisual(string path, string name, Transform parent, Material fallbackMaterial,
                                                    Vector3 extraEuler = default, bool addOutline = false)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null)
            {
                Debug.LogWarning($"[MagicHand] 手のモデルが見つかりません: {path}");

                GameObject fallback = CreatePrimitive(PrimitiveType.Sphere, name, parent, fallbackMaterial);
                fallback.transform.localPosition = new Vector3(0f, 0.6f, 0f);
                fallback.transform.localScale = Vector3.one * HandVisualTargetSize;
                Object.DestroyImmediate(fallback.GetComponent<Collider>());
                if (addOutline) AddOutlineShell(fallback);
                return fallback;
            }

            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            visual.name = name;
            visual.transform.localPosition = Vector3.zero;
            // extraEuler は「横向きに寝かせる」等の見た目調整用（パーの本のみ使用）。
            // ここで先に回してから実測するので、回した後の実際の見た目サイズで正規化される
            visual.transform.localRotation = Quaternion.Euler(extraEuler);
            visual.transform.localScale = Vector3.one;

            foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider);
            }

            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

                float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                if (longest > 0f) visual.transform.localScale = Vector3.one * (HandVisualTargetSize / longest);
            }

            visual.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            if (addOutline) AddOutlineShell(visual);
            return visual;
        }

        /// <summary>
        /// 反転殻方式の縁取り。同じ見た目をひとまわり大きく複製し、
        /// 表面（カメラ側）を消して背面だけ残す（`_Cull`をFrontに）ことで、
        /// 本体の輪郭からはみ出た部分だけが縁として見える。
        /// 手変更アイテム（グー/チョキ/パー）を巻物と見分けやすくするために使う
        /// </summary>
        private static void AddOutlineShell(GameObject target, float scaleMultiplier = 1.06f)
        {
            Material outline = CreateOutlineMaterial("M_HandItemOutline", Color.white);

            GameObject shell = Object.Instantiate(target, target.transform);
            shell.name = "Outline";
            shell.transform.localPosition = Vector3.zero;
            shell.transform.localRotation = Quaternion.identity;
            shell.transform.localScale = Vector3.one * scaleMultiplier;

            foreach (Renderer renderer in shell.GetComponentsInChildren<Renderer>(true))
            {
                var materials = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++) materials[i] = outline;
                renderer.sharedMaterials = materials;
            }
        }

        /// <summary>反転殻の縁取り専用マテリアル。表面を消して背面だけ描く。</summary>
        private static Material CreateOutlineMaterial(string name, Color color)
        {
            string path = $"{GameRoot}/Materials/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color")) material.SetColor("_Color", color);

            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Front);

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// ほうきの位置を遠くからでも分かるように立てる、薄い半透明の光の柱。
        /// 円柱を細長く伸ばすだけ。地面から浮かせないよう、原点（足元）から上へ伸ばす
        /// </summary>
        private static GameObject CreateBeacon(Transform parent, Material material)
        {
            const float height = 10f;
            const float radius = 0.18f;

            GameObject beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beacon.name = "Beacon";
            beacon.transform.SetParent(parent, false);
            Object.DestroyImmediate(beacon.GetComponent<Collider>());

            // 既定の Cylinder は高さ2・半径0.5（ローカル座標）なので、目標の寸法へ縮尺し直す
            beacon.transform.localScale = new Vector3(radius * 2f, height / 2f, radius * 2f);
            beacon.transform.localPosition = new Vector3(0f, height / 2f, 0f);

            beacon.GetComponent<MeshRenderer>().sharedMaterial = material;
            return beacon;
        }

        /// <summary>半透明のUnlitマテリアル。ビーコン等、薄く光らせたいだけの表示に使う。</summary>
        private static Material CreateTransparentUnlitMaterial(string name, Color color)
        {
            string path = $"{GameRoot}/Materials/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color")) material.SetColor("_Color", color);

            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f); // Transparent
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f); // Alpha
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// TextMesh用の、頂点カラー（TextMesh.color）を反映するマテリアル。
        ///
        /// 既定の Font.material（"GUI/Text Shader"）はIMGUI向けのレガシーシェーダーで、
        /// URPでは通常の深度テストを行わず壁越しでも常に見えてしまう。かといって標準の
        /// 「Universal Render Pipeline/Unlit」に載せ替えると、深度テストは直るが
        /// **頂点カラーを一切参照しないため優位/劣位/互角の色分けが効かなくなる**
        /// （文字が常にフォントテクスチャそのものの色＝黒で出てしまう）。2026-08-23、
        /// 「緑/赤/グレーに変えたのに文字が黒いまま」という報告で発覚した。
        ///
        /// フォントのテクスチャはアルファチャンネルだけがグリフの形を持つ（RGBは無視してよい）ため、
        /// 自作シェーダー<see cref="WorldTextVertexColor"/>で「テクスチャのアルファ×頂点カラー」を
        /// 合成する（旧"GUI/Text Shader"と同じ方式）。ZTestは通常のLEqualのまま
        /// （壁を貫通させたい表示用の<see cref="CreateXRayMaterial"/>とは役割が逆）。
        /// 既存のマテリアルアセットが古いシェーダーを参照したままでも直るよう、
        /// 毎回シェーダーを明示的に上書きする。
        /// </summary>
        private static Material CreateTextMeshMaterial(string name, Font font)
        {
            string path = $"{GameRoot}/Materials/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("MagicHand/WorldTextVertexColor");

            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            Texture fontTexture = font != null ? font.material.mainTexture : null;
            material.SetTexture("_MainTex", fontTexture);

            EditorUtility.SetDirty(material);
            return material;
        }

        private const int HandIconRenderSize = 128;

        /// <summary>
        /// アイコン合成時に透明として抜く背景色。モデルの素材に出てこなさそうな色を選んでおく。
        /// </summary>
        private static readonly Color HandIconChromaKey = new Color(1f, 0f, 1f);

        /// <summary>
        /// 自分の手表示（HUD）のアイコンを、地面のアイテムと同じ Assets/Te のモデルを撮って作る。
        ///
        /// 手描き図形ではなく実物を撮る方針にしたのは、地面のアイテム・相手の頭上表示と
        /// 同じ見た目で統一するため（3箇所で見た目がバラバラだと同じ「グー」だと気づきにくい）。
        /// `BroomPosePreview` と同じ「本編から離れた高所にモデルを置いてカメラで撮る」手法を使うが、
        /// URPのカメラ背景アルファがそのままPNGの透過に使えるとは限らないため、
        /// 単色（マゼンタ）を背景にして撮ってからCPU側でその色だけを透明に置き換える
        /// （クロマキー方式）。生成物は Textures/Icons にキャッシュされ、次回以降は撮り直さない。
        /// </summary>
        private static Sprite RenderHandIcon(string fileName, string prefabPath, float rollDegrees = 0f)
        {
            string path = $"{GameRoot}/Textures/Icons/{fileName}.png";
            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (source == null)
            {
                Debug.LogWarning($"[MagicHand] アイコン用モデルが見つかりません: {prefabPath}");
                return null;
            }

            EnsureIconFolder();

            GameObject stage = new GameObject("IconPreviewStage");
            // 本編・準備ルームと重ならない高所で撮る（BroomPosePreview と同じ考え方）
            stage.transform.position = new Vector3(0f, 800f, 0f);

            // 本編のライトが届かない高さなので、素材が暗い岩（グー）でも見えるよう専用の光を用意する
            GameObject lightGo = new GameObject("IconPreviewLight");
            lightGo.transform.SetParent(stage.transform, false);
            lightGo.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            Light iconLight = lightGo.AddComponent<Light>();
            iconLight.type = LightType.Directional;
            iconLight.intensity = 1.6f;
            iconLight.shadows = LightShadows.None;

            GameObject camGo = new GameObject("IconPreviewCamera");
            RenderTexture rt = new RenderTexture(HandIconRenderSize, HandIconRenderSize, 24);
            Texture2D tex = new Texture2D(HandIconRenderSize, HandIconRenderSize, TextureFormat.RGBA32, false);

            try
            {
                GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source, stage.transform);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;

                foreach (Collider collider in model.GetComponentsInChildren<Collider>(true))
                {
                    Object.DestroyImmediate(collider);
                }

                // 実測して原点付近・単位サイズへ正規化してから撮る。3体で元の縮尺がバラバラなため
                Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

                float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                if (longest > 0f) model.transform.localScale = Vector3.one * (1f / longest);

                renderers = model.GetComponentsInChildren<Renderer>(true);
                bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

                // 枠いっぱいに写すと縁の色（赤/緑/青）が見切れて分からなくなる一方、
                // 単純にカメラを引くだけだと交差剣（チョキ）のように中身がスカスカな形は
                // 小さく写りすぎて見えなくなる（どちらも実測して判明した）。
                // 「バウンディングボックスの対角」のような形状に依存する近似ではなく、
                // 実際の視点方向へ8つの角を投影した実測の横幅・高さを使ってカメラを引く。
                // これなら平たい・細長いなど形がバラバラでも見た目の大きさが揃う
                const float fillFraction = 0.92f;
                const float verticalFov = 30f;
                Vector3 direction = new Vector3(0f, 0.25f, -0.97f).normalized;

                Vector3 forward = -direction;
                Vector3 baseRight = Vector3.Cross(Vector3.up, forward).normalized;
                if (baseRight.sqrMagnitude < 0.001f) baseRight = Vector3.right;
                Vector3 baseUp = Vector3.Cross(forward, baseRight).normalized;

                // パーの本を横向きに見せる、といった見た目の微調整用。
                // カメラの向きそのものをロール（自身の前方軸まわりに回転）させることで、
                // モデル側のローカル回転を一切知らなくても画像を回せる
                float rollRad = rollDegrees * Mathf.Deg2Rad;
                Vector3 right = baseRight * Mathf.Cos(rollRad) + baseUp * Mathf.Sin(rollRad);
                Vector3 up = -baseRight * Mathf.Sin(rollRad) + baseUp * Mathf.Cos(rollRad);

                float halfWidthNeeded = 0f;
                float halfHeightNeeded = 0f;
                Vector3 extents = bounds.extents;
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    for (int sy = -1; sy <= 1; sy += 2)
                    {
                        for (int sz = -1; sz <= 1; sz += 2)
                        {
                            Vector3 corner = new Vector3(sx * extents.x, sy * extents.y, sz * extents.z);
                            halfWidthNeeded = Mathf.Max(halfWidthNeeded, Mathf.Abs(Vector3.Dot(corner, right)));
                            halfHeightNeeded = Mathf.Max(halfHeightNeeded, Mathf.Abs(Vector3.Dot(corner, up)));
                        }
                    }
                }

                float halfExtentNeeded = Mathf.Max(halfWidthNeeded, halfHeightNeeded);
                float distance = (halfExtentNeeded / fillFraction) / Mathf.Tan(verticalFov * 0.5f * Mathf.Deg2Rad);

                Camera cam = camGo.AddComponent<Camera>();
                cam.transform.position = bounds.center + direction * distance;
                cam.transform.LookAt(bounds.center, up);
                cam.fieldOfView = verticalFov;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = HandIconChromaKey;
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0f, 0f, HandIconRenderSize, HandIconRenderSize), 0, 0);
                RenderTexture.active = previous;

                Color32[] pixels = tex.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color c = pixels[i];
                    float keyDistance = Mathf.Abs(c.r - HandIconChromaKey.r)
                                       + Mathf.Abs(c.g - HandIconChromaKey.g)
                                       + Mathf.Abs(c.b - HandIconChromaKey.b);
                    if (keyDistance < 0.12f) pixels[i] = new Color32(0, 0, 0, 0);
                }
                tex.SetPixels32(pixels);
                tex.Apply();

                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(stage);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(tex);
                rt.Release();
                Object.DestroyImmediate(rt);
            }

            AssetDatabase.ImportAsset(path);
            ConfigureIconImporter(path);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>ほうきの全長。杖(2.30m)より短くして、持っているときに一目で見分けられるようにする。</summary>
        private const float BroomLength = 1.6f;

        /// <summary>
        /// ほうきの仮モデルをプリミティブで組む。アセットにほうきが無いため。
        ///
        /// 柄を +Y、穂を -Y に向けて作る。杖と同じ向きにしておくと、
        /// 手に持たせる計算（HeldItemPose）をそのまま流用できるため。
        /// 本物のアセットへ差し替えるときも、この向きと全長さえ合わせれば他は触らずに済む。
        /// </summary>
        private static GameObject CreateBroomModel(Transform parent, string name,
                                                   Material handleMaterial, Material bristleMaterial)
        {
            GameObject root = NewChild(name, parent);

            float handleLength = BroomLength * 0.72f;
            float bristleLength = BroomLength - handleLength;

            // 柄。円柱の既定の高さは2なので、半分の値をスケールに入れる
            GameObject handle = CreatePrimitive(PrimitiveType.Cylinder, "Handle", root.transform, handleMaterial);
            handle.transform.localScale = new Vector3(0.06f, handleLength / 2f, 0.06f);
            handle.transform.localPosition = new Vector3(0f, BroomLength / 2f - handleLength / 2f, 0f);
            Object.DestroyImmediate(handle.GetComponent<Collider>());

            // 穂。先へ行くほど広がるよう、細い箱を角度をつけて放射状に並べる
            const int bristleCount = 6;
            for (int i = 0; i < bristleCount; i++)
            {
                float angle = i * 360f / bristleCount;

                GameObject bristle = CreatePrimitive(PrimitiveType.Cube, $"Bristle_{i}", root.transform, bristleMaterial);
                bristle.transform.localScale = new Vector3(0.05f, bristleLength, 0.05f);
                bristle.transform.localPosition = new Vector3(0f, -BroomLength / 2f + bristleLength / 2f, 0f);
                bristle.transform.localRotation = Quaternion.Euler(0f, angle, 12f);
                Object.DestroyImmediate(bristle.GetComponent<Collider>());
            }

            // 柄と穂の境目の結束
            GameObject band = CreatePrimitive(PrimitiveType.Cylinder, "Band", root.transform, bristleMaterial);
            band.transform.localScale = new Vector3(0.1f, 0.04f, 0.1f);
            band.transform.localPosition = new Vector3(0f, -BroomLength / 2f + bristleLength, 0f);
            Object.DestroyImmediate(band.GetComponent<Collider>());

            return root;
        }

        // ---- アイテムのアイコン -------------------------------------------

        /// <summary>アイコンの1辺。HUDの枠に収まればよいので大きくしない。</summary>
        private const int IconSize = 64;

        /// <summary>
        /// スクロールのアイコンを描く。記号は単純な図形の組み合わせで作る。
        ///
        /// 外部のアイコン素材は使わない。アセットを増やすたびに
        /// スケール・ピボット・マテリアルの調整で手戻りが出ているのと、
        /// この解像度なら図形の組み合わせで十分見分けがつくため。
        /// </summary>
        private enum IconShape
        {
            /// <summary>上向きの二重矢印。スピードUP。</summary>
            DoubleArrowUp,


            /// <summary>稲妻。スタン。</summary>
            Bolt,

            /// <summary>右向きの太い矢印。ワープ。</summary>
            ArrowRight,

            /// <summary>渦。チェンジ。</summary>
            Swirl,

            /// <summary>目。サーチ。</summary>
            Eye,

            /// <summary>ほうき。</summary>
            Broom
        }

        private static Sprite CreateIcon(string fileName, IconShape shape, Color color)
        {
            string path = $"{GameRoot}/Textures/Icons/{fileName}.png";
            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;

            EnsureIconFolder();

            var pixels = new Color32[IconSize * IconSize];
            Color32 fill = color;
            Color32 clear = new Color32(0, 0, 0, 0);

            for (int y = 0; y < IconSize; y++)
            {
                for (int x = 0; x < IconSize; x++)
                {
                    // 0〜1 に正規化した座標で図形を判定すると、解像度を変えても式を直さずに済む
                    float u = (x + 0.5f) / IconSize;
                    float v = (y + 0.5f) / IconSize;

                    pixels[y * IconSize + x] = IsInsideShape(shape, u, v) ? fill : clear;
                }
            }

            var texture = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();

            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path);
            ConfigureIconImporter(path);

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>
        /// Textures/Icons は初回には存在しない。
        /// File.WriteAllBytes は無いフォルダには書けず、例外でシーン生成ごと止まるので先に作る。
        /// </summary>
        private static void EnsureIconFolder()
        {
            if (!AssetDatabase.IsValidFolder($"{GameRoot}/Textures"))
            {
                AssetDatabase.CreateFolder(GameRoot, "Textures");
            }

            if (!AssetDatabase.IsValidFolder($"{GameRoot}/Textures/Icons"))
            {
                AssetDatabase.CreateFolder($"{GameRoot}/Textures", "Icons");
            }
        }

        private static void ConfigureIconImporter(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }

        /// <summary>正規化座標 (u, v) が図形の内側かどうか。原点は左下。</summary>
        private static bool IsInsideShape(IconShape shape, float u, float v)
        {
            float cx = u - 0.5f;
            float cy = v - 0.5f;
            float radius = Mathf.Sqrt(cx * cx + cy * cy);

            switch (shape)
            {
                case IconShape.DoubleArrowUp:
                    return IsChevron(u, v, 0.42f) || IsChevron(u, v, 0.70f);


                case IconShape.Bolt:
                {
                    // 上半分と下半分で傾きの違う帯を重ねて稲妻にする
                    float upper = 0.62f - (v - 0.5f) * 0.7f;
                    float lower = 0.38f - (v - 0.5f) * 0.7f;
                    if (v >= 0.5f) return Mathf.Abs(u - upper) < 0.10f;
                    return Mathf.Abs(u - lower) < 0.10f;
                }

                case IconShape.ArrowRight:
                {
                    if (u < 0.55f) return Mathf.Abs(cy) < 0.10f && u > 0.12f;
                    return Mathf.Abs(cy) < (0.88f - u) * 0.9f;
                }

                case IconShape.Swirl:
                {
                    // 半径が角度に比例して伸びる線＝渦
                    float angle = Mathf.Atan2(cy, cx);
                    if (angle < 0f) angle += Mathf.PI * 2f;
                    float spiral = 0.05f + angle * 0.055f;
                    return radius < 0.44f && Mathf.Abs(radius - spiral) < 0.05f;
                }

                case IconShape.Eye:
                {
                    // 上下から潰した楕円の輪郭＋中央の瞳
                    float ellipse = (cx * cx) / (0.42f * 0.42f) + (cy * cy) / (0.22f * 0.22f);
                    if (radius < 0.12f) return true;
                    return ellipse > 0.72f && ellipse < 1f;
                }

                case IconShape.Broom:
                {
                    // 斜めの柄と、先端の広がった穂
                    float shaft = Mathf.Abs((v - 0.15f) - (u - 0.15f));
                    if (shaft < 0.07f && u > 0.35f && u < 0.88f) return true;
                    return v < 0.42f && u < 0.42f && Mathf.Abs((v - 0.15f) - (u - 0.15f)) < 0.06f + (0.42f - u) * 0.9f;
                }

                default:
                    return radius < 0.4f;
            }
        }

        /// <summary>
        /// 上向きの山形（∧）。二重矢印の1本ぶん。
        /// 中心が最も高く、外側へ行くほど下がる。逆にすると下向き（∨）になる。
        /// </summary>
        private static bool IsChevron(float u, float v, float baseHeight)
        {
            float distance = Mathf.Abs(u - 0.5f);
            if (distance > 0.34f) return false;

            float edge = baseHeight - distance;
            return v > edge && v < edge + 0.11f;
        }

        private static Material CreateMaterial(string name, Color color)
        {
            string path = $"{GameRoot}/Materials/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            EditorUtility.SetDirty(material);

            return material;
        }

        /// <summary>
        /// プレイヤー用の物理マテリアル。PlayerController が接地状況で使い分ける。
        /// friction=0 の方は「空中で壁に押し付けても摩擦で落下が止まらない」ため、
        /// friction あり の方は「坂の上で静止できる」ためのもの。
        /// </summary>
        private static PhysicsMaterial CreatePlayerPhysicsMaterial(string name, float friction, PhysicsMaterialCombine combine)
        {
            // AssetDatabase.CreateAsset は .physicsMaterial 拡張子を受け付けないため .asset で作る
            string path = $"{GameRoot}/Materials/{name}.asset";
            PhysicsMaterial material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);

            if (material == null)
            {
                material = new PhysicsMaterial(name);
                AssetDatabase.CreateAsset(material, path);
            }

            material.dynamicFriction = friction;
            material.staticFriction = friction;
            material.bounciness = 0f;
            material.frictionCombine = combine;
            material.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(material);

            return material;
        }

        /// <summary>
        /// Modular Arena の石材マテリアルを複製して自分の管理下に置く。
        /// 元アセットを直接使うと、色やタイリングの調整が外部アセットへの変更になってしまうため。
        /// </summary>
        private static Material CloneArenaMaterial(string sourceName, string newName)
        {
            string path = $"{GameRoot}/Materials/{newName}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            Material source = AssetDatabase.LoadAssetAtPath<Material>($"{ArenaMaterials}/{sourceName}.mat");
            if (source == null)
            {
                Debug.LogError($"[MagicHand] Modular Arena のマテリアルが見つかりません: {sourceName}");
                return CreateMaterial(newName, Color.gray);
            }

            material = new Material(source);
            AssetDatabase.CreateAsset(material, path);
            EditorUtility.SetDirty(material);

            return material;
        }

        /// <summary>引き伸ばしたプリミティブに、実寸に合ったテクスチャ繰り返しを設定する。</summary>
        private static void ApplyTiling(GameObject target, float meshUnitSize, bool verticalUsesHeight, float unitsPerTile = 3f)
        {
            if (target == null || target.GetComponent<Renderer>() == null) return;

            WorldScaleTiling tiling = target.AddComponent<WorldScaleTiling>();
            SetFloat(tiling, "unitsPerTile", unitsPerTile);
            SetFloat(tiling, "meshUnitSize", meshUnitSize);
            SetBool(tiling, "verticalUsesHeight", verticalUsesHeight);
        }

        /// <summary>
        /// アリーナを取り囲むコロッセオ。段状に外へ広がる客席の壁で「闘技場の中心にステージがある」形にする。
        ///
        /// プレイヤーは決してここへ来られないので、すべてのコライダーを外して物理から除外する。
        /// 見た目のためだけに数百のメッシュコライダーを抱えるのは無駄なため。
        /// </summary>
        private static void BuildColosseum(Scene scene, Material sandMaterial)
        {
            GameObject root = NewGameObject(scene, "Colosseum");

            // ステージの外へ広がる砂地。ステージ床(y=0)と重ならないよう少しだけ下げる
            GameObject sand = CreatePrimitive(PrimitiveType.Plane, "Colosseum_Sand", root.transform, sandMaterial);
            sand.transform.position = new Vector3(0f, -0.08f, 0f);
            sand.transform.localScale = new Vector3(14f, 1f, 14f);
            Object.DestroyImmediate(sand.GetComponent<Collider>());
            ApplyTiling(sand, 10f, false, 4f);

            // 下段は無地の壁、上段はアーチ窓にして闘技場の外壁らしい表情を出す
            string[] tierPrefabs = { "Wall_A_3x3", "Wall_A_3x3_Window_A", "Wall_A_3x3_Window_B" };

            for (int tier = 0; tier < tierPrefabs.Length; tier++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{ArenaWalls}/{tierPrefabs[tier]}.prefab");
                if (prefab == null)
                {
                    Debug.LogError($"[MagicHand] コロッセオ用プレハブが見つかりません: {tierPrefabs[tier]}");
                    continue;
                }

                // 外へ行くほど高くなる、階段状の客席
                float radius = ColosseumInnerRadius + tier * 4f;
                float baseHeight = ColosseumBaseHeight + tier * ColosseumModuleHeight;

                GameObject tierRoot = NewChild($"Tier_{tier}", root.transform);
                BuildColosseumRing(tierRoot.transform, prefab, radius, baseHeight);
            }
        }

        private static void BuildColosseumRing(Transform parent, GameObject prefab, float radius, float baseHeight)
        {
            int count = Mathf.Max(12, Mathf.RoundToInt(2f * Mathf.PI * radius / ColosseumModuleWidth));

            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / count;
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                GameObject piece = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                piece.transform.position = outward * radius + Vector3.up * baseHeight;
                piece.transform.rotation = Quaternion.LookRotation(outward, Vector3.up);

                // 装飾なので当たり判定は持たせない
                foreach (Collider collider in piece.GetComponentsInChildren<Collider>())
                {
                    Object.DestroyImmediate(collider);
                }
            }
        }

        /// <summary>壁越しに描くマーカー用のマテリアル。深度テストを無視する専用シェーダーを使う。</summary>
        private static Material CreateXRayMaterial(string name, Color color)
        {
            string path = $"{GameRoot}/Materials/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                Shader shader = Shader.Find("MagicHand/XRayMarker");
                if (shader == null)
                {
                    Debug.LogError("[MagicHand] XRayMarker シェーダーが見つかりません");
                    return CreateUnlitMaterial(name, color);
                }

                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(material);

            return material;
        }

        /// <summary>索敵スクロールが出す、対象に付いてくるマーカーのプレハブ。</summary>
        private static RevealMarker CreateRevealMarkerPrefab(string name, Material material)
        {
            string path = $"{GameRoot}/Prefabs/{name}.prefab";

            GameObject root = new GameObject(name);

            // 逆さの四角錐に見えるよう、立方体を傾けて縦に潰す
            GameObject visual = CreatePrimitive(PrimitiveType.Cube, "Visual", root.transform, material);
            visual.transform.localScale = new Vector3(0.55f, 0.55f, 0.55f);
            visual.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
            Object.DestroyImmediate(visual.GetComponent<Collider>());

            root.AddComponent<RevealMarker>();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            return saved.GetComponent<RevealMarker>();
        }

        /// <summary>
        /// アイテム発動の瞬間に出す輪のプレハブ。
        /// 範囲円（ScrollRangeIndicator）と同じ「ローカルXY平面＝地面」の向きに倒して置く。
        /// </summary>
        private static CastEffect CreateCastEffectPrefab(string name, Material material)
        {
            string path = $"{GameRoot}/Prefabs/{name}.prefab";

            GameObject root = new GameObject(name);
            root.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            LineRenderer ring = root.AddComponent<LineRenderer>();
            ring.sharedMaterial = material;
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.alignment = LineAlignment.TransformZ;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;

            root.AddComponent<CastEffect>();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            return saved.GetComponent<CastEffect>();
        }

        /// <summary>
        /// サーチ発動時に出す走査波動のプレハブ。
        /// 互いに直交する3枚の輪（前後・左右・上下）を子に持たせ、同じ半径で同時に広げることで
        /// 特定の向きだけでなく全方位へ広がる「3D同心円」に見せる。
        /// </summary>
        private static SearchWaveEffect CreateSearchWaveEffectPrefab(string name, Material material)
        {
            string path = $"{GameRoot}/Prefabs/{name}.prefab";

            GameObject root = new GameObject(name);
            var rings = new LineRenderer[3];

            // 正面向き（法線=前後）・横向き（法線=左右）・地面向き（法線=上下）の3枚
            Quaternion[] rotations =
            {
                Quaternion.identity,
                Quaternion.Euler(0f, 90f, 0f),
                Quaternion.Euler(90f, 0f, 0f)
            };

            for (int i = 0; i < rotations.Length; i++)
            {
                GameObject go = NewChild($"Ring_{i}", root.transform);
                go.transform.localRotation = rotations[i];

                LineRenderer ring = go.AddComponent<LineRenderer>();
                ring.sharedMaterial = material;
                ring.useWorldSpace = false;
                ring.loop = true;
                ring.alignment = LineAlignment.TransformZ;
                ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ring.receiveShadows = false;

                rings[i] = ring;
            }

            SearchWaveEffect effect = root.AddComponent<SearchWaveEffect>();
            SetList(effect, "rings", System.Array.ConvertAll(rings, r => (Object)r));

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            return saved.GetComponent<SearchWaveEffect>();
        }

        /// <summary>範囲円のような「光って見せたい／影の影響を受けたくない」表示用のマテリアル。</summary>
        private static Material CreateUnlitMaterial(string name, Color color)
        {
            string path = $"{GameRoot}/Materials/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            EditorUtility.SetDirty(material);

            return material;
        }

        private static void EnsureFolders()
        {
            string[] folders = { "Scenes", "Prefabs", "Materials", "ScriptableObjects", "Input", "Scripts", "Editor", "Animations", "Shaders", "Textures" };

            if (!AssetDatabase.IsValidFolder(GameRoot)) AssetDatabase.CreateFolder("Assets", "_Game");
            foreach (string folder in folders)
            {
                if (!AssetDatabase.IsValidFolder($"{GameRoot}/{folder}"))
                {
                    AssetDatabase.CreateFolder(GameRoot, folder);
                }
            }
        }

        private static void RegisterInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == ScenePath)) return;

            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ---- ヘルパー -------------------------------------------------------

        private static GameObject NewGameObject(Scene scene, string name)
        {
            GameObject go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        private static GameObject NewChild(string name, Transform parent)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        private static GameObject CreatePrimitive(PrimitiveType type, string name, Transform parent, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        private static GameObject CreateBox(string name, Transform parent, Material material, Vector3 center, Vector3 size)
        {
            GameObject go = CreatePrimitive(PrimitiveType.Cube, name, parent, material);
            go.transform.position = center;
            go.transform.localScale = size;

            // 石材テクスチャは1m単位で作られているので、引き伸ばした分だけ繰り返させる。
            // 縦に高い箱（壁・柱）は高さを、平たい箱（床・足場）は奥行きを縦方向に使う
            ApplyTiling(go, 1f, size.y >= size.z);

            return go;
        }

        private static GameObject NewUIChild(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return go;
        }

        private static void AddBackground(GameObject target, Color color)
        {
            Image image = target.AddComponent<Image>();
            image.color = color;
        }

        private static Font BuiltinFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return font;
        }

        private static Text CreateText(Transform parent, string name, string content, int size,
                                       TextAnchor alignment, Vector2 anchorMin, Vector2 anchorMax)
        {
            GameObject go = NewUIChild(name, parent, anchorMin, anchorMax);

            Text text = go.AddComponent<Text>();
            text.text = content;
            text.font = BuiltinFont();
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            return text;
        }

        /// <summary>
        /// 今の手の表示。手の色で塗った帯の上に大きく出す。
        ///
        /// 「HAND:」という見出しは付けない。色と文字だけで通じるうえ、
        /// 分割画面では横幅が半分しかなく、見出しに割く余地が無い。
        /// </summary>
        /// <summary>
        /// 今の手の表示。持ちアイテムの枠（BuildItemBox）と同じ「枠＋アイコン＋名前」の見た目にする。
        /// 頭上の相手向け表示が形で見分けさせる仕組みになったので、自分向けのこちらも
        /// 色の帯だけに頼らずアイコンと文字の両方でひと目に分かるようにした。
        /// </summary>
        private static void BuildHandDisplay(Transform parent, out Image frame, out Image icon, out Text label)
        {
            GameObject root = NewUIChild("HandDisplay", parent, new Vector2(0.03f, 0.03f), new Vector2(0.34f, 0.20f));
            AddBackground(root, new Color(0.04f, 0.04f, 0.07f, 0.65f));

            GameObject border = NewUIChild("Frame", root.transform, new Vector2(0.06f, 0.28f), new Vector2(0.94f, 0.96f));
            frame = border.AddComponent<Image>();
            frame.color = new Color(0.6f, 0.6f, 0.6f, 0.6f);
            frame.raycastTarget = false;

            // 手の色を「縁だけ」にするため、内側にパネルと同じ暗色の板を重ねて中身を隠す。
            // アイコンはこの板の上に置くので、外周にだけ色の帯が残って見える
            GameObject innerGo = NewUIChild("FrameInner", border.transform, new Vector2(0.07f, 0.07f), new Vector2(0.93f, 0.93f));
            Image inner = innerGo.AddComponent<Image>();
            inner.color = new Color(0.04f, 0.04f, 0.07f, 0.92f);
            inner.raycastTarget = false;

            GameObject iconGo = NewUIChild("Icon", innerGo.transform, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.96f));
            icon = iconGo.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.enabled = false;

            label = CreateText(root.transform, "HandText", "？", 26, TextAnchor.MiddleCenter,
                               new Vector2(0f, 0.02f), new Vector2(1f, 0.27f));
        }

        /// <summary>
        /// 持ちアイテムの枠。マリオカートのアイテム枠と同じで、絵の下に名前を置く。
        /// 何も持っていないときも枠は残す。出たり消えたりすると視線がそのたびに引っぱられるため。
        /// </summary>
        private static void BuildItemBox(Transform parent, out Image frame, out Image icon, out Text label, out Text unusableMark)
        {
            GameObject root = NewUIChild("ItemBox", parent, new Vector2(0.66f, 0.03f), new Vector2(0.97f, 0.20f));
            AddBackground(root, new Color(0.04f, 0.04f, 0.07f, 0.65f));

            // 枠の縁をアイテムの色で塗る。地面に落ちている巻物や球の色と対応が取れる
            GameObject border = NewUIChild("Frame", root.transform, new Vector2(0.06f, 0.28f), new Vector2(0.94f, 0.96f));
            frame = border.AddComponent<Image>();
            frame.color = new Color(1f, 1f, 1f, 0.25f);
            frame.raycastTarget = false;

            GameObject iconGo = NewUIChild("Icon", border.transform, new Vector2(0.12f, 0.12f), new Vector2(0.88f, 0.88f));
            icon = iconGo.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.enabled = false;

            // 持っているのに今は使えない（スタン中でワープ以外、等）ときだけ絵の上に重ねる
            unusableMark = CreateText(border.transform, "UnusableMark", "✕", 54, TextAnchor.MiddleCenter,
                                      Vector2.zero, Vector2.one);
            unusableMark.color = new Color(1f, 0.25f, 0.25f, 0.95f);
            AddTextOutline(unusableMark);
            unusableMark.gameObject.SetActive(false);

            label = CreateText(root.transform, "ItemName", "なし", 26, TextAnchor.MiddleCenter,
                               new Vector2(0f, 0.02f), new Vector2(1f, 0.27f));
        }

        /// <summary>かかっている効果の行。アイテム枠の上に積む。</summary>
        private static Text[] BuildStatusRows(Transform parent)
        {
            const int rowCount = 4;
            var rows = new Text[rowCount];

            for (int i = 0; i < rowCount; i++)
            {
                float bottom = 0.22f + i * 0.075f;

                // 「いちがバレている」のように長い名前も入るので、左端は広めに取る
                rows[i] = CreateText(parent, $"StatusRow_{i}", string.Empty, 44, TextAnchor.LowerRight,
                                     new Vector2(0.14f, bottom), new Vector2(0.97f, bottom + 0.07f));
                rows[i].color = new Color(0.75f, 0.95f, 1f);
                rows[i].enabled = false;
            }

            return rows;
        }

        /// <summary>
        /// サーチ中、画面外の相手を指す矢印。三角形のスプライトもここで作る。
        /// </summary>
        private static void BuildOffscreenArrow(Transform parent, PlayerController player, Camera viewCamera)
        {
            GameObject go = NewUIChild("OffscreenArrow", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(54f, 54f);

            Image image = go.AddComponent<Image>();
            image.sprite = CreateIcon("Icon_Arrow", IconShape.ArrowRight, Color.white);
            image.color = new Color(1f, 0.25f, 0.25f, 0.9f);
            image.preserveAspect = true;
            image.raycastTarget = false;
            go.SetActive(false);

            OffscreenTargetArrow arrow = parent.gameObject.AddComponent<OffscreenTargetArrow>();
            SetObject(arrow, "player", player);
            SetObject(arrow, "viewCamera", viewCamera);
            SetObject(arrow, "arrow", rect);
            SetObject(arrow, "canvasArea", parent.GetComponent<RectTransform>());
        }

        private const string TitleArtworkPath = GameRoot + "/Textures/UI/Title_Background.jpg";
        private const string BgmTitlePath = GameRoot + "/BGM/game start.mp3";
        private const string BgmPlayPath = GameRoot + "/BGM/game play.mp3";
        private const float BgmVolume = 0.01f;

        private const string SeStartButtonPath = GameRoot + "/BGM/SE/決定ボタンを押す3（スタートボタンの音）.mp3";
        private const string SeCountdownPath = GameRoot + "/BGM/SE/カウントダウン電子音（ゲーム開始時のカウントダウン）.mp3";
        private const string SeDefeatPath = GameRoot + "/BGM/SE/スタジアムの歓声2（相手にぶつかった時）.mp3";
        private const string SeDrawPath = GameRoot + "/BGM/SE/ロボットを強く殴る2（あいこの時）.mp3";
        private const string SeItemPickupPath = GameRoot + "/BGM/SE/食べ物をパクッ（アイテム取得音）.mp3";
        private const string SeStunPath = GameRoot + "/BGM/SE/足首がグキッ（スタン）.mp3";
        private const string SeBlinkPath = GameRoot + "/BGM/SE/俊敏15（ブリンク）.mp3";
        private const string SeSpeedUpPath = GameRoot + "/BGM/SE/俊敏11（スピードアップ）.mp3";
        private const string SeCharmPath = GameRoot + "/BGM/SE/回想（チャーム）.mp3";
        private const string SeBroomPath = GameRoot + "/BGM/SE/シャキーン2（ほうきを装備）.mp3";
        private const float SeVolume = 0.15f;

        /// <summary>3-2-1のカウントダウン音だけ他のSEより耳につくという指摘で、専用に下げた音量。</summary>
        private const float SeCountdownVolume = 0.01f;

        private const string GuVisualPrefabPath = "Assets/Te/gu-.prefab";
        private const string ChokiVisualPrefabPath = "Assets/Te/choki.prefab";
        private const string PaVisualPrefabPath = "Assets/Te/pa-.prefab";

        /// <summary>
        /// タイトルの1枚絵を全面に敷く。
        ///
        /// 画面比に合わせて引き伸ばすと絵が歪むので、`preserveAspect` で縦横比を保つ。
        /// 元画像は 1380×752 の横長で、画面より縦長になると上下に余白が出るため、
        /// 後ろに絵の空の色に近い暗色を敷いて余白を目立たなくしている。
        /// </summary>
        private static void AddTitleArtwork(GameObject panel)
        {
            AddBackground(panel, new Color(0.06f, 0.07f, 0.10f, 1f));

            Sprite artwork = LoadTitleArtwork();
            if (artwork == null)
            {
                Debug.LogWarning($"[MagicHand] タイトルの背景画像が見つかりません: {TitleArtworkPath}");
                return;
            }

            GameObject go = NewUIChild("Artwork", panel.transform, Vector2.zero, Vector2.one);

            Image image = go.AddComponent<Image>();
            image.sprite = artwork;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        /// <summary>タイトルの絵をスプライトとして読む。取り込み設定が未設定なら整えてから読み直す。</summary>
        private static Sprite LoadTitleArtwork()
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TitleArtworkPath);
            if (sprite != null) return sprite;

            var importer = (TextureImporter)AssetImporter.GetAtPath(TitleArtworkPath);
            if (importer == null) return null;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;

            // 1380px を縮めると文字が潰れるので、原寸のまま扱えるサイズ上限にする
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(TitleArtworkPath);
        }

        /// <summary>絵の上に文字を重ねるときの縁取り。帯を敷かずに読ませるため。</summary>
        private static void AddTextOutline(Text text)
        {
            Outline outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);
        }

        /// <summary>
        /// 見た目を持たない当たり判定だけのボタン。
        /// 絵に描かれた台座をそのまま押せるようにするために使う。
        /// </summary>
        private static Button CreateInvisibleButton(Transform parent, string name,
                                                    Vector2 anchorMin, Vector2 anchorMax)
        {
            GameObject go = NewUIChild(name, parent, anchorMin, anchorMax);

            // 完全な透明だとクリックを拾えないので、限りなく薄い色を置く
            Image hit = go.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0.004f);

            Button button = go.AddComponent<Button>();
            button.targetGraphic = hit;

            return button;
        }

        // ---- SerializedObject による private フィールド設定 -------------------

        private static void SetObject(Object target, string path, Object value)
            => Apply(target, path, p => p.objectReferenceValue = value);

        private static void SetInt(Object target, string path, int value)
            => Apply(target, path, p => p.intValue = value);

        private static void SetEnum(Object target, string path, int value)
            => Apply(target, path, p => p.enumValueIndex = value);

        private static void SetFloat(Object target, string path, float value)
            => Apply(target, path, p => p.floatValue = value);

        private static void SetString(Object target, string path, string value)
            => Apply(target, path, p => p.stringValue = value);

        private static void SetColor(Object target, string path, Color value)
            => Apply(target, path, p => p.colorValue = value);

        private static void SetBool(Object target, string path, bool value)
            => Apply(target, path, p => p.boolValue = value);

        private static void SetVector(Object target, string path, Vector3 value)
            => Apply(target, path, p => p.vector3Value = value);


        private static void SetList(Object target, string path, Object[] values)
        {
            Apply(target, path, p =>
            {
                p.arraySize = values.Length;
                for (int i = 0; i < values.Length; i++)
                {
                    p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
                }
            });
        }

        private static void Apply(Object target, string path, System.Action<SerializedProperty> action)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(path);

            if (property == null)
            {
                Debug.LogError($"[MagicHand] フィールドが見つかりません: {target.GetType().Name}.{path}");
                return;
            }

            action(property);
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
