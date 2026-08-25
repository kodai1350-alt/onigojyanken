using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MagicHand
{
    /// <summary>
    /// プレイヤー1体分の移動・ジャンプ・視点・属性・被撃/リスポーンを司る。
    /// 入力は PlayerInput の Send Messages 経由（同一 GameObject に PlayerInput を置くこと）。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(ScrollStock))]
    [RequireComponent(typeof(PlayerFlight))]
    [RequireComponent(typeof(PlayerStatusEffects))]
    public class PlayerController : MonoBehaviour
    {
        public const string GameplayMap = "Gameplay";
        public const string SelectionMap = "Selection";
        public const string LobbyMap = "Lobby";

        /// <summary>手選択のカーソルが並ぶ順（UIの縦並びカードと同じ順序）。</summary>
        private static readonly HandType[] SelectableHands = { HandType.Gu, HandType.Choki, HandType.Pa };

        [Header("Identity")]
        [SerializeField, Min(0)] private int playerIndex;
        [SerializeField] private string displayName = "1P";

        [Header("Move")]
        [SerializeField] private float moveSpeed = 7f;
        [SerializeField] private float acceleration = 60f;
        [SerializeField, Range(0f, 1f)] private float airControl = 0.55f;
        [Tooltip("カメラの向きへ追従して回頭する速度。TPSでは視点とキャラの向きがずれると狙いにくいので高め")]
        [SerializeField] private float turnSpeed = 1440f;

        [Tooltip("接触面の法線のY成分がこの値未満なら『壁』とみなす。0.5 なら傾き60度より急な面が対象")]
        [SerializeField, Range(0.1f, 0.9f)] private float wallNormalThreshold = 0.5f;

        [Header("Jump")]
        [SerializeField] private float jumpVelocity = 11f;
        [SerializeField] private float extraGravity = 8f;
        [SerializeField] private float groundCheckRadius = 0.35f;
        [SerializeField] private float groundProbeHeight = 0.25f;
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("Selection")]
        [Tooltip("リスポーン時に手を選べる時間。選択中は無敵なので、無制限だと居座れてしまう")]
        [SerializeField, Min(1f)] private float selectionTimeLimit = 5f;

        [Tooltip("リスポーンで手を決めた直後の無敵時間。湧き際を狙い撃たれるのを防ぐ")]
        [SerializeField, Min(0f)] private float respawnInvincibleDuration = 3f;

        [Header("Defeat")]
        [SerializeField] private float knockbackForce = 12f;
        [SerializeField] private float knockbackUpForce = 5f;
        [SerializeField] private float stunDuration = 2f;
        [SerializeField] private bool clearScrollOnDefeat = false;

        [Header("References")]
        [SerializeField] private ThirdPersonCameraRig cameraRig;

        [Header("Physics Materials")]
        [Tooltip("接地中に使う摩擦あり材質。坂の上で静止できるようにするため")]
        [SerializeField] private PhysicsMaterial groundedMaterial;

        [Tooltip("空中で使う摩擦ゼロ材質。壁に触れても摩擦で落下が止まらないようにするため")]
        [SerializeField] private PhysicsMaterial airborneMaterial;

        private static readonly Collider[] GroundHits = new Collider[8];

        private readonly List<Vector3> wallNormals = new List<Vector3>();

        private Rigidbody body;
        private Collider bodyCollider;
        private PlayerInput playerInput;
        private ScrollStock scrolls;
        private PlayerFlight flight;
        private PlayerStatusEffects status;

        private Vector2 moveInput;
        private Vector2 lookInput;
        private Vector2 navigateInput;

        // 上昇・下降だけは PlayerInput のメッセージを使わず、毎フレーム値を読む。理由は ReadVerticalInput を参照
        private InputActionMap cachedMap;
        private InputAction ascendAction;
        private InputAction descendAction;
        private float speedMultiplier = 1f;
        private float stunTimer;
        private float invincibleTimer;
        private float speedBuffTimer;
        private Coroutine defeatRoutine;
        private bool groundedCache;
        private int groundedFrame = -1;

        // 俯瞰カメラの準備ルームでは、自分のTPSカメラではなく固定の方位を移動の基準にする
        private bool useFixedBasis;
        private float fixedBasisYaw;

        // ---- 公開状態 -------------------------------------------------------

        public int PlayerIndex => playerIndex;
        public string DisplayName => displayName;
        public HandType CurrentHand { get; private set; } = HandType.None;
        public ScrollStock Scrolls => scrolls;
        public PlayerFlight Flight => flight;

        /// <summary>今かかっている時間制限つきの効果。HUDの残り時間表示はここを見る。</summary>
        public PlayerStatusEffects Status => status;
        public ThirdPersonCameraRig CameraRig => cameraRig;

        /// <summary>ほうきに乗っている最中か。飛行中と滑空中を含む。</summary>
        public bool IsRiding => flight != null && flight.IsRiding;

        /// <summary>属性選択UIを表示中か。</summary>
        public bool IsSelecting { get; private set; }

        /// <summary>手を選べる残り時間。切れると自動で決まる。</summary>
        public float SelectionRemaining { get; private set; }

        /// <summary>敗北してノックバック～リスポーンの最中か。</summary>
        public bool IsDefeated { get; private set; }

        public bool IsStunned => stunTimer > 0f;

        /// <summary>
        /// 煽りエモートの再生中か。<see cref="PlayerTauntController"/> が
        /// Animatorの実際の再生状態（"Taunt"ステートかどうか）を見て毎フレーム設定する。
        /// 再生中に動けてしまうと、身振りのポーズのまま見た目のキャラだけがすり抜けたように
        /// 移動して見える（「エモート中にキャラの位置が動く」不具合の実体）ため、
        /// 他のロック状態（選択中・スタン・敗北）と同じ扱いで CanAct に含めている。
        /// </summary>
        public bool IsTaunting { get; private set; }

        public void SetTaunting(bool value) => IsTaunting = value;

        /// <summary>
        /// 接触判定を受け付けない状態。
        /// 選択中・敗北処理中に加えて、リスポーン直後の猶予もここに含める。
        /// </summary>
        public bool IsInvincible => IsSelecting || IsDefeated || invincibleTimer > 0f;

        /// <summary>リスポーン直後の無敵の残り時間。0なら無敵ではない。</summary>
        public float InvincibleRemaining => invincibleTimer;

        /// <summary>手選択カーソルが今指している手。SelectionUI が強調表示に使う。</summary>
        public HandType SelectionCandidate => SelectableHands[Mathf.Clamp(selectionCursor, 0, SelectableHands.Length - 1)];

        /// <summary>入力を受け付けて動ける状態か。試合中と準備ルームで動ける。</summary>
        public bool CanAct
        {
            get
            {
                if (IsSelecting || IsStunned || IsDefeated || IsTaunting) return false;
                if (GameManager.Instance == null) return false;

                GameState state = GameManager.Instance.CurrentState;
                return state == GameState.InGame || state == GameState.Lobby;
            }
        }

        /// <summary>
        /// 巻物を使える状態か。スタン中は動けないが、脱出手段として
        /// ワープ（<see cref="TeleportEffectSO"/>）だけは例外で使える。
        /// </summary>
        public bool CanUseScroll
        {
            get
            {
                if (IsSelecting || IsDefeated) return false;
                if (GameManager.Instance == null) return false;

                GameState state = GameManager.Instance.CurrentState;
                if (state != GameState.InGame && state != GameState.Lobby) return false;

                if (IsStunned) return scrolls != null && scrolls.Current is TeleportEffectSO;
                return true;
            }
        }

        public event Action<PlayerController> SelectionStarted;
        public event Action<PlayerController> SelectionCompleted;
        public event Action<PlayerController, HandType> HandChanged;

        private int selectionCursor;
        private HandType lastConfirmedHand = HandType.Gu;

        /// <summary>この番号のレイヤーは自分のカメラにしか映らない。壁越しマーカー等の置き場。</summary>
        [SerializeField] private int ownViewLayer;
        public int OwnViewLayer => ownViewLayer;

        // ---- ライフサイクル -------------------------------------------------

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            bodyCollider = GetComponent<CapsuleCollider>();
            scrolls = GetComponent<ScrollStock>();
            flight = GetComponent<PlayerFlight>();
            status = GetComponent<PlayerStatusEffects>();
            playerInput = GetComponent<PlayerInput>();

            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            if (cameraRig != null) cameraRig.SetTarget(transform);
        }

        private void Update()
        {
            if (IsSelecting) TickSelectionTimer();

            if (stunTimer > 0f) stunTimer -= Time.deltaTime;
            if (invincibleTimer > 0f) invincibleTimer -= Time.deltaTime;

            if (speedBuffTimer > 0f)
            {
                speedBuffTimer -= Time.deltaTime;
                if (speedBuffTimer <= 0f) speedMultiplier = 1f;
            }

            if (flight != null) flight.SetVerticalInput(CanAct ? ReadVerticalInput() : 0f);

            if (cameraRig != null)
            {
                cameraRig.SetLookInput(CanAct ? lookInput : Vector2.zero);
                cameraRig.SetDeepLookAllowed(IsRiding);

                if (MatchSettings.Instance != null)
                {
                    cameraRig.SetSensitivity(MatchSettings.Instance.GetSensitivity(playerIndex));
                    cameraRig.SetInvertPitch(MatchSettings.Instance.IsLookInverted(playerIndex));
                    cameraRig.SetFieldOfView(MatchSettings.Instance.GetFieldOfView(playerIndex));
                }
            }
        }

        private void FixedUpdate()
        {
            UpdatePhysicsMaterial();
            UpdateFlightGravity();
            ApplyExtraGravity();

            // スタン中・選択中はノックバックの慣性を殺さないため速度に触れない
            if (CanAct)
            {
                Vector3 desired = ClipAgainstWalls(ResolveDesiredVelocity());
                Vector3 current = body.linearVelocity;
                Vector3 horizontal = new Vector3(current.x, 0f, current.z);
                float responsiveness = acceleration * (IsGrounded || IsRiding ? 1f : airControl);

                Vector3 next = Vector3.MoveTowards(horizontal, desired, responsiveness * Time.fixedDeltaTime);

                // 壁に押し付ける成分は実速度からも落とす。残しておくと接触が続き、
                // 摩擦で落下が止まって壁に張り付いたように見えてしまう
                next = ClipAgainstWalls(next);

                body.linearVelocity = new Vector3(next.x, ResolveVerticalVelocity(current.y), next.z);

                FaceMoveDirection(desired);
                ClampToFlightCeiling();
            }

            // 接触情報は物理ステップ後のコールバックで集め直すので毎回捨てる
            wallNormals.Clear();
        }

        /// <summary>
        /// 飛行中の垂直速度。ほうきに乗っていないときは今の速度をそのまま返す。
        ///
        /// 飛行中だけ縦を明示的に上書きするのは、重力任せにすると
        /// 「入力を放したらその高さで止まる」というクリエイティブ飛行の手触りにならないため。
        /// </summary>
        private float ResolveVerticalVelocity(float currentY)
        {
            if (flight == null || !flight.IsRiding) return currentY;
            return flight.ResolveVerticalVelocity(transform.position.y);
        }

        /// <summary>飛行中は重力を切る。滑空も落下速度を固定したいので同じく切ったままにする。</summary>
        private void UpdateFlightGravity()
        {
            bool wanted = !IsRiding;
            if (body.useGravity != wanted) body.useGravity = wanted;
        }

        /// <summary>
        /// 高度の上限で頭打ちにする。
        /// 速度側で止めるだけだと、上昇中に上限を跨いだフレームで少し行き過ぎるため、
        /// 位置そのものも抑えて上限を厳密に守らせる。
        /// </summary>
        private void ClampToFlightCeiling()
        {
            if (flight == null || !flight.IsRiding) return;

            float ceiling = flight.CeilingY;
            if (transform.position.y <= ceiling) return;

            Vector3 clamped = transform.position;
            clamped.y = ceiling;
            body.position = clamped;
            transform.position = clamped;

            Vector3 v = body.linearVelocity;
            if (v.y > 0f) body.linearVelocity = new Vector3(v.x, 0f, v.z);
        }

        /// <summary>
        /// 壁との接触面。OnCollisionStay は物理ステップの後に呼ばれるため、
        /// ここで集めた法線は次の FixedUpdate で使う（1ステップ遅れだが体感には出ない）。
        /// </summary>
        private void OnCollisionStay(Collision collision)
        {
            for (int i = 0; i < collision.contactCount; i++)
            {
                Vector3 normal = collision.GetContact(i).normal;

                // 床や坂（法線が上向き）は対象外。垂直に近い面だけを壁とみなす
                if (Mathf.Abs(normal.y) >= wallNormalThreshold) continue;

                Vector3 flat = new Vector3(normal.x, 0f, normal.z);
                if (flat.sqrMagnitude < 0.0001f) continue;

                wallNormals.Add(flat.normalized);
            }
        }

        /// <summary>壁にめり込む向きの速度成分を取り除き、壁に沿って滑る速度だけを残す。</summary>
        private Vector3 ClipAgainstWalls(Vector3 velocity)
        {
            for (int i = 0; i < wallNormals.Count; i++)
            {
                float into = Vector3.Dot(velocity, wallNormals[i]);
                if (into < 0f) velocity -= wallNormals[i] * into;
            }

            return velocity;
        }

        /// <summary>
        /// 接地中は摩擦あり、空中は摩擦ゼロに切り替える。
        /// 空中で摩擦があると壁に押し付けたときに落下が止まって張り付き、
        /// 逆に接地中に摩擦が無いと坂の上でじりじり滑り落ちてしまうため、状況で使い分ける。
        /// </summary>
        private void UpdatePhysicsMaterial()
        {
            if (bodyCollider == null || groundedMaterial == null || airborneMaterial == null) return;

            // ほうきに乗っている間は地面すれすれを飛んでも摩擦で引っかからないよう空中扱いにする
            PhysicsMaterial wanted = IsGrounded && !IsRiding ? groundedMaterial : airborneMaterial;
            if (bodyCollider.sharedMaterial != wanted) bodyCollider.sharedMaterial = wanted;
        }

        private void ApplyExtraGravity()
        {
            // 既定重力だけだと浮遊感が強いので落下を締める。
            // ほうきに乗っている間は高度を自分で決めるので掛けない
            if (!IsGrounded && !IsRiding)
            {
                body.AddForce(Vector3.down * extraGravity, ForceMode.Acceleration);
            }
        }

        private Vector3 ResolveDesiredVelocity()
        {
            Quaternion basis = ResolveMovementBasis();
            Vector3 direction = (basis * Vector3.forward) * moveInput.y + (basis * Vector3.right) * moveInput.x;
            if (direction.sqrMagnitude > 1f) direction.Normalize();

            // 飛行中は地上の速度も減速デバフも参照しない。
            // 着地ペナルティ中に拾い直したほうきが遅くなるのは意図しないため
            if (IsRiding) return direction * flight.HorizontalSpeed;

            return direction * (moveSpeed * speedMultiplier);
        }

        /// <summary>
        /// 移動入力を world 方向へ変換する基準。
        /// 試合中は自分の追従カメラの向き、準備ルームでは共有の固定カメラの向きを使う。
        /// </summary>
        private Quaternion ResolveMovementBasis()
        {
            if (useFixedBasis) return Quaternion.Euler(0f, fixedBasisYaw, 0f);
            return Quaternion.Euler(0f, cameraRig != null ? cameraRig.Yaw : 0f, 0f);
        }

        /// <summary>
        /// 試合中の操作方式。移動はカメラ基準だが、キャラは進行方向を向く（視点と移動は独立）。
        ///
        /// カメラの向きへ常に正対させる方式（フォートナイト型）だと横歩き・後退ができる代わりに、
        /// そのとき前進モーションが横向き・後ろ向きに再生されて破綻する。
        /// このアセットには横歩き・後退のクリップが無く2次元ブレンドを組めないため、
        /// 「向いている方向へ走る」方式にして見た目と実際の動きを一致させている。
        /// </summary>
        public void UseThirdPersonControl()
        {
            useFixedBasis = false;
        }

        /// <summary>準備ルームの操作方式。固定俯瞰カメラ基準で動き、進行方向を向く。</summary>
        public void UseOverheadControl(float basisYaw)
        {
            useFixedBasis = true;
            fixedBasisYaw = basisYaw;
            lookInput = Vector2.zero;
        }

        /// <summary>進んでいる方向を向く。走りモーションの向きと実際の移動方向が一致する。</summary>
        private void FaceMoveDirection(Vector3 desired)
        {
            Vector3 flat = new Vector3(desired.x, 0f, desired.z);
            if (flat.sqrMagnitude < 0.01f) return;

            Quaternion target = Quaternion.LookRotation(flat.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.fixedDeltaTime);
        }

        /// <summary>足元の接地判定。自分自身のコライダーは除外する。</summary>
        public bool IsGrounded
        {
            get
            {
                if (groundedFrame == Time.frameCount) return groundedCache;
                groundedFrame = Time.frameCount;
                groundedCache = ProbeGround();
                return groundedCache;
            }
        }

        private bool ProbeGround()
        {
            Vector3 origin = transform.position + Vector3.up * groundProbeHeight;
            int count = Physics.OverlapSphereNonAlloc(origin, groundCheckRadius, GroundHits, groundMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider hit = GroundHits[i];
                if (hit == null || hit.transform.IsChildOf(transform)) continue;
                return true;
            }

            return false;
        }

        // ---- 入力（PlayerInput / Send Messages） -----------------------------

        private void OnMove(InputValue value) => moveInput = value.Get<Vector2>();

        private void OnLook(InputValue value) => lookInput = value.Get<Vector2>();

        private void OnJump(InputValue value)
        {
            if (!value.isPressed || !CanAct || !IsGrounded) return;

            // 地面すれすれを飛んでいるときにジャンプが割り込むと高度制御が乱れる
            if (IsRiding) return;

            Vector3 v = body.linearVelocity;
            body.linearVelocity = new Vector3(v.x, jumpVelocity, v.z);
        }

        /// <summary>
        /// 上昇・下降の入力。押している間ずっと欲しいので、他の入力と違って毎フレーム自分で読む。
        ///
        /// PlayerInput の Send Messages は、ボタン型のアクションでは
        /// **押した瞬間しかメッセージを送らない**（離した瞬間の canceled は値型のアクションにしか送られない）。
        /// そのためメッセージで受けると、一度押した値が1のまま残り続ける。
        /// 上昇も下降も1に張り付いて差し引き0になり、上下とも動かなくなる。
        /// </summary>
        private float ReadVerticalInput()
        {
            RefreshVerticalActions();

            float value = 0f;
            if (ascendAction != null && ascendAction.IsPressed()) value += 1f;
            if (descendAction != null && descendAction.IsPressed()) value -= 1f;

            // 両方押していれば打ち消し合う。マイクラのクリエイティブと同じ挙動
            return value;
        }

        /// <summary>入力マップは準備ルームと試合で切り替わるので、変わったときだけ引き直す。</summary>
        private void RefreshVerticalActions()
        {
            InputActionMap map = playerInput != null ? playerInput.currentActionMap : null;
            if (map == cachedMap) return;

            cachedMap = map;
            ascendAction = map != null ? map.FindAction("Ascend") : null;
            descendAction = map != null ? map.FindAction("Descend") : null;
        }

        private void OnUseScroll(InputValue value)
        {
            if (!value.isPressed || !CanUseScroll) return;
            scrolls.TryUse(this);
        }

        /// <summary>
        /// 手選択のカーソル移動。スティックや十字キーは「倒している間ずっと入力が来る」ので、
        /// ニュートラルから倒された瞬間だけを拾って1段ずつ動かす。
        /// </summary>
        private void OnNavigate(InputValue value)
        {
            Vector2 next = value.Get<Vector2>();
            int step = ResolveVerticalStep(navigateInput, next);
            navigateInput = next;

            if (step == 0 || !IsSelecting) return;

            // 画面の上が配列の先頭なので、上入力でインデックスを減らす
            int count = SelectableHands.Length;
            selectionCursor = (selectionCursor - step + count) % count;
        }

        private void OnConfirm(InputValue value)
        {
            if (!value.isPressed || !IsSelecting) return;
            ConfirmHand(SelectionCandidate);
        }

        /// <summary>ニュートラル(±0.5未満)から倒された瞬間だけ +1 / -1 を返す。</summary>
        private static int ResolveVerticalStep(Vector2 previous, Vector2 current)
        {
            const float threshold = 0.5f;

            if (Mathf.Abs(previous.y) >= threshold) return 0;
            if (current.y >= threshold) return 1;
            if (current.y <= -threshold) return -1;

            return 0;
        }

        // ---- 属性選択 -------------------------------------------------------

        /// <summary>
        /// 属性選択モードに入る（開始時／リスポーン時）。この間は無敵。
        /// </summary>
        /// <param name="timed">
        /// 制限時間を付けるか。リスポーンは true、試合開始の1回目だけ false で呼ぶ。
        /// </param>
        public void BeginSelection()
        {
            IsSelecting = true;
            SelectionRemaining = selectionTimeLimit;
            invincibleTimer = 0f;
            moveInput = Vector2.zero;
            lookInput = Vector2.zero;
            navigateInput = Vector2.zero;

            // 前回選んだ手の位置から始める。同じ手を続けるなら決定1回で済む
            selectionCursor = Mathf.Max(0, Array.IndexOf(SelectableHands, lastConfirmedHand));

            SetHand(HandType.None);
            StopMotion();
            SwitchMap(SelectionMap);
            SelectionStarted?.Invoke(this);
        }

        /// <summary>
        /// 選べる時間を削り、切れたらランダムで決めてしまう。
        ///
        /// 選択中は無敵なので、時間無制限だと選ばずに居座るだけで安全地帯になってしまう。
        /// リスポーンのたびに試合が止まるのも避けたい。
        /// 自動で決めるのはランダム。カーソルの位置で決めると、
        /// 「置いておけば必ずこの手になる」と読まれて、待つこと自体が戦術になってしまう。
        /// </summary>
        private void TickSelectionTimer()
        {
            SelectionRemaining -= Time.deltaTime;
            if (SelectionRemaining > 0f) return;

            SelectionRemaining = 0f;
            ConfirmHand(SelectableHands[UnityEngine.Random.Range(0, SelectableHands.Length)]);
        }

        /// <summary>選択された属性を確定してゲームプレイに復帰する。</summary>
        public void ConfirmHand(HandType hand)
        {
            if (!IsSelecting || hand == HandType.None) return;

            IsSelecting = false;
            SelectionRemaining = 0f;
            lastConfirmedHand = hand;
            SetHand(hand);
            SwitchMap(GameplayMap);

            // リスポーン直後は相手が湧き先へ先回りできてしまうので、動き出しに猶予を与える
            GrantInvincibility(respawnInvincibleDuration);

            SelectionCompleted?.Invoke(this);
        }

        /// <summary>一定時間、接触判定を受け付けなくする。</summary>
        public void GrantInvincibility(float duration)
        {
            if (duration <= 0f) return;

            invincibleTimer = Mathf.Max(invincibleTimer, duration);
            if (status != null) status.Apply(PlayerStatusEffects.Invincible, "無敵", duration, new Color(1f, 0.95f, 0.6f));
        }

        /// <summary>
        /// 試合開始時、選ばせずにランダムで手を決める。
        /// UIを出さずに即決めることで、既存の「全員選び終えたらカウントダウン」の仕組みが
        /// そのまま働き、選択画面を経ずにカウントダウンだけが見える形になる。
        /// </summary>
        public void AssignRandomStartingHand()
        {
            HandType hand = SelectableHands[UnityEngine.Random.Range(0, SelectableHands.Length)];
            lastConfirmedHand = hand;
            SetHand(hand);
        }

        /// <summary>手変更アイテム等から属性を直接上書きする。</summary>
        public void SetHand(HandType hand)
        {
            if (CurrentHand == hand) return;
            CurrentHand = hand;
            HandChanged?.Invoke(this, hand);
        }

        // ---- 被撃・リスポーン -----------------------------------------------

        /// <summary>
        /// あいこでぶつかったときの軽い弾かれ。負けたわけではないのでスタンもリスポーンもしない。
        ///
        /// 同じ手同士だと今まで何も起きず、重なったまま押し合うだけだった。
        /// 弾いて距離を空けることで、手を変えるか離れるかの判断を挟ませる。
        /// </summary>
        public void ApplyBounce(Vector3 horizontalDirection, float force, float upForce)
        {
            if (IsInvincible) return;

            Vector3 flat = new Vector3(horizontalDirection.x, 0f, horizontalDirection.z);
            if (flat.sqrMagnitude < 0.0001f) flat = transform.forward;

            // 押し合いで溜まった速度に足すと弾かれ方が安定しないので、水平だけ一度捨てる
            Vector3 velocity = body.linearVelocity;
            body.linearVelocity = new Vector3(0f, velocity.y, 0f);
            body.AddForce(flat.normalized * force + Vector3.up * upForce, ForceMode.VelocityChange);
        }

        /// <summary>負け接触時の処理。ノックバック＋スタン後にリスポーンして属性選択へ。</summary>
        public void ApplyDefeat(Vector3 horizontalDirection)
        {
            if (defeatRoutine != null) StopCoroutine(defeatRoutine);
            defeatRoutine = StartCoroutine(DefeatSequence(horizontalDirection));
        }

        private IEnumerator DefeatSequence(Vector3 horizontalDirection)
        {
            IsDefeated = true;
            moveInput = Vector2.zero;
            lookInput = Vector2.zero;
            if (clearScrollOnDefeat) scrolls.Clear();

            // 飛行の高度制御とノックバックが同時に速度を書き換えると吹き飛び方が壊れる。
            // 倒された側にペナルティを重ねる意味も無いので、罰無しで打ち切る
            if (flight != null) flight.Cancel();

            body.linearVelocity = Vector3.zero;
            body.AddForce(horizontalDirection.normalized * knockbackForce + Vector3.up * knockbackUpForce,
                          ForceMode.VelocityChange);
            stunTimer = stunDuration;

            yield return new WaitForSeconds(stunDuration);

            if (GameManager.Instance != null) GameManager.Instance.RespawnPlayer(this);
            IsDefeated = false;
            BeginSelection();
            defeatRoutine = null;
        }

        /// <summary>スクロール等による外部からのスタン付与。</summary>
        public void Stun(float duration)
        {
            if (IsInvincible) return;
            stunTimer = Mathf.Max(stunTimer, duration);
            StopMotion();
        }

        /// <summary>
        /// 一定時間の移動速度倍率を適用する（自己強化にも相手からの妨害にも使う共通API）。
        /// 強化（倍率1以上）は、今かかっている強化より弱ければ無視する（拾い直しで誤って弱いバフに
        /// 上書きされないようにするため）。妨害としての減速（倍率1未満）は、自分が強化中かどうかに
        /// 関わらず常に適用する。強化が減速への耐性になってしまうのは意図しない挙動のため。
        /// </summary>
        public void ApplySpeedMultiplier(float multiplier, float duration)
        {
            bool isDebuff = multiplier < 1f;
            if (!isDebuff && multiplier <= speedMultiplier && speedBuffTimer > 0f) return;

            speedMultiplier = Mathf.Max(0.1f, multiplier);
            speedBuffTimer = duration;
        }

        public void TeleportForward(float distance, LayerMask blockMask)
        {
            Teleport(ResolveTeleportTarget(distance, blockMask), transform.rotation);
        }

        /// <summary>
        /// ワープで実際に着く地点。
        ///
        /// 発動前の着地点表示もこれを呼ぶ。別々に計算すると表示と実際がずれて、
        /// 「見えている場所と違うところへ飛ぶ」という嘘の情報になるため。
        /// </summary>
        public Vector3 ResolveTeleportTarget(float distance, LayerMask blockMask)
        {
            Vector3 origin = transform.position + Vector3.up * 0.9f;
            Vector3 direction = transform.forward;
            float allowed = distance;

            if (Physics.SphereCast(origin, 0.4f, direction, out RaycastHit hit, distance, blockMask, QueryTriggerInteraction.Ignore))
            {
                allowed = Mathf.Max(0f, hit.distance - 0.5f);
            }

            return transform.position + direction * allowed;
        }

        /// <summary>物理状態を含めて完全に指定地点へ移す。</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            StopMotion();
            body.position = position;
            body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            if (cameraRig != null) cameraRig.SnapToTarget();
        }

        public void StopMotion()
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        /// <summary>試合外（Title / Result）で入力を止める。</summary>
        public void SetInputActive(bool active)
        {
            if (playerInput == null) return;

            if (active) playerInput.ActivateInput();
            else playerInput.DeactivateInput();

            if (!active)
            {
                moveInput = Vector2.zero;
                lookInput = Vector2.zero;
            }
        }

        /// <summary>試合開始前の初期化。スコア以外のプレイヤー状態をリセットする。</summary>
        public void ResetForMatch()
        {
            if (defeatRoutine != null)
            {
                StopCoroutine(defeatRoutine);
                defeatRoutine = null;
            }

            IsDefeated = false;
            IsSelecting = false;
            stunTimer = 0f;
            invincibleTimer = 0f;
            speedBuffTimer = 0f;
            speedMultiplier = 1f;
            if (flight != null) flight.ResetState();
            if (status != null) status.Clear();
            scrolls.Clear();
            SetHand(HandType.None);
            StopMotion();
        }

        /// <summary>State 側から入力マップを切り替える（準備ルームへの出入りなど）。</summary>
        public void SwitchToMap(string mapName) => SwitchMap(mapName);

        private void SwitchMap(string mapName)
        {
            if (playerInput == null || playerInput.actions == null) return;
            if (playerInput.currentActionMap != null && playerInput.currentActionMap.name == mapName) return;

            playerInput.SwitchCurrentActionMap(mapName);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * groundProbeHeight, groundCheckRadius);
        }
    }
}
