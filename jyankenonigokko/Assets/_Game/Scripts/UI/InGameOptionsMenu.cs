using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MagicHand
{
    /// <summary>
    /// 試合中に開ける感度・視野角調整。操作説明もここから開ける。
    ///
    /// 試合は止めない。止めると開いていない側だけが動けて不公平だが、
    /// かといって一時停止すると対戦のテンポが切れるため、
    /// 「止めない代わりに視界を塞がない小さな表示にする」方針にしている。
    /// 画面隅に数値だけを出すので、動きながら調整でき、開くことの不利が最小になる。
    ///
    /// オプションボタンを押しっぱなしにすると（既定5秒）デバッグモードに入る。
    /// デバッグ中の専用パネルはこのパネルとは別に持つ（<see cref="debugPanel"/>）。
    /// 同じ十字キー入力を奪い合わないよう、こちらのパネルが開いている間は
    /// デバッグパネルを表示しない（閉じると入れ替わりで表示される）。
    ///
    /// PlayerInput の Send Messages を受けるので、このコンポーネントは
    /// パネルではなくプレイヤー本体に付ける。
    /// </summary>
    public class InGameOptionsMenu : MonoBehaviour
    {
        private enum Row { Sensitivity, Invert, Fov, Controls, EndGame }
        private const int RowCount = 5;

        private enum DebugRow { CreativeFlight, GrantItem0, GrantItem1, GrantItem2, GrantItem3, GrantItem4 }
        private const int DebugRowCount = 6;

        [SerializeField] private PlayerController player;
        [SerializeField] private PlayerInput playerInput;

        [Header("Options Panel")]
        [SerializeField] private GameObject panel;
        [SerializeField] private Text sensitivityText;
        [SerializeField] private Text invertText;
        [SerializeField] private Text fovText;
        [SerializeField] private Text controlsText;
        [SerializeField] private Text endGameText;

        [Tooltip("「操作方法」の行で開閉する、操作説明の表（準備ルームと同じ内容）")]
        [SerializeField] private GameObject controlsPanel;

        [Header("Debug Panel")]
        [Tooltip("デバッグモード中、オプションを閉じている間だけ表示する専用パネル")]
        [SerializeField] private GameObject debugPanel;
        [SerializeField] private Text creativeFlightText;
        [SerializeField] private Text[] grantItemTexts;
        [SerializeField] private ScrollEffectSO[] grantableScrolls;

        [Tooltip("オプションボタンをこの秒数だけ押し続けるとデバッグモードが切り替わる")]
        [SerializeField] private float debugHoldDuration = 5f;

        [Tooltip("倒しっぱなしにしたときの連続入力の間隔（秒）")]
        [SerializeField] private float repeatInterval = 0.22f;

        [Header("Colors")]
        [SerializeField] private Color selectedColor = new Color(1f, 0.92f, 0.45f);
        [SerializeField] private Color normalColor = new Color(0.85f, 0.85f, 0.9f);
        [SerializeField] private Color debugColor = new Color(1f, 0.55f, 0.3f);

        private bool isOpen;
        private bool debugMode;
        private int cursor;
        private int debugCursor;
        private Vector2 input;
        private Vector2 lastStep;
        private float repeatTimer;

        private InputActionMap cachedMap;
        private InputAction optionsAction;
        private float optionsHoldTimer;
        private bool debugToggleArmed;

        /// <summary>デバッグモード中か。HUD等が「今デバッグ中」を示したいときに参照する。</summary>
        public bool DebugMode => debugMode;

        /// <summary>
        /// 十字キーが今このパネル（オプション or デバッグ）に取られているか。
        /// 煽りエモート（十字キー下）など、別機能が同じ十字キーを使う前にこれを確認する。
        /// </summary>
        public bool IsInputCaptured => isOpen || debugMode;

        private void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (playerInput == null) playerInput = GetComponent<PlayerInput>();
            if (panel != null) panel.SetActive(false);
            if (controlsPanel != null) controlsPanel.SetActive(false);
            if (debugPanel != null) debugPanel.SetActive(false);
        }

        private void OnOptions(InputValue value)
        {
            if (!value.isPressed) return;

            GameManager manager = GameManager.Instance;
            if (manager == null || manager.CurrentState != GameState.InGame) return;

            SetOpen(!isOpen);
        }

        private void OnMenuNavigate(InputValue value)
        {
            input = value.Get<Vector2>();
        }

        private void Update()
        {
            TrackDebugHold();

            GameManager manager = GameManager.Instance;
            bool inGame = manager != null && manager.CurrentState == GameState.InGame;

            // 試合が終わった等で状態が変わったら開きっぱなしにしない
            if (isOpen && !inGame) SetOpen(false);

            if (isOpen)
            {
                HandleRepeat();
                RefreshOptionsPanel();
            }

            // デバッグ中はオプションを閉じている間だけ専用パネルを出す。
            // 同じ十字キーを両方のパネルが同時に取り合わないようにするため
            bool showDebug = debugMode && !isOpen && inGame;
            if (debugPanel != null && debugPanel.activeSelf != showDebug) debugPanel.SetActive(showDebug);

            if (showDebug)
            {
                HandleDebugRepeat();
                RefreshDebugPanel();
            }
        }

        /// <summary>
        /// オプションボタンの押しっぱなし時間を毎フレーム直接読む。
        /// Send Messages はボタン型アクションだと押した瞬間しか届かないため、
        /// 「押し続けている」の検知には使えない（PlayerController.ReadVerticalInput と同じ理由）。
        /// </summary>
        private void TrackDebugHold()
        {
            InputActionMap map = playerInput != null ? playerInput.currentActionMap : null;
            if (map != cachedMap)
            {
                cachedMap = map;
                optionsAction = map != null ? map.FindAction("Options") : null;
            }

            if (optionsAction == null || !optionsAction.IsPressed())
            {
                optionsHoldTimer = 0f;
                debugToggleArmed = false;
                return;
            }

            optionsHoldTimer += Time.deltaTime;
            if (!debugToggleArmed && optionsHoldTimer >= debugHoldDuration)
            {
                debugToggleArmed = true;
                SetDebugMode(!debugMode);
            }
        }

        private void SetDebugMode(bool enabled)
        {
            if (debugMode == enabled) return;

            debugMode = enabled;
            debugCursor = 0;

            // デバッグ中だけの効果は、抜けたら必ず後始末する
            if (!enabled && player != null && player.Flight != null) player.Flight.SetCreativeFlight(false);
        }

        private void SetOpen(bool open)
        {
            isOpen = open;
            input = Vector2.zero;
            lastStep = Vector2.zero;

            if (panel != null) panel.SetActive(open);
            if (!open && controlsPanel != null) controlsPanel.SetActive(false);
            if (open) RefreshOptionsPanel();
        }

        /// <summary>上下で項目、左右で値。試合が動いているので時間は通常どおり数える。</summary>
        private void HandleRepeat()
        {
            Vector2 step = Quantize(input);

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
                Apply(step);
                return;
            }

            repeatTimer -= Time.deltaTime;
            if (repeatTimer > 0f) return;

            repeatTimer = repeatInterval;
            Apply(step);
        }

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

        private void Apply(Vector2 step)
        {
            MatchSettings settings = MatchSettings.Instance;
            if (settings == null || player == null) return;

            if (step.y != 0f)
            {
                // 画面の上が先頭なので、上入力でインデックスを減らす
                cursor = ((cursor - (int)step.y) % RowCount + RowCount) % RowCount;
                return;
            }

            if (step.x == 0f) return;

            switch ((Row)cursor)
            {
                case Row.Sensitivity:
                    settings.AdjustSensitivity(player.PlayerIndex, (int)step.x);
                    break;

                case Row.Invert:
                    settings.ToggleInvertLook(player.PlayerIndex);
                    break;

                case Row.Fov:
                    settings.AdjustFieldOfView(player.PlayerIndex, (int)step.x);
                    break;

                case Row.Controls:
                    if (controlsPanel != null) controlsPanel.SetActive(!controlsPanel.activeSelf);
                    break;

                case Row.EndGame:
                    // 試合を強制終了する操作なので、左右どちらでも反応すると誤操作で
                    // 押し間違えてしまう。決定操作としてL（右）だけに絞る
                    if (step.x > 0f) GameManager.Instance?.ReturnToLobby();
                    break;
            }
        }

        private void RefreshOptionsPanel()
        {
            MatchSettings settings = MatchSettings.Instance;
            if (settings == null || player == null) return;

            int index = player.PlayerIndex;

            if (sensitivityText != null)
            {
                bool selected = cursor == (int)Row.Sensitivity;
                sensitivityText.text = (selected ? "▶ " : "   ") + $"感度 ◀ {settings.GetSensitivity(index):0.0} ▶";
                sensitivityText.color = selected ? selectedColor : normalColor;
            }

            if (invertText != null)
            {
                bool selected = cursor == (int)Row.Invert;
                invertText.text = (selected ? "▶ " : "   ") + $"上下反転 {(settings.IsLookInverted(index) ? "ON" : "OFF")}";
                invertText.color = selected ? selectedColor : normalColor;
            }

            if (fovText != null)
            {
                bool selected = cursor == (int)Row.Fov;
                fovText.text = (selected ? "▶ " : "   ") + $"視野角 ◀ {settings.GetFieldOfView(index):0} ▶";
                fovText.color = selected ? selectedColor : normalColor;
            }

            if (controlsText != null)
            {
                bool selected = cursor == (int)Row.Controls;
                bool open = controlsPanel != null && controlsPanel.activeSelf;
                controlsText.text = (selected ? "▶ " : "   ") + $"操作説明 {(open ? "閉じる ▶" : "開く ▶")}";
                controlsText.color = selected ? selectedColor : normalColor;
            }

            if (endGameText != null)
            {
                bool selected = cursor == (int)Row.EndGame;
                endGameText.text = (selected ? "▶ " : "   ") + "ゲーム終了（L）";
                endGameText.color = selected ? selectedColor : normalColor;
            }
        }

        // ---- デバッグパネル ---------------------------------------------------

        /// <summary>上下で項目、左右で決定（アイテム付与はどちらの方向でも実行する）。</summary>
        private void HandleDebugRepeat()
        {
            Vector2 step = Quantize(input);

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
                ApplyDebug(step);
                return;
            }

            repeatTimer -= Time.deltaTime;
            if (repeatTimer > 0f) return;

            repeatTimer = repeatInterval;
            ApplyDebug(step);
        }

        private void ApplyDebug(Vector2 step)
        {
            if (player == null) return;

            if (step.y != 0f)
            {
                debugCursor = ((debugCursor - (int)step.y) % DebugRowCount + DebugRowCount) % DebugRowCount;
                return;
            }

            if (step.x == 0f) return;

            if (debugCursor == (int)DebugRow.CreativeFlight)
            {
                if (player.Flight != null) player.Flight.SetCreativeFlight(!player.Flight.CreativeFlight);
                return;
            }

            // アイテムの付与は決定操作。左右どちらを押しても同じ付与を実行する
            int grantIndex = debugCursor - (int)DebugRow.GrantItem0;
            if (grantIndex >= 0 && grantableScrolls != null && grantIndex < grantableScrolls.Length && player.Scrolls != null)
            {
                player.Scrolls.ForceStock(grantableScrolls[grantIndex]);
            }
        }

        private void RefreshDebugPanel()
        {
            if (player == null) return;

            if (creativeFlightText != null)
            {
                bool selected = debugCursor == (int)DebugRow.CreativeFlight;
                bool on = player.Flight != null && player.Flight.CreativeFlight;
                creativeFlightText.text = (selected ? "▶ " : "   ") + $"クリエイティブ飛行 {(on ? "ON" : "OFF")}";
                creativeFlightText.color = selected ? selectedColor : debugColor;
            }

            if (grantItemTexts == null) return;

            for (int i = 0; i < grantItemTexts.Length; i++)
            {
                Text text = grantItemTexts[i];
                if (text == null) continue;

                bool selected = debugCursor == (int)DebugRow.GrantItem0 + i;
                string name = grantableScrolls != null && i < grantableScrolls.Length && grantableScrolls[i] != null
                    ? grantableScrolls[i].DisplayName : "???";

                text.text = (selected ? "▶ " : "   ") + $"付与: {name}";
                text.color = selected ? selectedColor : debugColor;
            }
        }
    }
}
