using UnityEngine;
using UnityEngine.UI;

namespace MagicHand
{
    /// <summary>
    /// プレイヤーごとの手選択UI。開始時とリスポーン時に表示される。
    /// 表示中そのプレイヤーは無敵（PlayerController.IsSelecting）。
    ///
    /// 十字キーで動かすカーソルの現在位置は PlayerController が持っているので、
    /// ここは毎フレームそれを読んで該当カードを強調するだけにしてある。
    /// </summary>
    public class SelectionUI : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private GameObject panel;

        [Header("Cards (グー・チョキ・パーの順)")]
        [SerializeField] private Image[] cardBackgrounds;
        [SerializeField] private Outline[] cardOutlines;

        [Tooltip("選べる残り時間。切れるとランダムで決まるので、必ず見せる")]
        [SerializeField] private Text remainingText;

        [Header("Highlight")]
        [SerializeField] private float selectedScale = 1.08f;
        [SerializeField] private float unselectedAlpha = 0.45f;

        [Tooltip("残りがこの秒数を切ったら赤くして急かす")]
        [SerializeField] private float hurryThreshold = 2f;

        private static readonly HandType[] Order = { HandType.Gu, HandType.Choki, HandType.Pa };

        private void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
        }

        private void Update()
        {
            if (panel == null || player == null) return;

            bool visible = player.IsSelecting;
            if (panel.activeSelf != visible) panel.SetActive(visible);
            if (!visible) return;

            HighlightCandidate(player.SelectionCandidate);
            UpdateRemaining();
        }

        /// <summary>
        /// 選べる残り時間。何も出さずに勝手にランダムで決まると理不尽に感じるので、
        /// 秒数を見せて「急がないと勝手に決まる」ことを分からせる。
        /// </summary>
        private void UpdateRemaining()
        {
            if (remainingText == null) return;

            float remaining = player.SelectionRemaining;
            remainingText.text = $"残り {remaining:0.0}";
            remainingText.color = remaining <= hurryThreshold
                ? new Color(1f, 0.35f, 0.35f)
                : Color.white;
        }

        /// <summary>選択中のカードだけを不透明・少し大きく・縁取り付きにする。</summary>
        private void HighlightCandidate(HandType candidate)
        {
            if (cardBackgrounds == null) return;

            for (int i = 0; i < cardBackgrounds.Length && i < Order.Length; i++)
            {
                Image background = cardBackgrounds[i];
                if (background == null) continue;

                bool selected = Order[i] == candidate;

                Color color = Order[i].ToColor();
                color.a = selected ? 1f : unselectedAlpha;
                background.color = color;

                background.rectTransform.localScale = Vector3.one * (selected ? selectedScale : 1f);

                if (cardOutlines != null && i < cardOutlines.Length && cardOutlines[i] != null)
                {
                    cardOutlines[i].enabled = selected;
                }
            }
        }
    }
}
