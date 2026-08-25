using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// 3-2-1のあと、試合開始の合図として画面中央に一つだけ「START」を出す。
    /// 分割画面それぞれに出すと同じ文字が二つ並んでしまうので、FinishUIと同じく
    /// 全体を覆う共有Canvas側に一つだけ置く（GameManager.ShowStartText の間だけ表示）。
    /// </summary>
    public class StartUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;

        private void Update()
        {
            GameManager manager = GameManager.Instance;
            if (manager == null || panel == null) return;

            bool show = manager.ShowStartText;
            if (panel.activeSelf != show) panel.SetActive(show);
        }
    }
}
