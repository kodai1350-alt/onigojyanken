using System;
using System.Collections;
using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// ステージに置かれるアイテムの汎用コンポーネント。
    /// どの ItemDefinitionSO を積むかは知っていても構わないが、
    /// 手変更／スクロールの「振る舞い」自体は ItemDefinitionSO 側に委譲するため、
    /// 新しいアイテム種別を足してもこのクラスは変更不要。
    ///
    /// 見た目は「グー／チョキ／パー／スクロール（巻物）／ほうき」の5種類をプレハブ内に持たせておき、
    /// 中身の型（手変更なら Hand の値）で ON/OFF を切り替えるだけにしている
    /// （ランタイムでのメッシュ差し替えを避けるため）。
    /// 見た目が要る新種を足すときは、ここへスロットを1つ増やして SetVisualsActive に分岐を1行足す。
    ///
    /// グー・チョキ・パーはそれぞれ専用のモデル（Assets/Te）を使うので形そのもので見分けが付く。
    /// 色による塗り分けは行わない。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ItemPickup : MonoBehaviour
    {
        [Header("Definition")]
        [SerializeField] private ItemDefinitionSO definition;

        [Header("Idle Motion")]
        [SerializeField] private float spinSpeed = 90f;
        [SerializeField] private float bobAmplitude = 0.25f;
        [SerializeField] private float bobSpeed = 2f;

        [Header("Respawn")]
        [Tooltip("準備ルームの見本用。true なら取得されても消滅せず、その場で復活する")]
        [SerializeField] private bool respawnInPlace;
        [SerializeField, Min(0f)] private float respawnDelay = 1f;

        [Header("Visuals (種別ごとに用意し、中身の型で切り替える)")]
        [SerializeField] private GameObject guVisual;
        [SerializeField] private GameObject chokiVisual;
        [SerializeField] private GameObject paVisual;
        [SerializeField] private GameObject scrollVisual;
        [SerializeField] private GameObject broomVisual;

        [Tooltip("ほうきの位置を遠くからでも分かるように出す薄いビーコン。ほうきのときだけ出す")]
        [SerializeField] private GameObject beaconVisual;

        private Vector3 basePosition;
        private float bobOffset;
        private bool collectable = true;

        public ItemDefinitionSO Definition => definition;

        /// <summary>取得されて消滅する直前に発火する。ItemSpawnManager が補充に使う。</summary>
        public event Action<ItemPickup> Collected;

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
            basePosition = transform.position;
            bobOffset = UnityEngine.Random.value * Mathf.PI * 2f;
        }

        private void Start()
        {
            SetVisualsActive(true);
        }

        /// <summary>スポーン時に中身を差し込む。</summary>
        public void Initialize(ItemDefinitionSO newDefinition)
        {
            definition = newDefinition;
            basePosition = transform.position;
            SetVisualsActive(true);
        }

        private void Update()
        {
            transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);

            float bob = Mathf.Sin(Time.time * bobSpeed + bobOffset) * bobAmplitude;
            transform.position = basePosition + Vector3.up * bob;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (definition == null || !collectable) return;

            GameManager manager = GameManager.Instance;
            if (manager == null) return;

            // 試合中に加え、準備ルームでも見本として拾えるようにする
            bool pickupAllowed = manager.CurrentState == GameState.InGame
                                 || (respawnInPlace && manager.CurrentState == GameState.Lobby);
            if (!pickupAllowed) return;

            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null) return;

            // ほうきに乗っている間は拾えない。
            // 空を自由に飛べる上に拾い集めまでできると、飛行が万能の手番になってしまう
            if (player.IsRiding) return;

            // 取得が成立しなかった場合（スクロールのストックが埋まっている等）は場に残す
            if (!definition.TryPickup(player)) return;

            SEPlayer.PlayItemPickup();
            Collected?.Invoke(this);

            if (respawnInPlace) StartCoroutine(RespawnAfterDelay());
            else Destroy(gameObject);
        }

        /// <summary>見本アイテムは消さずに一旦隠し、すぐ復活させて何度でも試せるようにする。</summary>
        private IEnumerator RespawnAfterDelay()
        {
            collectable = false;
            SetVisualsActive(false);

            yield return new WaitForSeconds(respawnDelay);

            SetVisualsActive(true);
            collectable = true;
        }

        /// <summary>中身の型から出すべき見た目を決めて ON/OFF する。</summary>
        private void SetVisualsActive(bool active)
        {
            if (definition == null) return;

            HandType hand = (definition as HandItemSO)?.Hand ?? HandType.None;
            bool isBroom = definition is BroomEffectSO;
            bool isScroll = !isBroom && hand == HandType.None;

            if (guVisual != null) guVisual.SetActive(active && hand == HandType.Gu);
            if (chokiVisual != null) chokiVisual.SetActive(active && hand == HandType.Choki);
            if (paVisual != null) paVisual.SetActive(active && hand == HandType.Pa);
            if (scrollVisual != null) scrollVisual.SetActive(active && isScroll);
            if (broomVisual != null) broomVisual.SetActive(active && isBroom);

            // ビーコンはイージーモード限定。ノーマルは今まで通り出さない
            bool easyMode = MatchSettings.Instance != null && MatchSettings.Instance.EasyMode;
            if (beaconVisual != null) beaconVisual.SetActive(active && isBroom && easyMode);
        }
    }
}
