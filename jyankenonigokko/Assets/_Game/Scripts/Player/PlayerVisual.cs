using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// 現在の手に応じてキャラクターの見た目を切り替える。
    ///
    /// 体全体が1メッシュ1マテリアルなので、色を乗算すると肌まで染まってしまう。
    /// そのため「肌はそのまま・衣装だけを手の色にした」テクスチャを手ごとに用意し、
    /// マテリアルごと差し替えている。
    ///
    /// 参照するのは CurrentHand ではなく VisibleHand。
    /// 偽装スクロールの最中は本当の手と見た目が食い違うため、
    /// 相手に見える情報はすべてこちらを通す必要がある。
    /// </summary>
    public class PlayerVisual : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private Renderer[] targetRenderers;

        [Tooltip("未選択・グー・チョキ・パー の順に4つ")]
        [SerializeField] private Material[] handMaterials;

        private HandType lastApplied = (HandType)(-1);

        private static readonly HandType[] Order = { HandType.None, HandType.Gu, HandType.Choki, HandType.Pa };

        private void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (targetRenderers == null || targetRenderers.Length == 0)
            {
                targetRenderers = GetComponentsInChildren<Renderer>();
            }
        }

        private void Update()
        {
            if (player == null) return;

            // 偽装の開始・終了はイベントを伴わないので、毎フレーム見た目の手を確認する
            HandType visible = player.CurrentHand;
            if (visible == lastApplied) return;

            lastApplied = visible;
            Apply(visible);
        }

        private void Apply(HandType hand)
        {
            Material material = ResolveMaterial(hand);
            if (material == null || targetRenderers == null) return;

            foreach (Renderer renderer in targetRenderers)
            {
                if (renderer == null) continue;
                renderer.sharedMaterial = material;
            }
        }

        private Material ResolveMaterial(HandType hand)
        {
            if (handMaterials == null) return null;

            for (int i = 0; i < Order.Length && i < handMaterials.Length; i++)
            {
                if (Order[i] == hand) return handMaterials[i];
            }

            return null;
        }
    }
}
