using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MagicHand
{
    /// <summary>
    /// タイトル画面。対戦開始ボタン（またはSpace / ゲームパッドのStart）で試合を始める。
    /// GameManager の初期化順に依存しないよう、表示状態は毎フレーム参照して決める。
    /// </summary>
    public class TitleUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Button startButton;

        private void Awake()
        {
            if (startButton != null) startButton.onClick.AddListener(OnStartClicked);
        }

        private void Update()
        {
            GameManager manager = GameManager.Instance;
            if (manager == null || panel == null) return;

            bool visible = manager.CurrentState == GameState.Title;
            if (panel.activeSelf != visible) panel.SetActive(visible);
            if (!visible) return;

            if (WasSubmitPressed()) OnStartClicked();
        }

        /// <summary>
        /// 開始の合図。パッドは Start と ✕(A) の両方で通す。
        ///
        /// 絵に描かれているのは「START」の一語だけで、どのボタンを押すかは書いていない。
        /// 決定に使うボタンは人によって Start だったり ✕ だったりするので、両方受ける。
        ///
        /// Gamepad.current ではなく繋がっている全部を見る。current は最後に触られた1台を指すので、
        /// 2台目のパッドから押したときに反応しないことがある。
        /// </summary>
        private static bool WasSubmitPressed()
        {
            if (Keyboard.current != null
                && (Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame))
            {
                return true;
            }

            foreach (Gamepad pad in Gamepad.all)
            {
                if (pad == null) continue;
                if (pad.startButton.wasPressedThisFrame || pad.buttonSouth.wasPressedThisFrame) return true;
            }

            return false;
        }

        private void OnStartClicked()
        {
            GameManager manager = GameManager.Instance;
            if (manager == null || manager.CurrentState != GameState.Title) return;

            SEPlayer.PlayStartButton();
            manager.StartMatch();
        }
    }
}
