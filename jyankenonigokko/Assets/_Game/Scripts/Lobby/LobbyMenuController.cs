using UnityEngine;
using UnityEngine.InputSystem;

namespace MagicHand
{
    /// <summary>
    /// 準備ルームでのメニュー入力を受け取り、自分のパネルへ渡す。
    ///
    /// PlayerInput の Send Messages は同じ GameObject のコンポーネントにしか届かないので、
    /// このコンポーネントはプレイヤー本体に付け、参照でパネルを操作する形にしている。
    /// これにより移動（左スティック）とメニュー（十字キー）を同時に扱える。
    /// </summary>
    public class LobbyMenuController : MonoBehaviour
    {
        [SerializeField] private LobbySettingsPanel panel;
        [Tooltip("倒しっぱなしにしたときの連続入力の間隔（秒）")]
        [SerializeField] private float repeatInterval = 0.25f;

        private Vector2 input;
        private Vector2 lastStep;
        private float repeatTimer;

        private void OnMenuNavigate(InputValue value)
        {
            input = value.Get<Vector2>();
        }

        /// <summary>十字キー左右の決定に加えて、Xbox の B／PS の○でも同じ決定を行えるようにする。</summary>
        private void OnConfirm(InputValue value)
        {
            if (!value.isPressed || panel == null) return;

            bool active = GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Lobby;
            if (!active) return;

            panel.Confirm();
        }

        private void Update()
        {
            if (panel == null) return;

            bool active = GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Lobby;
            if (!active)
            {
                input = Vector2.zero;
                lastStep = Vector2.zero;
                return;
            }

            Vector2 step = Quantize(input);

            // ニュートラルへ戻ったら即座に次の入力を受け付ける
            if (step == Vector2.zero)
            {
                lastStep = Vector2.zero;
                repeatTimer = 0f;
                return;
            }

            if (step != lastStep)
            {
                lastStep = step;
                repeatTimer = repeatInterval;
                Send(step);
                return;
            }

            repeatTimer -= Time.deltaTime;
            if (repeatTimer > 0f) return;

            repeatTimer = repeatInterval;
            Send(step);
        }

        /// <summary>上下と左右が同時に入らないよう、傾きの大きい方だけを採用する。</summary>
        private static Vector2 Quantize(Vector2 raw)
        {
            const float threshold = 0.5f;

            if (Mathf.Abs(raw.y) >= threshold && Mathf.Abs(raw.y) >= Mathf.Abs(raw.x))
            {
                return new Vector2(0f, Mathf.Sign(raw.y));
            }

            if (Mathf.Abs(raw.x) >= threshold)
            {
                return new Vector2(Mathf.Sign(raw.x), 0f);
            }

            return Vector2.zero;
        }

        private void Send(Vector2 step)
        {
            panel.Navigate(Mathf.RoundToInt(step.y), Mathf.RoundToInt(step.x));
        }
    }
}
