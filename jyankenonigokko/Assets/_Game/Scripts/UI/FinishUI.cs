using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// 試合が終わった瞬間からResultへ移るまでの一瞬だけ「FINISH」を出す画面。
    /// GameManager.Finish の間だけ表示する（表示はTieBreakUIなどと同じくポーリング）。
    /// </summary>
    public class FinishUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;

        private void Update()
        {
            GameManager manager = GameManager.Instance;
            if (manager == null || panel == null) return;

            bool show = manager.CurrentState == GameState.Finish;
            if (panel.activeSelf != show) panel.SetActive(show);
        }
    }
}
