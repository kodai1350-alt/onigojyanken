using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// プレイヤーの実際の動きを見て、キャラクターモデルのアニメーションを切り替える。
    ///
    /// PlayerController 側から「今どのモーション」と指示するのではなく、
    /// Rigidbody の速度という結果を見て決める形にしてある。
    /// スクロールの加速・ノックバック・坂の滑りなど、
    /// 入力以外で動く要因が多く、入力を基準にすると見た目と実際がずれるため。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class PlayerAnimatorDriver : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int HitId = Animator.StringToHash("Hit");
        private static readonly int ArmedId = Animator.StringToHash("Armed");
        private static readonly int FlyingId = Animator.StringToHash("Flying");

        [SerializeField] private PlayerController player;
        [SerializeField] private Rigidbody body;
        [SerializeField] private Animator animator;

        [Tooltip("この速さでアニメーションの Speed が 1（全力疾走）になる")]
        [SerializeField, Min(0.1f)] private float runSpeed = 7f;

        [Tooltip("速度の変化をならす時間。カクつきを防ぐ")]
        [SerializeField, Min(0f)] private float damping = 0.12f;

        private bool wasDefeated;

        private void Awake()
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (body == null && player != null) body = player.GetComponent<Rigidbody>();
        }

        private void Update()
        {
            if (animator == null || player == null || body == null) return;

            Vector3 velocity = body.linearVelocity;
            float planarSpeed = new Vector2(velocity.x, velocity.z).magnitude;

            // スタン中・選択中は動けないので、見た目も止めて棒立ちにする
            float normalized = player.CanAct ? Mathf.Clamp01(planarSpeed / runSpeed) : 0f;

            animator.SetFloat(SpeedId, normalized, damping, Time.deltaTime);
            animator.SetBool(GroundedId, player.IsGrounded);

            // 杖を持っているときだけ杖構えのモーションにする。
            // 素手なのに杖を構えた姿勢で走っていた見た目のずれを解消するため
            animator.SetBool(ArmedId, player.Scrolls != null && player.Scrolls.HasStock);

            // ほうきに乗っている間は移動モーションを完全に止めて、またがった姿勢に差し替える。
            // 速度は10.5m/s出ているので、放っておくと空中で全力疾走してしまう
            animator.SetBool(FlyingId, player.IsRiding);

            // 負けた瞬間だけ一度、被弾モーションを再生する
            if (player.IsDefeated && !wasDefeated) animator.SetTrigger(HitId);
            wasDefeated = player.IsDefeated;
        }
    }
}
