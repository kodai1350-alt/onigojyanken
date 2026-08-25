using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace MagicHand
{
    /// <summary>
    /// 単一シーン内でゲーム全体の進行を統括する。
    /// State ごとにクラスを分けず enum + switch のシンプルな FSM で扱う。
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Match")]
        [SerializeField] private MatchTimer timer = new MatchTimer();
        [SerializeField, Min(0f)] private float countdownDuration = 3f;
        [Tooltip("3-2-1のあと「START」を表示しておく時間")]
        [SerializeField, Min(0.1f)] private float startTextDuration = 1f;
        [Tooltip("試合終了からResultへ移るまで「FINISH」を表示しておく時間")]
        [SerializeField, Min(0.1f)] private float finishDuration = 2f;

        [Header("Players")]
        [SerializeField] private List<PlayerController> players = new List<PlayerController>();

        [Header("Systems")]
        [SerializeField] private RespawnManager respawnManager;
        [SerializeField] private ItemSpawnManager itemSpawnManager;
        [Tooltip("イージーモード用の、縮小マップ側のリスポーン・アイテム湧き管理")]
        [SerializeField] private RespawnManager respawnManagerEasy;
        [SerializeField] private ItemSpawnManager itemSpawnManagerEasy;
        [SerializeField] private MatchSettings matchSettings;

        [Header("Lobby")]
        [SerializeField] private LobbyStartZone lobbyStartZone;
        [SerializeField] private Camera lobbyCamera;
        [SerializeField] private Transform[] lobbySpawnPoints;
        [Tooltip("俯瞰カメラの向き。準備ルームの移動入力はこの方位を基準にする")]
        [SerializeField] private float lobbyBasisYaw;

        private readonly int[] scores = new int[2];

        private ControllerPriorityAssigner controllerPriorityAssigner;

        [Header("Draw Contact")]
        [Tooltip("あいこでぶつかったときに弾く強さ。負け接触のノックバック(PlayerController.knockbackForce)と同じにしてある")]
        [SerializeField, Min(0f)] private float drawBounceForce = 12f;

        [SerializeField, Min(0f)] private float drawBounceUpForce = 5f;

        [Tooltip("同じ接触で何度も弾かないための間隔。重なっている間は毎フレーム判定が走るため必要")]
        [SerializeField, Min(0.1f)] private float drawBounceInterval = 0.8f;

        [Tooltip("点で負けている側だけが受け取る加速の倍率")]
        [SerializeField, Min(1f)] private float comebackSpeedMultiplier = 1.35f;

        [SerializeField, Min(0.1f)] private float comebackDuration = 3f;

        private float drawBounceTimer;
        private Coroutine finishRoutine;

        /// <summary>Finishを抜けたあとにどこへ進むか。同点の時間切れだけTieBreak、それ以外はResult。</summary>
        private GameState finishNextState = GameState.Result;

        /// <summary>
        /// サドンデス中か。先に1点入れた側がその瞬間に勝つ。
        /// 制限時間は動かさない（0のまま Tick すると即座に終了してしまうため）。
        /// </summary>
        public bool IsSuddenDeath { get; private set; }
        private Coroutine countdownRoutine;

        public GameState CurrentState { get; private set; } = GameState.Title;
        public IReadOnlyList<PlayerController> Players => players;
        public MatchTimer Timer => timer;
        public RespawnManager Respawns => ActiveRespawnManager;

        /// <summary>今の設定（イージーモードか）に応じて使うマップを切り替える。</summary>
        private RespawnManager ActiveRespawnManager =>
            (matchSettings != null && matchSettings.EasyMode && respawnManagerEasy != null) ? respawnManagerEasy : respawnManager;

        private ItemSpawnManager ActiveItemSpawnManager =>
            (matchSettings != null && matchSettings.EasyMode && itemSpawnManagerEasy != null) ? itemSpawnManagerEasy : itemSpawnManager;

        /// <summary>カウントダウン中の残り秒数。0以下なら非表示。</summary>
        public float CountdownRemaining { get; private set; }

        /// <summary>3-2-1のあと、試合開始の合図として「START」を出している間だけtrue。</summary>
        public bool ShowStartText { get; private set; }

        // UI 側は毎フレーム CurrentState / GetScore / CountdownRemaining を参照する。
        // イベント購読にすると Awake/OnEnable の実行順に依存して取りこぼすため。

        // ---- ライフサイクル -------------------------------------------------

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;

            controllerPriorityAssigner?.Dispose();
        }

        private void Start()
        {
            // PlayerInput 自身の初期化（内部の InputUser 登録）が終わっていないと
            // SwitchCurrentControlScheme が "Invalid user" で失敗する。
            // Awake同士の実行順はGameObjectの並びに依存して不安定なので、
            // 全員のAwake/OnEnableが終わってから走るStartでしか安全に呼べない
            if (players.Count >= 2)
            {
                controllerPriorityAssigner = new ControllerPriorityAssigner(
                    players[0].GetComponent<PlayerInput>(),
                    players[1].GetComponent<PlayerInput>(),
                    this);
            }

            // 設定パネルに並べる項目は、実際に湧く抽選候補から作る（両モードぶんまとめて登録）
            if (matchSettings != null && itemSpawnManager != null)
            {
                itemSpawnManager.RegisterLootWithSettings(matchSettings);
            }

            if (matchSettings != null && itemSpawnManagerEasy != null)
            {
                itemSpawnManagerEasy.RegisterLootWithSettings(matchSettings);
            }

            ChangeState(GameState.Title);
        }

        private void Update()
        {
            switch (CurrentState)
            {
                case GameState.Lobby:
                    UpdateLobby();
                    break;

                case GameState.Selection:
                    if (countdownRoutine == null && AreAllPlayersReady())
                    {
                        countdownRoutine = StartCoroutine(CountdownThenStart());
                    }
                    break;

                case GameState.InGame:
                    if (drawBounceTimer > 0f) drawBounceTimer -= Time.deltaTime;

                    // サドンデス中は時間で終わらない。決着は得点した瞬間だけ
                    if (!IsSuddenDeath && timer.Tick(Time.deltaTime))
                    {
                        // 同点でもそうでなくても、必ずFinishを一度挟む。
                        // 同点ならFinishの後にTieBreak（分岐）へ、そうでなければ直接Resultへ
                        finishNextState = ScoresTied() ? GameState.TieBreak : GameState.Result;
                        ChangeState(GameState.Finish);
                    }
                    break;
            }
        }

        // ---- ステート遷移 ---------------------------------------------------

        public void ChangeState(GameState next)
        {
            ExitState(CurrentState);
            CurrentState = next;
            EnterState(next);
        }

        private void ExitState(GameState state)
        {
            if (countdownRoutine != null && (state == GameState.Selection || state == GameState.Lobby))
            {
                StopCoroutine(countdownRoutine);
                countdownRoutine = null;
                SetCountdown(0f);
            }

            if (state == GameState.Lobby) SetLobbyCameraActive(false);
        }

        private void EnterState(GameState state)
        {
            switch (state)
            {
                case GameState.Title:
                    timer.Stop();
                    timer.ResetTimer();
                    StopAllSpawning();
                    ResetScores();
                    foreach (PlayerController player in players)
                    {
                        if (player == null) continue;
                        player.ResetForMatch();
                        player.SetInputActive(false);
                    }
                    PlacePlayersAtStart();
                    break;

                case GameState.Lobby:
                    timer.Stop();
                    ResetScores();
                    StopAllSpawning();
                    SetLobbyCameraActive(true);
                    PlacePlayersInLobby();
                    foreach (PlayerController player in players)
                    {
                        if (player == null) continue;
                        player.ResetForMatch();
                        player.SetInputActive(true);
                        player.UseOverheadControl(lobbyBasisYaw);
                        player.SwitchToMap(PlayerController.LobbyMap);
                    }
                    break;

                case GameState.Selection:
                    // 準備ルームで決めた制限時間を、ここで試合へ持ち込む
                    if (matchSettings != null) timer.SetDuration(matchSettings.MatchDuration);
                    timer.ResetTimer();
                    ResetScores();
                    PlacePlayersAtStart();
                    foreach (PlayerController player in players)
                    {
                        if (player == null) continue;
                        player.ResetForMatch();
                        player.SetInputActive(true);
                        player.UseThirdPersonControl();

                        // 試合開始は選ばせず、両者ともランダムで即決める。
                        // UIは出さず、既存の「全員選び終えたらカウントダウン」がそのまま働くので、
                        // 見えるのはカウントダウンだけになる
                        player.AssignRandomStartingHand();

                        // 手選択を経由しなくなったぶん、入力マップの切り替えもここで肩代わりする。
                        // 以前は BeginSelection→ConfirmHand の中で Gameplay マップへ戻していたが、
                        // それを呼ばなくなったので、準備ルームの Lobby マップのままカメラが動かなくなっていた
                        player.SwitchToMap(PlayerController.GameplayMap);
                    }
                    break;

                case GameState.InGame:
                    // サドンデスは時間で終わらせないので、計り直さず止めたままにする
                    if (IsSuddenDeath) timer.Stop();
                    else timer.Begin();

                    // 同点分岐で入力を切っているので、戻ってきたら必ず入れ直す
                    foreach (PlayerController player in players)
                    {
                        if (player == null) continue;
                        player.SetInputActive(true);
                    }

                    ActiveItemSpawnManager?.BeginSpawning();
                    break;

                case GameState.TieBreak:
                    // 選ぶまで試合を止める。アイテムの補充も止めて場を固定する
                    timer.Stop();
                    ActiveItemSpawnManager?.StopSpawning();
                    foreach (PlayerController player in players)
                    {
                        if (player == null) continue;
                        player.SetInputActive(false);
                        player.StopMotion();
                    }
                    break;

                case GameState.Finish:
                    timer.Stop();
                    ActiveItemSpawnManager?.StopSpawning();
                    foreach (PlayerController player in players)
                    {
                        if (player == null) continue;
                        player.SetInputActive(false);
                        player.StopMotion();
                    }
                    finishRoutine = StartCoroutine(FinishThenResult());
                    break;

                case GameState.Result:
                    timer.Stop();
                    ActiveItemSpawnManager?.StopSpawning();
                    foreach (PlayerController player in players)
                    {
                        if (player == null) continue;
                        player.SetInputActive(false);
                        player.StopMotion();
                    }
                    MatchResultData.Player1Score = GetScore(0);
                    MatchResultData.Player2Score = GetScore(1);

                    // LoadScene はこのフレームの描画が終わってから効くので、
                    // 先に画面を覆っておかないと試合画面が1フレーム残ってちらつく
                    SceneCover.Cover();
                    SceneManager.LoadScene("Victory");
                    break;
            }
        }

        /// <summary>タイトル画面の「対戦開始」から呼ばれる。まず準備ルームへ入る。</summary>
        public void StartMatch() => ChangeState(GameState.Lobby);

        /// <summary>
        /// ノーマル・イージー両方のアイテム湧きを止める。
        /// 準備ルームでモードを切り替えられるので、直前にどちらで遊んでいたかに関わらず
        /// 両方を止めて場を片付けないと、切り替え後にもう片方へ残り続けてしまう。
        /// </summary>
        private void StopAllSpawning()
        {
            if (itemSpawnManager != null) itemSpawnManager.StopSpawning();
            if (itemSpawnManagerEasy != null) itemSpawnManagerEasy.StopSpawning();
        }

        /// <summary>時間切れの時点で同点か。同点なら結果発表の前に分岐を挟む。</summary>
        public bool ScoresTied() => GetScore(0) == GetScore(1);

        /// <summary>
        /// 同点分岐で「サドンデス」を選んだとき。時間無制限で試合を再開し、
        /// 先に1点入れた側がその瞬間に勝つ。
        /// </summary>
        public void BeginSuddenDeath()
        {
            if (CurrentState != GameState.TieBreak) return;

            IsSuddenDeath = true;
            ChangeState(GameState.InGame);
        }

        /// <summary>同点分岐で「結果発表」を選んだとき。引き分けのまま終わる。</summary>
        public void FinishWithResult()
        {
            if (CurrentState != GameState.TieBreak) return;

            finishNextState = GameState.Result;
            ChangeState(GameState.Finish);
        }

        /// <summary>「FINISH」を一定時間見せてから、finishNextState（Result または TieBreak）へ進む。</summary>
        private IEnumerator FinishThenResult()
        {
            yield return new WaitForSeconds(finishDuration);
            finishRoutine = null;
            ChangeState(finishNextState);
        }

        // ---- 準備ルーム -----------------------------------------------------

        /// <summary>二人ともスタート地点に乗っている間だけカウントダウンを進める。</summary>
        private void UpdateLobby()
        {
            bool everyoneReady = AreAllPlayersOnStartZone();

            if (everyoneReady && countdownRoutine == null)
            {
                countdownRoutine = StartCoroutine(CountdownThenSelection());
            }
            else if (!everyoneReady && countdownRoutine != null)
            {
                // 誰かが離れたら中断する
                StopCoroutine(countdownRoutine);
                countdownRoutine = null;
                SetCountdown(0f);
            }
        }

        private bool AreAllPlayersOnStartZone()
        {
            if (lobbyStartZone == null || players.Count == 0) return false;

            foreach (PlayerController player in players)
            {
                if (player == null) continue;
                if (!lobbyStartZone.Contains(player.transform.position)) return false;
            }

            return true;
        }

        private IEnumerator CountdownThenSelection()
        {
            float remaining = countdownDuration;

            while (remaining > 0f)
            {
                SetCountdown(remaining);
                yield return null;
                remaining -= Time.deltaTime;
            }

            SetCountdown(0f);
            countdownRoutine = null;
            ChangeState(GameState.Selection);
        }

        /// <summary>準備ルームの入口に二人を並べる。</summary>
        private void PlacePlayersInLobby()
        {
            if (lobbySpawnPoints == null) return;

            for (int i = 0; i < players.Count && i < lobbySpawnPoints.Length; i++)
            {
                if (players[i] == null || lobbySpawnPoints[i] == null) continue;
                players[i].Teleport(lobbySpawnPoints[i].position, lobbySpawnPoints[i].rotation);
            }
        }

        /// <summary>
        /// 俯瞰カメラと分割TPSカメラの切り替え。
        /// Camera コンポーネントだけを切るのは、AudioListener を止めないようにするため。
        /// </summary>
        private void SetLobbyCameraActive(bool active)
        {
            if (lobbyCamera != null) lobbyCamera.enabled = active;

            foreach (PlayerController player in players)
            {
                if (player == null || player.CameraRig == null) continue;

                Camera playerCamera = player.CameraRig.GetComponent<Camera>();
                if (playerCamera != null) playerCamera.enabled = !active;
            }
        }

        /// <summary>リザルト画面の「タイトルへ戻る」から呼ばれる。</summary>
        public void ReturnToTitle() => ChangeState(GameState.Title);

        /// <summary>
        /// リザルト画面の「もう一度」から呼ばれる。設定は準備ルームにあるので、
        /// タイトルを経由せず直接そこへ戻して調整→再戦できるようにする。
        /// </summary>
        public void ReturnToLobby() => ChangeState(GameState.Lobby);

        private bool AreAllPlayersReady()
        {
            foreach (PlayerController player in players)
            {
                if (player == null) continue;
                if (player.IsSelecting) return false;
            }

            return players.Count > 0;
        }

        private IEnumerator CountdownThenStart()
        {
            // 手をランダムで決めた直後、InGameへ入るまでのカウントダウン。
            // 準備ルームで待っている間のカウントダウン（CountdownThenSelection）とは別で、
            // こちらが「試合開始」に直結するほうなのでSEはここで鳴らす
            SEPlayer.PlayCountdown();

            float remaining = countdownDuration;

            while (remaining > 0f)
            {
                SetCountdown(remaining);
                yield return null;
                remaining -= Time.deltaTime;
            }

            SetCountdown(0f);
            ShowStartText = true;
            yield return new WaitForSeconds(startTextDuration);
            ShowStartText = false;

            countdownRoutine = null;
            ChangeState(GameState.InGame);
        }

        private void SetCountdown(float value)
        {
            CountdownRemaining = value;
        }

        // ---- スコア ---------------------------------------------------------

        public int GetScore(int playerIndex)
        {
            return (playerIndex >= 0 && playerIndex < scores.Length) ? scores[playerIndex] : 0;
        }

        /// <summary>点をリセットするタイミングでサドンデスも解く。再戦へ持ち越さないため。</summary>
        private void ResetScores()
        {
            IsSuddenDeath = false;

            for (int i = 0; i < scores.Length; i++)
            {
                scores[i] = 0;
            }
        }

        /// <summary>勝ち属性で接触したときに PlayerCombat から呼ばれる。</summary>
        public void ResolveContact(PlayerController winner, PlayerController loser)
        {
            if (CurrentState != GameState.InGame) return;
            if (winner == null || loser == null) return;

            int index = players.IndexOf(winner);
            if (index >= 0 && index < scores.Length)
            {
                scores[index]++;
            }

            // サドンデスは1点で決着。負けた側のノックバックやリスポーンには入らせない
            if (IsSuddenDeath)
            {
                finishNextState = GameState.Result;
                ChangeState(GameState.Finish);
                return;
            }

            Vector3 direction = loser.transform.position - winner.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = winner.transform.forward;

            SEPlayer.PlayDefeat();
            loser.ApplyDefeat(direction.normalized);
        }

        /// <summary>
        /// あいこで接触したときの処理。PlayerCombat から、番号の小さい側だけが呼ぶ。
        ///
        /// 今までは同じ手同士だと何も起きず、重なったまま押し合うだけだった。
        /// 互いに弾いて距離を空けることで「手を変えるか離れるか」の判断を挟ませる。
        ///
        /// 加速は点で負けている側にだけ渡す。追いつく手立てを用意して、
        /// 一度離された側がそのまま終わるのを防ぐため。同点のときは誰にも渡さない。
        /// </summary>
        public void ResolveDraw(PlayerController a, PlayerController b)
        {
            if (CurrentState != GameState.InGame) return;
            if (a == null || b == null) return;

            // 重なっている間は毎フレーム呼ばれるので、一定間隔を空ける
            if (drawBounceTimer > 0f) return;
            drawBounceTimer = drawBounceInterval;

            Vector3 direction = b.transform.position - a.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = a.transform.forward;
            direction.Normalize();

            SEPlayer.PlayDraw();
            a.ApplyBounce(-direction, drawBounceForce, drawBounceUpForce);
            b.ApplyBounce(direction, drawBounceForce, drawBounceUpForce);

            GiveComeback(a, b);
        }

        /// <summary>点で負けている側にだけ加速を渡す。同点なら何もしない。</summary>
        private void GiveComeback(PlayerController a, PlayerController b)
        {
            int scoreA = GetScore(a.PlayerIndex);
            int scoreB = GetScore(b.PlayerIndex);
            if (scoreA == scoreB) return;

            PlayerController behind = scoreA < scoreB ? a : b;

            behind.ApplySpeedMultiplier(comebackSpeedMultiplier, comebackDuration);

            if (behind.Status != null)
            {
                behind.Status.Apply(PlayerStatusEffects.Comeback, "追い上げ", comebackDuration, new Color(1f, 0.55f, 0.15f));
            }
        }

        // ---- リスポーン -----------------------------------------------------

        /// <summary>相手から最も遠いリスポーン地点へ移す。</summary>
        public void RespawnPlayer(PlayerController player)
        {
            RespawnManager active = ActiveRespawnManager;
            if (player == null || active == null) return;

            PlayerController opponent = GetOpponent(player);
            Vector3 origin = opponent != null ? opponent.transform.position : Vector3.zero;

            RespawnPoint point = active.GetFarthestFrom(origin);
            if (point == null) return;

            player.Teleport(point.transform.position, point.transform.rotation);

            // 湧き先の足元にアイテムが落ちていると、復帰しただけで拾えてしまう
            ActiveItemSpawnManager?.RelocateAround(point.transform.position);
        }

        public PlayerController GetOpponent(PlayerController player)
        {
            foreach (PlayerController other in players)
            {
                if (other != null && other != player) return other;
            }

            return null;
        }

        /// <summary>試合開始時の初期配置。1Pをランダム地点に、2Pをそこから最も遠い地点に置く。</summary>
        private void PlacePlayersAtStart()
        {
            RespawnManager active = ActiveRespawnManager;
            if (active == null || players.Count == 0) return;

            RespawnPoint first = active.GetRandomPoint();
            if (first == null) return;

            if (players[0] != null)
            {
                players[0].Teleport(first.transform.position, first.transform.rotation);
            }

            if (players.Count > 1 && players[1] != null)
            {
                RespawnPoint second = active.GetFarthestFrom(first.transform.position);
                if (second != null)
                {
                    players[1].Teleport(second.transform.position, second.transform.rotation);
                }
            }
        }
    }
}
