using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MagicHand
{
    /// <summary>
    /// 時間切れで同点だったときの分岐。サドンデスに進むか、そのまま結果発表へ行くかを選ぶ。
    ///
    /// 十字キーで選んで決定、という手の選択画面と同じ操作にしてある。
    /// ボタンごとに違うキーを割り当てると、押す前に対応を覚える必要があって迷うため。
    ///
    /// どちらのプレイヤーからも操作できる。同点で終わった直後に
    /// 「どちらが決める権利を持つか」を決める材料が無く、決めても不公平になるため。
    /// </summary>
    public class TieBreakUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Text scoreText;

        [Tooltip("上から順に サドンデス / 結果発表")]
        [SerializeField] private Image[] choiceBackgrounds;
        [SerializeField] private Text[] choiceLabels;

        [Header("Colors")]
        [SerializeField] private Color selectedColor = new Color(0.95f, 0.72f, 0.2f);
        [SerializeField] private Color normalColor = new Color(0.18f, 0.24f, 0.38f);

        private static readonly string[] Labels = { "サドンデス", "結果発表" };

        private int cursor;

        /// <summary>十字キーを倒しっぱなしにしたときに滑らないよう、離すまで次を受け付けない。</summary>
        private bool navigateHeld;

        private void Update()
        {
            GameManager manager = GameManager.Instance;
            if (manager == null || panel == null) return;

            bool visible = manager.CurrentState == GameState.TieBreak;
            if (panel.activeSelf != visible)
            {
                panel.SetActive(visible);
                if (visible) cursor = 0;
            }

            if (!visible) return;

            if (scoreText != null)
            {
                scoreText.text = $"{manager.GetScore(0)}  -  {manager.GetScore(1)}   同点！";
            }

            HandleNavigate();
            Refresh();

            if (WasConfirmPressed()) Decide(manager);
        }

        private void HandleNavigate()
        {
            int step = ReadVertical();

            if (step == 0)
            {
                navigateHeld = false;
                return;
            }

            if (navigateHeld) return;
            navigateHeld = true;

            // 画面の上が先頭なので、上入力でインデックスを減らす
            cursor = Mathf.Clamp(cursor - step, 0, Labels.Length - 1);
        }

        /// <summary>上で +1、下で -1。繋がっている全パッドとキーボードを見る。</summary>
        private static int ReadVertical()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.upArrowKey.isPressed || keyboard.wKey.isPressed) return 1;
                if (keyboard.downArrowKey.isPressed || keyboard.sKey.isPressed) return -1;
            }

            foreach (Gamepad pad in Gamepad.all)
            {
                if (pad == null) continue;

                if (pad.dpad.up.isPressed || pad.leftStick.up.isPressed) return 1;
                if (pad.dpad.down.isPressed || pad.leftStick.down.isPressed) return -1;
            }

            return 0;
        }

        private static bool WasConfirmPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null
                && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame))
            {
                return true;
            }

            foreach (Gamepad pad in Gamepad.all)
            {
                if (pad != null && pad.buttonSouth.wasPressedThisFrame) return true;
            }

            return false;
        }

        private void Refresh()
        {
            for (int i = 0; i < Labels.Length; i++)
            {
                bool selected = i == cursor;

                if (choiceBackgrounds != null && i < choiceBackgrounds.Length && choiceBackgrounds[i] != null)
                {
                    choiceBackgrounds[i].color = selected ? selectedColor : normalColor;
                }

                if (choiceLabels != null && i < choiceLabels.Length && choiceLabels[i] != null)
                {
                    choiceLabels[i].text = (selected ? "▶ " : "　 ") + Labels[i];
                }
            }
        }

        private void Decide(GameManager manager)
        {
            if (cursor == 0) manager.BeginSuddenDeath();
            else manager.FinishWithResult();
        }
    }
}
