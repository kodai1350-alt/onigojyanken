using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// 何か時間制限つきの効果がかかっている間、足元に輪を出し続ける。
    ///
    /// スタンや減速は動きの変化だけでは分かりにくく、何が起きているのか本人にも相手にも
    /// 伝わりづらい。発動の瞬間だけの演出（CastEffect）とは別に、効果が続いている間ずっと
    /// 見える形にしてある。ScrollRangeIndicator と違い自分だけに見せる理由が無いので、
    /// レイヤーは絞らず両プレイヤーから見える。
    ///
    /// 複数の効果が重なっているときは、残り時間が最も長い（HUDの一覧で一番上に出ている）
    /// ものの色を使う。
    ///
    /// スピードUpとスタンは専用の演出（SpeedUpEffect の矢印、StunEffect の稲妻）を持つので、
    /// ここでは候補から外す。
    ///
    /// サーチされている（Searched）も候補から外す。これは「見られている」という
    /// HUDだけの通知（本人にしか出ない）で、世界空間の輪を足元に出してしまうと
    /// 索敵側からもその場所が見えてしまい、壁越しでない索敵情報が漏れてしまうため。
    /// それ以外に何も掛かっていなければ輪は出さない。
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class StatusAura : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private LineRenderer ring;

        [Header("Ring")]
        [SerializeField, Range(12, 128)] private int segments = 40;
        [SerializeField] private float radius = 0.7f;
        [SerializeField] private float groundOffset = 0.05f;
        [SerializeField] private float spinSpeed = 60f;

        private MaterialPropertyBlock block;
        private bool visible;

        private void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (ring == null) ring = GetComponent<LineRenderer>();

            block = new MaterialPropertyBlock();

            // ScrollRangeIndicator と同じ向き。ローカルXY平面を地面に倒して寝かせる
            transform.localPosition = new Vector3(0f, groundOffset, 0f);
            transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            ring.positionCount = segments;
            BuildCircle();
        }

        private void Update()
        {
            PlayerStatusEffects status = player != null ? player.Status : null;
            Color color = default;
            bool shouldShow = status != null
                              && status.TryGetPrimaryColor(out color, PlayerStatusEffects.SpeedUp,
                                                            PlayerStatusEffects.Stun, PlayerStatusEffects.Searched);

            if (shouldShow != visible)
            {
                visible = shouldShow;
                ring.enabled = visible;
            }

            if (!visible) return;

            ring.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            ring.SetPropertyBlock(block);

            // 地面に寝かせた状態でのローカルZが鉛直軸にあたるので、これを軸に回すと平らに回転する
            transform.Rotate(Vector3.forward, spinSpeed * Time.deltaTime, Space.Self);
        }

        private void BuildCircle()
        {
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
            }
        }
    }
}
