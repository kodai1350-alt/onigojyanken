using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// 範囲を持つスクロール（AreaScrollEffectSO）をストックしている間、
    /// プレイヤーの足元に効果範囲の円を表示する。
    ///
    /// ストックが空、または範囲を持たないスクロール（ワープ等）の場合は非表示。
    /// 分割画面で相手側に手の内が見えないよう、この GameObject には
    /// 「そのプレイヤーのカメラだけが映すレイヤー」を割り当てる（シーン生成側で設定）。
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class ScrollRangeIndicator : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private LineRenderer ring;

        [Header("Ring")]
        [SerializeField, Range(12, 128)] private int segments = 72;
        [SerializeField] private float lineWidth = 0.18f;
        [SerializeField] private float groundOffset = 0.08f;

        private MaterialPropertyBlock block;
        private ScrollEffectSO lastScroll;
        private bool initialized;

        private void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (ring == null) ring = GetComponent<LineRenderer>();

            block = new MaterialPropertyBlock();

            // 円は自分のローカル XY 平面に描く。この GameObject を X+90° 倒すことで
            // ローカルXY＝ワールドの水平面、ローカルZ（＝リボンの法線）＝鉛直 になり、
            // 線が地面に寝た輪として見える。親（プレイヤー）のY回転は円の見た目に影響しない。
            transform.localPosition = new Vector3(0f, groundOffset, 0f);
            transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            ring.useWorldSpace = false;
            ring.loop = true;
            ring.alignment = LineAlignment.TransformZ;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.widthMultiplier = lineWidth;
            ring.enabled = false;

            initialized = true;
        }

        private void Update()
        {
            if (!initialized) return;

            // 実行順に依存しないよう、UI と同じく毎フレーム状態を参照する
            ScrollStock stock = player != null ? player.Scrolls : null;
            ScrollEffectSO current = stock != null ? stock.Current : null;

            if (current == lastScroll) return;
            lastScroll = current;

            Refresh(current);
        }

        private void Refresh(ScrollEffectSO scroll)
        {
            AreaScrollEffectSO area = scroll as AreaScrollEffectSO;

            if (area == null)
            {
                ring.enabled = false;
                return;
            }

            BuildCircle(area.Radius);

            Color color = area.DisplayColor;
            ring.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            ring.SetPropertyBlock(block);

            ring.enabled = true;
        }

        /// <summary>半径 radius の円を、ローカル XY 平面上の頂点として敷き直す。</summary>
        private void BuildCircle(float radius)
        {
            ring.positionCount = segments;

            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
            }
        }
    }
}
