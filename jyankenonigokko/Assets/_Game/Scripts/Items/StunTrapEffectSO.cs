using UnityEngine;

namespace MagicHand
{
    /// <summary>相手妨害：一定範囲内の相手プレイヤーを一定時間スタンさせる。</summary>
    [CreateAssetMenu(fileName = "Scroll_StunTrap", menuName = "MagicHand/Scrolls/Stun Trap")]
    public class StunTrapEffectSO : AreaScrollEffectSO
    {
        [Header("Stun Trap")]
        [SerializeField, Min(0.1f)] private float stunDuration = 1.5f;

        protected override void ApplyToTarget(PlayerController user, PlayerController target)
        {
            target.Stun(stunDuration);

            if (target.Status != null)
            {
                target.Status.Apply(PlayerStatusEffects.Stun, "スタン", stunDuration, DisplayColor);
            }
        }

        protected override void PlayActivationSound() => SEPlayer.PlayStun();
    }
}
