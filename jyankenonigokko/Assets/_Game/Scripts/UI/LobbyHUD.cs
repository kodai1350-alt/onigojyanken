using UnityEngine;
using UnityEngine.UI;

namespace MagicHand
{
    /// <summary>
    /// 準備ルームの中央表示。開始カウントダウンと、スタート地点の状況を出す。
    ///
    /// カウントダウンの表示は InGameHUD が持っているが、
    /// あちらはプレイヤーのカメラに紐づく Canvas 上にあり、
    /// 俯瞰カメラに切り替わる準備ルームでは映らない。そのため専用に用意している。
    /// </summary>
    public class LobbyHUD : MonoBehaviour
    {
        [SerializeField] private LobbyStartZone startZone;
        [SerializeField] private Text countdownText;
        [SerializeField] private Text statusText;

        [Tooltip("今どちらのモードで遊ぶ設定になっているかを、常に画面中央に出す")]
        [SerializeField] private Text modeText;

        [Header("Colors")]
        [SerializeField] private Color emptyColor = new Color(0.75f, 0.77f, 0.82f);
        [SerializeField] private Color partialColor = new Color(1f, 0.45f, 0.45f);
        [SerializeField] private Color readyColor = new Color(0.45f, 1f, 0.6f);

        [SerializeField] private Color normalModeColor = new Color(0.6f, 0.8f, 1f);
        [SerializeField] private Color easyModeColor = new Color(0.55f, 1f, 0.6f);

        private void Update()
        {
            GameManager manager = GameManager.Instance;
            bool inLobby = manager != null && manager.CurrentState == GameState.Lobby;

            if (!inLobby)
            {
                SetActive(countdownText, false);
                SetActive(statusText, false);
                SetActive(modeText, false);
                return;
            }

            UpdateCountdown(manager);
            UpdateStatus(manager);
            UpdateMode();
        }

        /// <summary>
        /// イージー／ノーマルの切り替えは分かりにくいと事故のもとなので、
        /// 準備ルームにいる間は常に画面中央に今の設定を出しておく。
        /// </summary>
        private void UpdateMode()
        {
            if (modeText == null) return;

            SetActive(modeText, true);

            bool easy = MatchSettings.Instance != null && MatchSettings.Instance.EasyMode;
            modeText.text = easy ? "イージーモード" : "ノーマルモード";
            modeText.color = easy ? easyModeColor : normalModeColor;
        }

        private void UpdateCountdown(GameManager manager)
        {
            if (countdownText == null) return;

            float remaining = manager.CountdownRemaining;
            bool counting = remaining > 0f;

            SetActive(countdownText, counting);
            if (counting) countdownText.text = Mathf.CeilToInt(remaining).ToString();
        }

        private void UpdateStatus(GameManager manager)
        {
            if (statusText == null) return;

            SetActive(statusText, true);

            int occupants = startZone != null ? startZone.OccupantCount : 0;
            int required = manager.Players.Count;

            if (occupants <= 0)
            {
                // 色には言及しない。誰も乗っていないときの円はグレーなので、
                // 「みどりの円」と書くと実際の見た目と食い違うため
                statusText.text = "乗ると開始";
                statusText.color = emptyColor;
            }
            else if (occupants >= required)
            {
                statusText.text = "揃った！";
                statusText.color = readyColor;
            }
            else
            {
                statusText.text = $"あと {required - occupants} 人";
                statusText.color = partialColor;
            }
        }

        private static void SetActive(Graphic target, bool active)
        {
            if (target == null) return;
            if (target.gameObject.activeSelf != active) target.gameObject.SetActive(active);
        }
    }
}
