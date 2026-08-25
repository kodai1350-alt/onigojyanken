using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// ワープをストックしている間、着地点に薄い輪を出す。
    ///
    /// ワープは撃つまでどこへ飛ぶか分からず、壁の手前で止まる仕様も相まって
    /// 「思ったより飛ばなかった」が起きやすい。事前に見えれば向きを直してから撃てる。
    ///
    /// 着地点は必ず PlayerController.ResolveTeleportTarget から取る。
    /// ここで独自に計算すると、表示と実際の着地点がずれて嘘の情報になる。
    ///
    /// 分割画面で相手に手の内が見えないよう、この GameObject には
    /// 「そのプレイヤーのカメラだけが映すレイヤー」を割り当てる（シーン生成側で設定）。
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class BlinkTargetIndicator : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private LineRenderer ring;

        [Header("Ring")]
        [SerializeField, Min(0.2f)] private float radius = 0.7f;
        [SerializeField, Range(12, 128)] private int segments = 40;
        [SerializeField] private float lineWidth = 0.12f;

        [Tooltip("着地点の地面からどれだけ浮かせるか")]
        [SerializeField] private float groundOffset = 0.1f;

        [Tooltip("薄く出すための不透明度")]
        [SerializeField, Range(0.05f, 1f)] private float alpha = 0.45f;

        private MaterialPropertyBlock block;
        private bool built;

        private void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (ring == null) ring = GetComponent<LineRenderer>();

            block = new MaterialPropertyBlock();

            // 輪は自分のローカルXY平面に描く。X+90度倒すと水平な輪になる。
            // ScrollRangeIndicator と同じ理屈
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            ring.useWorldSpace = false;
            ring.loop = true;
            ring.alignment = LineAlignment.TransformZ;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.widthMultiplier = lineWidth;
            ring.enabled = false;
        }

        private void LateUpdate()
        {
            if (player == null || ring == null) return;

            TeleportEffectSO blink = ResolveStockedBlink();
            bool show = blink != null && player.CanAct;

            if (!show)
            {
                if (ring.enabled) ring.enabled = false;
                return;
            }

            if (!built) BuildCircle();

            // 親の回転に引きずられないよう、位置だけ合わせて向きは水平に固定する
            Vector3 target = player.ResolveTeleportTarget(blink.Distance, blink.BlockMask);
            transform.position = target + Vector3.up * groundOffset;
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            if (!ring.enabled)
            {
                Color color = blink.DisplayColor;
                color.a = alpha;

                ring.GetPropertyBlock(block);
                block.SetColor("_BaseColor", color);
                block.SetColor("_Color", color);
                ring.SetPropertyBlock(block);

                ring.enabled = true;
            }
        }

        private TeleportEffectSO ResolveStockedBlink()
        {
            ScrollStock stock = player.Scrolls;
            return stock != null ? stock.Current as TeleportEffectSO : null;
        }

        private void BuildCircle()
        {
            ring.positionCount = segments;

            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
            }

            built = true;
        }
    }
}
