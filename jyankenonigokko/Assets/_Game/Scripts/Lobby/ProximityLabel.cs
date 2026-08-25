using UnityEngine;
using UnityEngine.UI;

namespace MagicHand
{
    /// <summary>
    /// プレイヤーが近づいたときだけ出るワールド空間の名札。
    ///
    /// 見本を8個並べると名札も8枚が常時出て画面が文字だらけになるため、
    /// 「今そばにあるもの」だけを見せて情報量を絞る。
    /// </summary>
    public class ProximityLabel : MonoBehaviour
    {
        [SerializeField] private Text label;
        [Tooltip("この距離まで近づいたら表示する（水平距離で判定）")]
        [SerializeField, Min(1f)] private float showDistance = 5f;
        [SerializeField] private GameState visibleState = GameState.Lobby;

        private void Awake()
        {
            if (label == null) label = GetComponent<Text>();
        }

        private void LateUpdate()
        {
            if (label == null) return;

            bool visible = ShouldShow();

            // GameObject ごと消すとこのスクリプトも止まるので、表示は Text 側で切り替える
            if (label.enabled != visible) label.enabled = visible;
        }

        private bool ShouldShow()
        {
            GameManager manager = GameManager.Instance;
            if (manager == null || manager.CurrentState != visibleState) return false;

            float sqrRange = showDistance * showDistance;

            foreach (PlayerController player in manager.Players)
            {
                if (player == null) continue;

                Vector3 delta = player.transform.position - transform.position;
                delta.y = 0f;

                if (delta.sqrMagnitude <= sqrRange) return true;
            }

            return false;
        }
    }
}
