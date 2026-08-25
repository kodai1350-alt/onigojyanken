using System.Collections.Generic;
using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// スポーングループ1つ分の設定。手変更用／スクロール用など、
    /// スポーン地点・目標維持数・抽選テーブルを種別ごとに分けて持つ。
    /// </summary>
    [System.Serializable]
    public class ItemSpawnGroup
    {
        public string groupName = "Group";

        [Tooltip("この Transform の子がスポーン地点として使われる（spawnPoints が空の場合）")]
        public Transform spawnRoot;

        public List<Transform> spawnPoints = new List<Transform>();

        [Tooltip("このグループから湧くアイテムの抽選候補")]
        public List<ItemDefinitionSO> lootTable = new List<ItemDefinitionSO>();

        [Tooltip("常にマップ上に維持したい個数")]
        [Min(0)] public int targetCount = 5;

        [Min(0f)] public float minRespawnDelay = 15f;
        [Min(0f)] public float maxRespawnDelay = 20f;

        [Tooltip("取得されてから次が湧くまでの最低待ち時間。0なら通常の補充間隔だけで湧く")]
        [Min(0f)] public float collectCooldown;

        [Header("Guaranteed Slot")]
        [Tooltip("このグループの中で常に一定数だけ確保しておきたい特別な種類（例：ほうきを25個中1個は必ず出す）")]
        public ItemDefinitionSO guaranteedItem;

        [Tooltip("guaranteedItem を維持する数。0なら特別扱いせず、通常の抽選候補として扱う")]
        [Min(0)] public int guaranteedCount;

        [Tooltip("準備ルームの設定パネルにこのグループの種類を並べるか。手変更は常に必要な基本アイテムなのでオフにしてある")]
        public bool includeInSettings = true;
    }

    /// <summary>
    /// アイテムの「常時一定数維持」方式のスポーン管理。
    /// グループごとに targetCount を満たすよう、空いているスポーン地点へ補充する。
    /// </summary>
    public class ItemSpawnManager : MonoBehaviour
    {
        [Header("Prefab")]
        [SerializeField] private ItemPickup itemPrefab;

        [Header("Groups")]
        [SerializeField]
        private List<ItemSpawnGroup> groups = new List<ItemSpawnGroup>();

        [Header("Player Clearance")]
        [Tooltip("プレイヤーからこの距離以内には湧かせない。湧いた足元でいきなり拾えてしまうのを防ぐ")]
        [SerializeField, Min(0f)] private float playerClearRadius = 7f;

        [Header("Item Clearance")]
        [Tooltip("他の湧いているアイテム（手変更・アイテム両グループとも）からこの距離以内は" +
                 "できるだけ避ける。空き地点が無ければ諦めて通常通り選ぶ（常時数の維持を優先する）")]
        [SerializeField, Min(0f)] private float itemClearRadius = 8f;

        private readonly Dictionary<ItemSpawnGroup, Dictionary<Transform, ItemPickup>> occupancy
            = new Dictionary<ItemSpawnGroup, Dictionary<Transform, ItemPickup>>();
        private readonly Dictionary<ItemSpawnGroup, float> nextSpawnTime
            = new Dictionary<ItemSpawnGroup, float>();
        private readonly List<Transform> freePointBuffer = new List<Transform>();
        private readonly List<Transform> preferredPointBuffer = new List<Transform>();
        private readonly List<ItemDefinitionSO> enabledLootBuffer = new List<ItemDefinitionSO>();
        private readonly List<ItemDefinitionSO> leastRepresentedBuffer = new List<ItemDefinitionSO>();

        private bool running;

        private void Awake()
        {
            foreach (ItemSpawnGroup group in groups)
            {
                ResolveSpawnPoints(group);
                occupancy[group] = new Dictionary<Transform, ItemPickup>();
                nextSpawnTime[group] = 0f;
            }
        }

        private static void ResolveSpawnPoints(ItemSpawnGroup group)
        {
            if (group.spawnPoints.Count > 0 || group.spawnRoot == null) return;

            foreach (Transform child in group.spawnRoot)
            {
                group.spawnPoints.Add(child);
            }
        }

        /// <summary>試合開始時に呼ぶ。目標数まで即座に埋めてから維持を始める。</summary>
        public void BeginSpawning()
        {
            ClearAll();
            running = true;

            foreach (ItemSpawnGroup group in groups)
            {
                for (int i = CountAlive(group); i < group.targetCount; i++)
                {
                    if (!SpawnNextForGroup(group)) break;
                }
                nextSpawnTime[group] = Time.time + RandomDelay(group);
            }
        }

        /// <summary>試合終了時に呼ぶ。補充を止めて場のアイテムを片付ける。</summary>
        public void StopSpawning()
        {
            running = false;
            ClearAll();
        }

        private void Update()
        {
            if (!running) return;

            foreach (ItemSpawnGroup group in groups)
            {
                if (CountAlive(group) >= group.targetCount) continue;
                if (Time.time < nextSpawnTime[group]) continue;

                SpawnNextForGroup(group);
                nextSpawnTime[group] = Time.time + RandomDelay(group);
            }
        }

        private float RandomDelay(ItemSpawnGroup group)
        {
            float min = Mathf.Min(group.minRespawnDelay, group.maxRespawnDelay);
            float max = Mathf.Max(group.minRespawnDelay, group.maxRespawnDelay);
            return Random.Range(min, max);
        }

        private int CountAlive(ItemSpawnGroup group)
        {
            Dictionary<Transform, ItemPickup> map = occupancy[group];
            int alive = 0;

            foreach (KeyValuePair<Transform, ItemPickup> pair in map)
            {
                if (pair.Value != null) alive++;
            }

            return alive;
        }

        private int CountAliveOfDefinition(ItemSpawnGroup group, ItemDefinitionSO definition)
        {
            if (definition == null) return 0;

            Dictionary<Transform, ItemPickup> map = occupancy[group];
            int alive = 0;

            foreach (KeyValuePair<Transform, ItemPickup> pair in map)
            {
                if (pair.Value != null && pair.Value.Definition == definition) alive++;
            }

            return alive;
        }

        /// <summary>
        /// このグループへ次の1個を湧かせる。
        ///
        /// 保証枠（例：ほうき）が足りていなければそれを優先して湧かせ、
        /// 満たしていれば通常の抽選に任せる。呼び出し側（一斉補充と毎フレームの補充）が
        /// 共通してこれを使うことで、「保証数を優先する」というルールを一箇所にまとめている。
        /// </summary>
        private bool SpawnNextForGroup(ItemSpawnGroup group)
        {
            bool needsGuaranteed = group.guaranteedItem != null
                                   && CountAliveOfDefinition(group, group.guaranteedItem) < group.guaranteedCount
                                   && (MatchSettings.Instance == null || MatchSettings.Instance.IsItemEnabled(group.guaranteedItem));

            return SpawnOne(group, needsGuaranteed ? group.guaranteedItem : null);
        }

        private bool SpawnOne(ItemSpawnGroup group, ItemDefinitionSO forcedDefinition = null)
        {
            if (itemPrefab == null) return false;

            ItemDefinitionSO definition = forcedDefinition;
            if (definition == null)
            {
                if (group.lootTable.Count == 0) return false;
                definition = PickEnabledLoot(group);
                if (definition == null) return false;
            }

            Transform point = PickFreePoint(group);
            if (point == null) return false;

            ItemPickup pickup = Instantiate(itemPrefab, point.position, point.rotation, transform);
            pickup.name = $"Item_{definition.DisplayName}";
            pickup.Initialize(definition);
            pickup.Collected += _ =>
            {
                occupancy[group][point] = null;

                // 「取得されてから何秒後に湧くか」はここでしか測れない。
                // 補充間隔だけだと、拾われた瞬間に前回の待ちが明けていて即座に湧いてしまう
                if (group.collectCooldown > 0f)
                {
                    nextSpawnTime[group] = Mathf.Max(nextSpawnTime[group], Time.time + group.collectCooldown);
                }
            };

            occupancy[group][point] = pickup;
            return true;
        }

        /// <summary>
        /// 準備ルームで無効にされた種類を除いた中から抽選する。
        /// 全部無効にされたグループは何も湧かない（呼び出し側は false 扱い）。
        ///
        /// 単純な均等抽選だと、運が偏ると特定の種類（グー/チョキ/パーなど）だけが
        /// マップ上に多く／少なく残り続けることがある。今マップ上にいちばん少ない種類だけに
        /// 絞ってから選ぶことで、種類ごとの個数が大きくは偏らないようにする。
        /// </summary>
        private ItemDefinitionSO PickEnabledLoot(ItemSpawnGroup group)
        {
            enabledLootBuffer.Clear();

            foreach (ItemDefinitionSO candidate in group.lootTable)
            {
                if (candidate == null) continue;
                if (MatchSettings.Instance != null && !MatchSettings.Instance.IsItemEnabled(candidate)) continue;
                enabledLootBuffer.Add(candidate);
            }

            if (enabledLootBuffer.Count == 0) return null;

            int minCount = int.MaxValue;
            foreach (ItemDefinitionSO candidate in enabledLootBuffer)
            {
                int count = CountAliveOfDefinition(group, candidate);
                if (count < minCount) minCount = count;
            }

            leastRepresentedBuffer.Clear();
            foreach (ItemDefinitionSO candidate in enabledLootBuffer)
            {
                if (CountAliveOfDefinition(group, candidate) == minCount) leastRepresentedBuffer.Add(candidate);
            }

            return leastRepresentedBuffer[Random.Range(0, leastRepresentedBuffer.Count)];
        }

        /// <summary>
        /// 設定パネルに並べるため、全グループの抽選候補を MatchSettings へ登録する。
        ///
        /// <see cref="RandomScrollSO"/>（拾ったときに中身を抽選する巻物）は湧きの抽選テーブルには
        /// 1個だけ入っているが、設定パネルには中身の候補（5種類）をそれぞれ個別に出したいので、
        /// ラッパーそのものではなく Candidates を展開して登録する。
        /// </summary>
        public void RegisterLootWithSettings(MatchSettings settings)
        {
            if (settings == null) return;

            foreach (ItemSpawnGroup group in groups)
            {
                if (!group.includeInSettings) continue;

                foreach (ItemDefinitionSO item in group.lootTable)
                {
                    if (item is RandomScrollSO random) settings.RegisterItems(random.Candidates);
                    else settings.RegisterItems(new[] { item });
                }

                // 保証枠（ほうき等）は抽選テーブルに入れていないので、ここで別に登録する
                if (group.guaranteedItem != null) settings.RegisterItems(new[] { group.guaranteedItem });
            }
        }

        /// <summary>
        /// 空いている湧き地点を選ぶ。他のアイテム（手変更・アイテム両グループとも）から
        /// 離れた地点を優先し、そういう地点が無ければ諦めて空いている地点から普通に選ぶ
        /// （常に一定数を維持する方を優先するため、完全な排他条件にはしない）。
        /// </summary>
        private Transform PickFreePoint(ItemSpawnGroup group)
        {
            Dictionary<Transform, ItemPickup> map = occupancy[group];
            freePointBuffer.Clear();
            preferredPointBuffer.Clear();

            foreach (Transform point in group.spawnPoints)
            {
                if (point == null) continue;
                if (map.TryGetValue(point, out ItemPickup existing) && existing != null) continue;
                if (IsNearAnyPlayer(point.position)) continue;

                freePointBuffer.Add(point);
                if (!IsNearAnyActiveItem(point.position)) preferredPointBuffer.Add(point);
            }

            if (preferredPointBuffer.Count > 0) return preferredPointBuffer[Random.Range(0, preferredPointBuffer.Count)];
            if (freePointBuffer.Count == 0) return null;
            return freePointBuffer[Random.Range(0, freePointBuffer.Count)];
        }

        /// <summary>
        /// 他に湧いているアイテムの近くかどうか。グループを問わず全アイテムを見る
        /// （手変更のすぐ隣にアイテムが湧く、といった偏りを減らすため）。
        /// </summary>
        private bool IsNearAnyActiveItem(Vector3 position)
        {
            if (itemClearRadius <= 0f) return false;

            float squared = itemClearRadius * itemClearRadius;

            foreach (Dictionary<Transform, ItemPickup> map in occupancy.Values)
            {
                foreach (ItemPickup pickup in map.Values)
                {
                    if (pickup == null) continue;
                    if ((pickup.transform.position - position).sqrMagnitude < squared) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// プレイヤーの近くかどうか。
        /// 湧き位置がそのまま足元だと、リスポーンした側が何もせずアイテムを拾えてしまう。
        /// </summary>
        private bool IsNearAnyPlayer(Vector3 position)
        {
            if (playerClearRadius <= 0f || GameManager.Instance == null) return false;

            float squared = playerClearRadius * playerClearRadius;

            foreach (PlayerController player in GameManager.Instance.Players)
            {
                if (player == null) continue;
                if ((player.transform.position - position).sqrMagnitude < squared) return true;
            }

            return false;
        }

        /// <summary>
        /// 指定位置の周りに出ているアイテムを片付けて、別の場所へ湧かせ直す。
        /// プレイヤーを置いた直後に呼ぶ。除外は湧く瞬間にしか効かないので、
        /// 「先に湧いていた場所へ後からプレイヤーが現れる」ぶんはここで拾う。
        /// </summary>
        public void RelocateAround(Vector3 position)
        {
            if (!running || playerClearRadius <= 0f) return;

            float squared = playerClearRadius * playerClearRadius;

            foreach (ItemSpawnGroup group in groups)
            {
                Dictionary<Transform, ItemPickup> map = occupancy[group];
                int removed = 0;

                foreach (Transform point in new List<Transform>(map.Keys))
                {
                    ItemPickup pickup = map[point];
                    if (pickup == null) continue;
                    if ((point.position - position).sqrMagnitude >= squared) continue;

                    Destroy(pickup.gameObject);
                    map[point] = null;
                    removed++;
                }

                // 減らした数だけその場で湧かせ直す。待ち時間を置くと、
                // 消しただけでマップ上の総数が減ってしまう
                for (int i = 0; i < removed; i++)
                {
                    if (!SpawnNextForGroup(group)) break;
                }
            }
        }

        /// <summary>場に出ているアイテムを全て消す。</summary>
        public void ClearAll()
        {
            foreach (ItemSpawnGroup group in groups)
            {
                Dictionary<Transform, ItemPickup> map = occupancy[group];

                foreach (Transform point in new List<Transform>(map.Keys))
                {
                    if (map[point] != null) Destroy(map[point].gameObject);
                    map[point] = null;
                }
            }
        }
    }
}
