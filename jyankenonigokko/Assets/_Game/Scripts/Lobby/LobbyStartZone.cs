using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// 準備ルームの「スタート地点」。二人ともこの円の中に入ると試合開始のカウントダウンが始まる。
    ///
    /// 乗っている人数を色で返すので、ボタンの説明を読まなくても
    /// 「グレー＝誰もいない／赤＝あと一人／緑＝そろった」がパッと見で分かる。
    ///
    /// トリガーの Enter/Exit ではなく範囲の内外判定にしているのは、
    /// プレイヤーが当たり判定用の球と胴体のカプセルという複数のコライダーを持っており、
    /// 出入りイベントで数を管理すると片方だけ抜けたときに誤検知するため。
    /// </summary>
    public class LobbyStartZone : MonoBehaviour
    {
        [Header("Shape")]
        [SerializeField, Min(0.5f)] private float radius = 3.5f;
        [SerializeField, Min(0.5f)] private float height = 4f;

        [Tooltip("判定の下端を床より少し下げる余裕。プレイヤーの座標は足元にあるため、" +
                 "床に立っただけで範囲外になってしまうのを防ぐ")]
        [SerializeField, Min(0f)] private float groundMargin = 0.6f;

        [Header("Feedback")]
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private string colorProperty = "_BaseColor";
        [SerializeField] private Color emptyColor = new Color(0.45f, 0.47f, 0.52f);
        [SerializeField] private Color partialColor = new Color(0.95f, 0.30f, 0.30f);
        [SerializeField] private Color readyColor = new Color(0.30f, 0.95f, 0.45f);

        private MaterialPropertyBlock block;

        /// <summary>今この円に乗っているプレイヤーの数。</summary>
        public int OccupantCount { get; private set; }

        public float Radius => radius;

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
        }

        private void Update()
        {
            OccupantCount = CountOccupants();
            ApplyColor(OccupantCount);
        }

        private int CountOccupants()
        {
            GameManager manager = GameManager.Instance;
            if (manager == null) return 0;

            int count = 0;
            foreach (PlayerController player in manager.Players)
            {
                if (player == null) continue;
                if (Contains(player.transform.position)) count++;
            }

            return count;
        }

        private void ApplyColor(int occupants)
        {
            if (targetRenderer == null || block == null) return;

            GameManager manager = GameManager.Instance;
            int required = manager != null ? manager.Players.Count : 2;

            Color color;
            if (occupants <= 0) color = emptyColor;
            else if (occupants >= required) color = readyColor;
            else color = partialColor;

            targetRenderer.GetPropertyBlock(block);
            block.SetColor(colorProperty, color);
            block.SetColor("_Color", color);
            targetRenderer.SetPropertyBlock(block);
        }

        /// <summary>円柱状の判定。水平は半径、垂直は床の少し下から height ぶん。</summary>
        public bool Contains(Vector3 point)
        {
            float bottom = transform.position.y - groundMargin;
            if (point.y < bottom || point.y > bottom + height) return false;

            float dx = point.x - transform.position.x;
            float dz = point.z - transform.position.z;

            return dx * dx + dz * dz <= radius * radius;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.5f);
            Vector3 center = transform.position + Vector3.up * (height / 2f - groundMargin);
            Gizmos.DrawWireSphere(center, radius);
        }
    }
}
