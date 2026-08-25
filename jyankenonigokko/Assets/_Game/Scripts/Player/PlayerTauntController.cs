using UnityEngine;
using UnityEngine.InputSystem;

namespace MagicHand
{
    /// <summary>
    /// 試合中、オプション/デバッグパネルが開いていない間だけ、十字キー下で煽りエモートを再生する。
    ///
    /// MenuNavigateはオプションパネルの上下移動にも使う共有入力なので、
    /// パネルが十字キーを使っている間はここでは何もしない（<see cref="InGameOptionsMenu.IsInputCaptured"/>）。
    /// </summary>
    public class PlayerTauntController : MonoBehaviour
    {
        private static readonly int TauntId = Animator.StringToHash("Taunt");
        private const string LocomotionUnarmedState = "Locomotion_Unarmed";
        private const float DownThreshold = 0.5f;

        [Tooltip("この大きさ以上の移動入力が入ったら、再生中のエモートを打ち切る")]
        [SerializeField] private float moveCancelThreshold = 0.2f;

        [SerializeField] private PlayerController player;
        [SerializeField] private InGameOptionsMenu optionsMenu;
        [SerializeField] private Animator animator;

        private Vector2 input;
        private Vector2 moveInput;
        private bool wasDown;
        private bool wasTaunting;

        /// <summary>
        /// トリガーを撃った瞬間から、Tauntステートを抜けてLocomotionへ戻るまでtrue。
        ///
        /// AnyState→Taunt の遷移には duration（既定0.08秒）ぶんのブレンド時間があり、
        /// その間 Animator.GetCurrentAnimatorStateInfo は遷移元（Locomotion）を返し続ける。
        /// 「Taunt名のステートになったかどうか」だけを見て動きを止めていると、この
        /// ブレンド中は CanAct が true のままで、直前の移動入力がそのまま数フレーム
        /// 通り続けてしまう——「エモートを使うと後方にスライドしてから再生される」不具合の実体
        /// （後ろに下がりながら十字キー下を押すと、そのままの勢いがブレンド中だけ生き残る）。
        /// トリガーを撃った"その場"でこのフラグを立てて動きを止めることで、遷移の
        /// ブレンド時間を待たずに済む。
        /// </summary>
        private bool committedToTaunt;

        /// <summary>committedToTaunt になってから、実際に一度でもTauntステートへ入ったか。
        /// 「まだ遷移待ち」と「再生し終えて自然にLocomotionへ戻った」を区別するために要る。</summary>
        private bool hasEnteredTauntState;

        private void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (optionsMenu == null) optionsMenu = GetComponent<InGameOptionsMenu>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        private void OnMenuNavigate(InputValue value)
        {
            input = value.Get<Vector2>();
        }

        // PlayerController.OnMove と同じ "Move" アクションを、Send Messages で並行して受け取る。
        // 移動入力そのものはPlayerController側が握っているので、ここでは
        // 「動こうとしているか」の判定だけに使う自分専用のコピーを持つ
        private void OnMove(InputValue value)
        {
            moveInput = value.Get<Vector2>();
        }

        private void Update()
        {
            if (animator == null || player == null) return;

            GameManager manager = GameManager.Instance;
            bool inGame = manager != null && manager.CurrentState == GameState.InGame;

            bool captured = !inGame || (optionsMenu != null && optionsMenu.IsInputCaptured) || !player.CanAct;
            bool down = !captured && input.y <= -DownThreshold;

            // 地上でしか発動できない。空中で発動すると、CanAct=false中でも
            // PlayerController.ApplyExtraGravity は毎FixedUpdate無条件にかかり続けるため
            // （ノックバック中の落下などを止めないための仕様）、着地するまで見た目のポーズのまま
            // 落下し続けてしまう。これが「エモート中に位置が動く」不具合のもう一つの原因だった。
            //
            // committedToTaunt はここで即座に true にする（Animatorが実際にTauntへ遷移し終わる
            // のを待たない）。下のCanAct凍結・StopMotionもこの同じUpdate内で効くので、
            // ボタンを押した瞬間から動きが止まる
            if (down && !wasDown && player.IsGrounded && !committedToTaunt)
            {
                animator.SetTrigger(TauntId);
                committedToTaunt = true;
                hasEnteredTauntState = false;
            }
            wasDown = down;

            bool inTauntState = animator.GetCurrentAnimatorStateInfo(0).IsName("Taunt");

            // 移動しようとしたら即座に打ち切る。Playで強制的にLocomotionへ切り替えるので、
            // exitTimeの遷移を待たずその場で中断できる
            if (committedToTaunt && moveInput.sqrMagnitude > moveCancelThreshold * moveCancelThreshold)
            {
                animator.Play(LocomotionUnarmedState, 0, 0f);
                committedToTaunt = false;
                hasEnteredTauntState = false;
            }
            else if (committedToTaunt && inTauntState)
            {
                hasEnteredTauntState = true;
            }
            else if (committedToTaunt && hasEnteredTauntState && !inTauntState)
            {
                // Tauntの再生がexitTimeまで進み、自然にLocomotionへ戻った
                committedToTaunt = false;
                hasEnteredTauntState = false;
            }

            player.SetTaunting(committedToTaunt);

            // CanAct=false中は他のロック状態（スタン等）に合わせて既存の速度に触れない設計になっている
            // （ノックバックの慣性を殺さないための意図的な仕様）。だがエモートは自発的な演出で、
            // ノックバックのような「慣性を残したい」理由が無い。ここで触れないままだと、
            // 発動前に歩いていた勢いのままエモート中も滑り続けてしまう
            // （「エモート中にキャラの位置が動く」不具合の一因）。突入した瞬間だけ止める
            if (committedToTaunt && !wasTaunting) player.StopMotion();
            wasTaunting = committedToTaunt;
        }
    }
}
