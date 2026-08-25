using UnityEngine;
using UnityEngine.UI;

namespace MagicHand
{
    /// <summary>
    /// 準備ルームで、プレイヤーの頭上に「今持っている手とスクロール」を出す。
    ///
    /// 準備ルームは俯瞰の共有画面なので、画面端のHUDに出すと
    /// どちらのキャラの情報なのか分かりにくい。本人の頭の上に出すのが一番読みやすい。
    ///
    /// 試合中は表示しない。スクロールのストックは本来相手に見えない情報であり、
    /// 共有されるワールド空間の表示に出すと手の内が漏れてしまうため。
    /// </summary>
    public class PlayerCarryLabel : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private Text label;
        [SerializeField] private Transform followTarget;
        [SerializeField] private Vector3 offset = new Vector3(0f, 2.9f, 0f);

        private void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (followTarget == null && player != null) followTarget = player.transform;
        }

        private void LateUpdate()
        {
            GameManager manager = GameManager.Instance;
            bool visible = manager != null && manager.CurrentState == GameState.Lobby && player != null;

            // GameObject ごと消すとこのスクリプトも止まって二度と復帰できなくなるため、
            // 表示の ON/OFF は Text コンポーネント側で行う
            if (label != null && label.enabled != visible) label.enabled = visible;

            if (!visible) return;

            if (followTarget != null) transform.position = followTarget.position + offset;

            // 俯瞰の共有画面ではどちらのカプセルが自分か分からないので、まず名前を出す。
            // 手はキャラの体の色で分かるためここには出さず、色だけ持っているスクロールに使う。
            ScrollEffectSO stock = player.Scrolls != null ? player.Scrolls.Current : null;

            label.text = stock != null ? $"{player.DisplayName}\n{stock.DisplayName}" : player.DisplayName;
            label.color = stock != null ? stock.DisplayColor : Color.white;
        }
    }
}
