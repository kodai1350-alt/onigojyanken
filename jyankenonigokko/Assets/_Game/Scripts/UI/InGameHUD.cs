using UnityEngine;
using UnityEngine.UI;

namespace MagicHand
{
    /// <summary>
    /// プレイヤーごとのHUD。残り時間・スコア・今の手・持ちアイテム・かかっている効果を表示する。
    /// 分割画面のため各プレイヤーのカメラに紐づく Canvas 配下へ置く。
    ///
    /// 分割画面は横幅が実質半分しかない。文字が小さいと試合中に読めないので、
    /// 一番大事な「自分の手」と「持ちアイテム」は大きく、色を伴わせて出している。
    /// </summary>
    public class InGameHUD : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private GameObject panel;

        [Header("Texts")]
        [SerializeField] private Text timerText;
        [SerializeField] private Text scoreText;
        [SerializeField] private Text countdownText;

        [Tooltip("残り1分／残り10秒になった瞬間だけ画面中心に出す通知。数秒で自動的に消える")]
        [SerializeField] private Text timeAnnounceText;

        [Header("Hand")]
        [Tooltip("今の手。アイテム枠と同じ「枠＋アイコン＋名前」の見た目にしてある")]
        [SerializeField] private Image handFrame;
        [SerializeField] private Image handIcon;
        [SerializeField] private Text handText;
        [SerializeField] private Sprite guIcon;
        [SerializeField] private Sprite chokiIcon;
        [SerializeField] private Sprite paIcon;

        [Header("Item Box")]
        [Tooltip("マリオカート風の枠。何も持っていなくても枠は残す")]
        [SerializeField] private Image itemFrame;
        [SerializeField] private Image itemIcon;
        [SerializeField] private Text itemName;

        [Tooltip("持っているのに今は使えないときに重ねるバツ印（スタン中でワープ以外を持っている等）")]
        [SerializeField] private Text itemUnusableMark;

        [Header("Status Effects")]
        [Tooltip("かかっている効果の行。上から残り時間の長い順に埋める")]
        [SerializeField] private Text[] statusRows;

        [Tooltip("飛行と滑空はタイマーではなく状態なので、効果一覧とは別に出す")]
        [SerializeField] private Text flightText;

        [Tooltip("負けて吹っ飛んでいる間の表示。何が起きたのか分からないまま飛ばされるのを防ぐ")]
        [SerializeField] private Text defeatText;

        private static readonly Color EmptyFrameColor = new Color(1f, 1f, 1f, 0.25f);
        private static readonly Color OneMinuteColor = new Color(1f, 0.9f, 0.2f);
        private static readonly Color TenSecondsColor = new Color(1f, 0.3f, 0.25f);

        [Tooltip("残り1分／10秒の通知を出しっぱなしにする秒数")]
        [SerializeField, Min(0.1f)] private float announceDuration = 2.5f;

        private bool wasInMatch;
        private bool announcedOneMinute;
        private bool announcedTenSeconds;
        private float announceRemaining;

        private void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
        }

        private void Update()
        {
            GameManager manager = GameManager.Instance;
            if (manager == null || player == null) return;

            bool inMatch = manager.CurrentState == GameState.Selection || manager.CurrentState == GameState.InGame;
            if (panel != null && panel.activeSelf != inMatch) panel.SetActive(inMatch);

            // 新しい試合が始まった瞬間に、前の試合ぶんの通知フラグを引きずらないよう戻す
            if (inMatch && !wasInMatch)
            {
                announcedOneMinute = false;
                announcedTenSeconds = false;
                announceRemaining = 0f;
            }
            wasInMatch = inMatch;

            if (!inMatch) return;

            float remaining = manager.Timer.Remaining;
            bool finalMinute = !manager.IsSuddenDeath && remaining <= 60f && remaining > 10f;
            bool finalTenSeconds = !manager.IsSuddenDeath && remaining <= 10f && remaining > 0f;

            if (timerText != null)
            {
                // サドンデス中は時計が止まっているので、0:00 ではなく状況を出す
                timerText.text = manager.IsSuddenDeath ? "サドンデス" : manager.Timer.ToDisplayString();

                // 中心の通知（残り1分/10秒）は数秒で消えるが、タイマー本体の色は
                // その時間帯ずっと変えたままにする。通知が消えた後も「まだ危ない」と分かるように
                timerText.color = manager.IsSuddenDeath ? new Color(1f, 0.5f, 0.4f)
                                  : finalTenSeconds ? TenSecondsColor
                                  : finalMinute ? OneMinuteColor
                                  : Color.white;

                // 最後の10秒は文字を脈打たせて、時間切れが迫っていることを強調する
                float pulse = finalTenSeconds ? 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 9f) : 1f;
                timerText.transform.localScale = Vector3.one * pulse;
            }

            UpdateTimeAnnounce(manager, remaining);

            if (scoreText != null)
            {
                // 相手の点は画面中央の共有UI（ScoreDashUI、仕切り線の"-"）の反対側に
                // 相手自身のHUDが出すので、こちらは自分の点だけを仕切り線ぎりぎりに出す
                scoreText.text = manager.GetScore(player.PlayerIndex).ToString();
            }

            UpdateHand();
            UpdateItemBox();
            UpdateStatusRows();

            if (flightText != null) UpdateFlightText();
            if (defeatText != null) UpdateDefeatText();

            if (countdownText != null)
            {
                float countdownRemaining = manager.CountdownRemaining;
                bool showCountdown = countdownRemaining > 0f;

                if (countdownText.gameObject.activeSelf != showCountdown)
                {
                    countdownText.gameObject.SetActive(showCountdown);
                }

                if (showCountdown) countdownText.text = Mathf.CeilToInt(countdownRemaining).ToString();
            }
        }

        /// <summary>
        /// 今の手。持ちアイテムの枠と同じ「枠＋アイコン＋名前」の見た目で出す。
        /// 頭上の相手向け表示（<see cref="PlayerHandIndicator"/>）が形で見分けさせる仕組みなので、
        /// 自分向けのこちらも色だけに頼らずアイコンと文字の両方で分かるようにしてある。
        /// </summary>
        private void UpdateHand()
        {
            HandType hand = player.CurrentHand;

            if (handFrame != null) handFrame.color = hand.ToColor();

            if (handIcon != null)
            {
                bool hasHand = hand != HandType.None;
                if (handIcon.enabled != hasHand) handIcon.enabled = hasHand;
                handIcon.sprite = ResolveHandIcon(hand);
                handIcon.color = Color.white;
            }

            if (handText != null)
            {
                // 自分のHUDには常に本当の手を出す。偽装は相手を騙すためのもので、
                // 使った本人が自分の手を見失っては困るため
                handText.text = hand == HandType.None ? "？" : hand.ToLabel();
                handText.color = Color.white;
            }
        }

        /// <summary>
        /// 持ちアイテムの枠。空でも枠は消さない。
        /// 出たり消えたりすると視線がそのたびに引っぱられるため。
        /// </summary>
        private void UpdateItemBox()
        {
            ScrollEffectSO stock = player.Scrolls != null ? player.Scrolls.Current : null;

            if (itemFrame != null)
            {
                itemFrame.color = stock != null ? stock.DisplayColor : EmptyFrameColor;
            }

            if (itemIcon != null)
            {
                bool hasItem = stock != null;
                if (itemIcon.enabled != hasItem) itemIcon.enabled = hasItem;

                if (hasItem)
                {
                    // アイコンが無いアイテムでも壊れないよう、色だけの四角で代替する
                    itemIcon.sprite = stock.Icon;
                    itemIcon.color = stock.Icon != null ? Color.white : stock.DisplayColor;
                }
            }

            if (itemName != null)
            {
                itemName.text = stock != null ? stock.DisplayName : "なし";
                itemName.color = stock != null ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            }

            if (itemUnusableMark != null)
            {
                bool unusable = stock != null && player != null && !player.CanUseScroll;
                if (itemUnusableMark.gameObject.activeSelf != unusable) itemUnusableMark.gameObject.SetActive(unusable);
            }
        }

        private Sprite ResolveHandIcon(HandType hand)
        {
            switch (hand)
            {
                case HandType.Gu: return guIcon;
                case HandType.Choki: return chokiIcon;
                case HandType.Pa: return paIcon;
                default: return null;
            }
        }

        /// <summary>かかっている効果を、残り時間の長い順に上から埋める。</summary>
        private void UpdateStatusRows()
        {
            if (statusRows == null || statusRows.Length == 0) return;

            PlayerStatusEffects status = player.Status;
            int count = status != null ? status.Count : 0;

            for (int i = 0; i < statusRows.Length; i++)
            {
                Text row = statusRows[i];
                if (row == null) continue;

                bool show = i < count;
                if (row.enabled != show) row.enabled = show;
                if (show) row.text = $"{status.GetLabel(i)} {status.GetRemaining(i):0.0}";
            }
        }

        /// <summary>
        /// 残り1分・残り10秒になった瞬間だけ画面中心に大きく出す通知。
        /// 一度出したら数秒で自動的に消え、以後その試合中は再び出さない
        /// （<see cref="Update"/> で試合開始のたびにフラグを戻す）。
        /// サドンデス中は時計そのものが止まっているので対象外。
        /// </summary>
        private void UpdateTimeAnnounce(GameManager manager, float remaining)
        {
            if (timeAnnounceText == null) return;

            if (!manager.IsSuddenDeath)
            {
                if (!announcedOneMinute && remaining <= 60f && remaining > 0f)
                {
                    announcedOneMinute = true;
                    announceRemaining = announceDuration;
                    timeAnnounceText.text = "残り1分";
                    timeAnnounceText.color = OneMinuteColor;
                }

                if (!announcedTenSeconds && remaining <= 10f && remaining > 0f)
                {
                    announcedTenSeconds = true;
                    announceRemaining = announceDuration;
                    timeAnnounceText.text = "残り10秒";
                    timeAnnounceText.color = TenSecondsColor;
                }
            }

            bool show = announceRemaining > 0f;
            if (timeAnnounceText.enabled != show) timeAnnounceText.enabled = show;

            if (show)
            {
                announceRemaining -= Time.deltaTime;
            }
        }

        /// <summary>
        /// 負けて吹っ飛んでいる間の表示。
        ///
        /// 負けた瞬間は視点ごと吹き飛ばされるので、当たったこと自体に気づきにくい。
        /// スタンが明けるとリスポーンして手の選択が始まるため、
        /// 何が起きたのか分からないまま画面が切り替わってしまう。
        /// </summary>
        private void UpdateDefeatText()
        {
            bool show = player.IsDefeated;
            if (defeatText.enabled != show) defeatText.enabled = show;
            if (show) defeatText.text = "負けてしまった";
        }

        /// <summary>
        /// 飛行と滑空の状態。
        /// 残り時間で表せるものは効果一覧に寄せてあるが、滑空は高度次第で終わりが決まらないため、
        /// タイマーではなく状態としてこちらに出している。
        /// </summary>
        private void UpdateFlightText()
        {
            PlayerFlight flight = player.Flight;

            string message = string.Empty;

            if (flight != null)
            {
                if (flight.Phase == FlightPhase.Flying) message = $"飛行 {flight.FlightRemaining:0.0}";
                else if (flight.Phase == FlightPhase.Gliding) message = "滑空中";
            }

            bool show = message.Length > 0;
            if (flightText.enabled != show) flightText.enabled = show;
            if (show) flightText.text = message;
        }
    }
}
