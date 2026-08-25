using UnityEngine;

namespace MagicHand
{
    /// <summary>ほうきの飛行がいまどの段階にあるか。</summary>
    public enum FlightPhase
    {
        /// <summary>飛んでいない。</summary>
        None,

        /// <summary>自由飛行中。上下左右を自分で操れる。</summary>
        Flying,

        /// <summary>飛行時間が切れて滑空中。高度は下がる一方で、水平だけ弱く操れる。</summary>
        Gliding
    }

    /// <summary>
    /// ほうきの飛行を司る状態機械。
    ///
    /// 飛行3秒 → 滑空（落下速度固定） → 着地 → ペナルティ3秒、という一本道で進む。
    /// 途中で自分から降りることはできない。降りる理由が無いようにペナルティを一律にしてあるので、
    /// 中断できてもボタンが増えるだけで選択が生まれないため。
    ///
    /// 物理そのものは PlayerController が握っている。ここが持つのは
    /// 「いまどの段階か」「残り時間はいくつか」「速度はいくつであるべきか」までで、
    /// Rigidbody に触れるのは PlayerController 側に任せている。
    /// 壁ずりや接地判定と飛行の速度指定が別々の場所で Rigidbody を書き換えると、
    /// どちらが勝つのかが状況任せになって追えなくなるため。
    /// </summary>
    public class PlayerFlight : MonoBehaviour
    {
        [SerializeField] private PlayerController player;

        [Header("Flight")]
        [Tooltip("飛行できる時間。途中で降りることはできない")]
        [SerializeField, Min(0.1f)] private float flightDuration = 5f;

        [Tooltip("飛行中の水平速度。地上の移動速度(7)の1.75倍")]
        [SerializeField, Min(0.1f)] private float horizontalSpeed = 12.25f;

        [Tooltip("上昇・下降の速度")]
        [SerializeField, Min(0.1f)] private float verticalSpeed = 7f;

        [Tooltip("基準となる床からどれだけ上がれるか")]
        [SerializeField, Min(1f)] private float ceilingHeight = 14f;

        [Header("Glide")]
        [Tooltip("滑空中の落下速度")]
        [SerializeField, Min(0.1f)] private float glideFallSpeed = 4f;

        [Tooltip("滑空中の水平速度が飛行中の何割になるか")]
        [SerializeField, Range(0f, 1f)] private float glideControl = 0.5f;

        [Header("Landing Penalty")]
        [Tooltip("着地後の移動速度倍率")]
        [SerializeField, Range(0.1f, 1f)] private float landingSpeedMultiplier = 0.25f;

        [Tooltip("着地後のペナルティが続く時間")]
        [SerializeField, Min(0f)] private float landingPenaltyDuration = 3f;

        [Header("Exposure")]
        [Tooltip("着地後、相手の画面にだけ出す位置マーカー")]
        [SerializeField] private RevealMarker exposureMarkerPrefab;
        [SerializeField] private Vector3 exposureMarkerOffset = new Vector3(0f, 2.8f, 0f);

        [Header("Floor Reference")]
        [Tooltip("試合アリーナの1階の高さ。高度上限はここから測る")]
        [SerializeField] private float arenaFloorY;

        [Tooltip("準備ルームの1階の高さ。準備ルームは本編の遥か下に置かれているため別に持つ")]
        [SerializeField] private float lobbyFloorY = -100f;

        private float phaseTimer;
        private float exposureTimer;
        private float verticalInput;

        public FlightPhase Phase { get; private set; } = FlightPhase.None;

        /// <summary>
        /// デバッグモード専用。ONの間は時間切れで滑空に移行せず、無制限にホバー飛行できる。
        /// 着地ペナルティ・位置露出も付かない（壁・地形の当たり判定はいつも通り）。
        /// </summary>
        public bool CreativeFlight { get; private set; }

        /// <summary>クリエイティブ飛行のON/OFF。ONにした瞬間、地上にいてもそのまま浮き始める。</summary>
        public void SetCreativeFlight(bool enabled)
        {
            CreativeFlight = enabled;

            if (enabled)
            {
                Phase = FlightPhase.Flying;
                phaseTimer = float.PositiveInfinity;
                verticalInput = 0f;
            }
            else if (Phase != FlightPhase.None)
            {
                Cancel();
            }
        }

        /// <summary>ほうきに乗っている最中か。飛行中と滑空中の両方を含む。</summary>
        public bool IsRiding => Phase != FlightPhase.None;

        /// <summary>飛行の残り時間。HUDのゲージ用。滑空中は0。</summary>
        public float FlightRemaining => Phase == FlightPhase.Flying ? phaseTimer : 0f;

        /// <summary>相手に位置が見えている残り時間。0なら見えていない。</summary>
        public float ExposureRemaining => exposureTimer;

        /// <summary>いま出すべき水平速度。</summary>
        public float HorizontalSpeed =>
            Phase == FlightPhase.Gliding ? horizontalSpeed * glideControl : horizontalSpeed;

        /// <summary>上昇入力の強さ。-1（下降）～ 1（上昇）。搭乗姿勢の傾きにも使う。</summary>
        public float VerticalInput => Phase == FlightPhase.Flying ? verticalInput : (Phase == FlightPhase.Gliding ? -1f : 0f);

        /// <summary>いま到達できる高さの上限。</summary>
        public float CeilingY => FloorReference + ceilingHeight;

        /// <summary>
        /// 高度上限を測る基準の床。
        /// 準備ルームは本編アリーナの遥か下（y=-100）に置かれているので、
        /// ワールド座標をそのまま使うと準備ルームでは一切上がれなくなる。
        /// </summary>
        private float FloorReference
        {
            get
            {
                GameManager manager = GameManager.Instance;
                bool inLobby = manager != null && manager.CurrentState == GameState.Lobby;
                return inLobby ? lobbyFloorY : arenaFloorY;
            }
        }

        private void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
        }

        /// <summary>ほうきを発動して飛行を始める。</summary>
        public void BeginFlight()
        {
            if (player == null || player.IsDefeated) return;

            Phase = FlightPhase.Flying;
            phaseTimer = flightDuration;
            verticalInput = 0f;
        }

        /// <summary>上昇・下降の入力。PlayerController から渡される。</summary>
        public void SetVerticalInput(float value)
        {
            verticalInput = Mathf.Clamp(value, -1f, 1f);
        }

        /// <summary>
        /// いま出すべき垂直速度。
        /// 上限に達しているときは上昇入力を無視して、天井に押し付け続けないようにする。
        /// </summary>
        public float ResolveVerticalVelocity(float currentY)
        {
            switch (Phase)
            {
                case FlightPhase.Flying:
                {
                    float wanted = verticalInput * verticalSpeed;
                    if (wanted > 0f && currentY >= CeilingY) return 0f;
                    return wanted;
                }

                case FlightPhase.Gliding:
                    return -glideFallSpeed;

                default:
                    return 0f;
            }
        }

        private void Update()
        {
            if (exposureTimer > 0f) exposureTimer -= Time.deltaTime;

            // 動けなくなったら飛行を打ち切って落とす。
            // スタンを受けても飛び続けると、重力を切ったまま空中で固まってしまう。
            // 罰は付けない。スタンや敗北自体がすでに罰になっているため
            if (Phase != FlightPhase.None && player != null && !player.CanAct)
            {
                Cancel();
                return;
            }

            // クリエイティブ飛行はONの間ずっと有効。スタン等でCancelされても動けるようになり次第戻す
            if (CreativeFlight && Phase == FlightPhase.None && player != null && player.CanAct)
            {
                Phase = FlightPhase.Flying;
                phaseTimer = float.PositiveInfinity;
            }

            switch (Phase)
            {
                case FlightPhase.Flying:
                {
                    if (!CreativeFlight)
                    {
                        phaseTimer -= Time.deltaTime;
                        if (phaseTimer <= 0f) Phase = FlightPhase.Gliding;
                    }
                    break;
                }

                case FlightPhase.Gliding:
                {
                    // 滑空を始めた最初の1フレームはまだ地面から離れていないことがあるので、
                    // 落下速度が乗ってから接地を見る
                    if (player != null && player.IsGrounded) Land();
                    break;
                }
            }
        }

        /// <summary>着地して、減速と位置露出のペナルティを受ける。</summary>
        private void Land()
        {
            Phase = FlightPhase.None;
            verticalInput = 0f;

            if (player == null) return;

            player.ApplySpeedMultiplier(landingSpeedMultiplier, landingPenaltyDuration);
            exposureTimer = landingPenaltyDuration;

            if (player.Status != null)
            {
                player.Status.Apply(PlayerStatusEffects.LandingSlow, "減速", landingPenaltyDuration, new Color(0.78f, 0.62f, 0.30f));
                player.Status.Apply(PlayerStatusEffects.Exposed, "位置がバレている", landingPenaltyDuration, new Color(1f, 0.35f, 0.35f));
            }

            ExposePositionToOpponent();
        }

        /// <summary>
        /// 着地地点を相手にだけ知らせる。
        ///
        /// 索敵スクロールと同じマーカーを使うが、乗せるレイヤーが逆になる。
        /// 索敵は「使用者にだけ見える層」に置くのに対し、こちらは「相手にだけ見える層」に置く。
        /// </summary>
        private void ExposePositionToOpponent()
        {
            if (exposureMarkerPrefab == null) return;

            GameManager manager = GameManager.Instance;
            if (manager == null) return;

            PlayerController opponent = manager.GetOpponent(player);
            if (opponent == null) return;

            RevealMarker.Spawn(exposureMarkerPrefab, transform, exposureMarkerOffset,
                               landingPenaltyDuration, opponent.OwnViewLayer);
        }

        /// <summary>
        /// 飛行を打ち切る。敗北したときと試合の初期化で呼ばれる。
        /// ペナルティは付けない。倒された側にさらに罰を重ねる意味が無いため。
        /// </summary>
        public void Cancel()
        {
            Phase = FlightPhase.None;
            verticalInput = 0f;
            phaseTimer = 0f;
        }

        /// <summary>試合開始前の初期化。露出も含めて完全に消す。</summary>
        public void ResetState()
        {
            CreativeFlight = false;
            Cancel();
            exposureTimer = 0f;
        }
    }
}
