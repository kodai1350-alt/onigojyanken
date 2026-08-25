using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// 試合中の得点表示は、各プレイヤーのHUDが自分の点だけを画面の仕切り線ぎりぎりに出す
    /// （<see cref="InGameHUD"/>）。仕切り線をまたぐ「-」は片方のHUDだけでは画面中央に
    /// ぴったり置けない（分割画面はカメラのビューポートで左右に切られており、
    /// 各HUDのCanvasはその半分の中でしか描けないため）。
    /// そのため、画面全体を覆う共有Canvas側に「-」を一つだけ置き、常に真ん中に表示する。
    /// </summary>
    public class ScoreDashUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;

        private void Update()
        {
            GameManager manager = GameManager.Instance;
            if (manager == null || panel == null) return;

            bool show = manager.CurrentState == GameState.Selection || manager.CurrentState == GameState.InGame;
            if (panel.activeSelf != show) panel.SetActive(show);
        }
    }
}
