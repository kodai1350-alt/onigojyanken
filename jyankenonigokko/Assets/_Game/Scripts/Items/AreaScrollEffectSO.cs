using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// 「自分を中心とした範囲内にいる相手全員に何かする」系スクロールの共通処理。
    /// StunTrap・HandScramble はこれを継承し、ApplyToTarget だけを書けばよい。
    ///
    /// 選択中／敗北処理中（無敵）の相手は対象から除外する。個々の効果メソッド
    /// （Stun/SetHand/ApplySpeedMultiplier 等）に無敵判定が実装されているとは限らないため、
    /// ここで一括して弾いておく。
    /// </summary>
    public abstract class AreaScrollEffectSO : ScrollEffectSO
    {
        [Header("Area")]
        [SerializeField, Min(1f)] protected float radius = 10f;

        /// <summary>効果範囲。プレイヤーの足元に範囲円を描くのに使う。</summary>
        public float Radius => radius;

        public sealed override void Apply(PlayerController user)
        {
            GameManager manager = GameManager.Instance;
            if (user == null || manager == null) return;

            PlayActivationSound();

            float sqrRadius = radius * radius;

            foreach (PlayerController target in manager.Players)
            {
                if (target == null || target == user) continue;
                if (target.IsSelecting || target.IsDefeated) continue;

                Vector3 delta = target.transform.position - user.transform.position;
                if (delta.sqrMagnitude > sqrRadius) continue;

                ApplyToTarget(user, target);
            }
        }

        /// <summary>
        /// 範囲内に入った相手1人に対する効果本体。
        /// 使用者の手を見て効果を決める種類（相手の手を自分に有利な手へ変える等）があるため、
        /// 対象だけでなく使用者も渡している。
        /// </summary>
        protected abstract void ApplyToTarget(PlayerController user, PlayerController target);

        /// <summary>
        /// 発動音。範囲内に対象がいるかどうかに関わらず、使った瞬間に鳴らす
        /// （当たり音ではなく発動音なので、空振りでも鳴らす）。
        /// </summary>
        protected virtual void PlayActivationSound() { }
    }
}
