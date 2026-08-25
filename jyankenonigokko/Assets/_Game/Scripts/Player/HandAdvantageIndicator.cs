using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// 相手の頭上表示（<see cref="PlayerHandIndicator"/>）と本体の間に出す、
    /// 「自分が相手の手に勝っているか」を表す文字（優位／劣位／互角）。
    ///
    /// owner（マークが付いている本人）ではなく、その相手（viewer）にだけ見える
    /// レイヤーに乗せる（ビルダー側で rivalLayer を割り当てる）。頭上の手表示と同じ
    /// 「自分の頭上には要らない、相手にだけ見えれば良い」情報のため。
    ///
    /// 最初は図形（三角形/ひし形）のメッシュで作っていたが、「優位、劣位、互角のまま文字で表示」
    /// の依頼で TextMesh に差し替えた。どの角度から見ても正面を向かせる必要があるのは変わらないため、
    /// TextMesh自体のalignment機能には頼らず、LateUpdateで毎フレーム transform 全体を
    /// Quaternion.LookRotationで向ける（図形版と同じ自前ビルボード方式）。
    ///
    /// 注意: TextMeshの文字は -Z 側から見て正しく読める向きで生成される（+Zが裏側）。
    /// そのため forward には「カメラの方向」ではなく「カメラの逆方向（自分からカメラを見た
    /// 反対側）」を渡す必要がある。図形（三角形）のときは両面描画だったので向きを気にしていなかったが、
    /// 文字は裏表があるため向きを間違えると鏡文字になる
    /// </summary>
    public class HandAdvantageIndicator : MonoBehaviour
    {
        [SerializeField] private PlayerController owner;
        [SerializeField] private PlayerController viewer;
        [SerializeField] private TextMesh textMesh;
        [SerializeField] private MeshRenderer meshRenderer;

        [Tooltip("縁取り用に少し大きく重ねる黒いコピー（TextMeshにはUIのOutlineが使えないため自前で作る）")]
        [SerializeField] private TextMesh outlineTextMesh;
        [SerializeField] private MeshRenderer outlineMeshRenderer;

        [Tooltip("頭上の手表示（PlayerHandIndicator）の縁取り。この表示と同じ色で染めて、" +
                 "「今の優劣」が頭上の手にも伝わるようにする")]
        [SerializeField] private Renderer[] handOutlineRenderers;

        private static readonly Color AdvantageColor = new Color(0.35f, 1f, 0.4f);
        private static readonly Color DisadvantageColor = new Color(1f, 0.35f, 0.35f);
        private static readonly Color EvenColor = new Color(0.6f, 0.6f, 0.6f);
        private static readonly Color OutlineColor = Color.black;

        /// <summary>
        /// owner本体からどれだけ持ち上げるか。
        ///
        /// 「手のアイテムと人の間に表示してほしい」という依頼で、いったんy=2.0（頭上の手表示
        /// HandIndicator、y=2.3のすぐ下）まで上げたが、実際に描き出して確認したところ
        /// 完全に重なって見えた。原因はHandIndicatorの見た目が実測で非常に大きいこと
        /// （頭上の手表示のRendererバウンズが中心y=2.15・縦幅約0.96で、下端がy=1.67まで
        /// 垂れ下がっている＝頭上の手表示自体が既にキャラの頭部（頭頂 約1.95）と重なる大きさ）。
        /// つまり「手のアイテムの下端」と「人の頭」の間には実質すき間が無く、文字通り
        /// 両者に挟んで重ならせない配置は成立しない。1.4〜1.45も試したが、角度によっては
        /// 帽子のつば（§22-1で1.5〜1.6付近と実測済み、つばは横に大きく張り出す）の後ろに
        /// 隠れて逆に読めなくなる場合があった。結局、頭上の手表示にも帽子のつばにも
        /// 干渉しないことが実写で確認済みの元の値（1.3、胸の高さ付近）に戻した
        /// </summary>
        private const float IndicatorHeight = 1.3f;

        /// <summary>owner本体からどれだけ視聴者側へ浮かせるか（体のメッシュに埋もれないようにする）。</summary>
        private const float IndicatorForwardOffset = 0.4f;

        private int shownState = -1; // -1:未描画 0:優位 1:劣位 2:互角
        private MaterialPropertyBlock handOutlineBlock;

        private void Awake()
        {
            if (owner == null) owner = GetComponentInParent<PlayerController>();
        }

        private void LateUpdate()
        {
            // 描画のビルボード回転は、キャラの向き（Update）が確定した後に行いたいのでLateUpdateにしてある
            if (owner == null || viewer == null || viewer.CameraRig == null) return;

            // ownerの「ローカル前方」ではなく「viewerへ向かう水平方向」へ浮かせる。
            // ownerの向き基準だと、ownerが視聴者に背を向けたときにマークが体の裏側へ回り込んで
            // 隠れてしまう（＝「相手キャラの中心」から外れて見える不具合の原因だった）。
            // 常に視聴者側へ出すことで、ownerがどちらを向いていても必ず体の手前・中心に見える
            Vector3 basePos = owner.transform.position + Vector3.up * IndicatorHeight;
            Vector3 toViewer = viewer.transform.position - owner.transform.position;
            toViewer.y = 0f;
            Vector3 direction = toViewer.sqrMagnitude > 0.0001f ? toViewer.normalized : owner.transform.forward;
            transform.position = basePos + direction * IndicatorForwardOffset;

            Vector3 away = transform.position - viewer.CameraRig.transform.position;
            if (away.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(away);
            }

            if (textMesh == null || meshRenderer == null) return;

            HandType ownerHand = owner.CurrentHand;
            HandType viewerHand = viewer.CurrentHand;

            // どちらかが選択中（None）のときは、比べようがないので出さない
            bool bothSelected = ownerHand != HandType.None && viewerHand != HandType.None;
            if (meshRenderer.enabled != bothSelected) meshRenderer.enabled = bothSelected;
            if (outlineMeshRenderer != null && outlineMeshRenderer.enabled != bothSelected) outlineMeshRenderer.enabled = bothSelected;
            SetHandOutlineActive(bothSelected);
            if (!bothSelected) return;

            bool advantage = viewerHand.Beats(ownerHand);
            bool disadvantage = ownerHand.Beats(viewerHand);
            int state = advantage ? 0 : disadvantage ? 1 : 2;

            if (state != shownState)
            {
                shownState = state;
                string label = state == 0 ? "優位" : state == 1 ? "劣位" : "互角";
                Color color = state == 0 ? AdvantageColor : state == 1 ? DisadvantageColor : EvenColor;

                textMesh.text = label;
                textMesh.color = color;

                if (outlineTextMesh != null)
                {
                    outlineTextMesh.text = label;
                    outlineTextMesh.color = OutlineColor;
                }

                ApplyHandOutlineColor(color);
            }
        }

        /// <summary>頭上の手表示の縁取りを、このマークと同じ色に染める。地面の手アイテムの縁取り（白）とは別のマテリアルなので影響しない。</summary>
        private void ApplyHandOutlineColor(Color color)
        {
            if (handOutlineRenderers == null || handOutlineRenderers.Length == 0) return;
            if (handOutlineBlock == null) handOutlineBlock = new MaterialPropertyBlock();

            handOutlineBlock.SetColor("_BaseColor", color);
            handOutlineBlock.SetColor("_Color", color);

            foreach (Renderer renderer in handOutlineRenderers)
            {
                if (renderer == null) continue;
                renderer.SetPropertyBlock(handOutlineBlock);
            }
        }

        private void SetHandOutlineActive(bool active)
        {
            if (handOutlineRenderers == null) return;

            foreach (Renderer renderer in handOutlineRenderers)
            {
                if (renderer == null) continue;
                if (renderer.enabled != active) renderer.enabled = active;
            }
        }
    }
}
