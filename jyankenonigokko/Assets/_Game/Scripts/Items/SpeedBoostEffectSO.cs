using UnityEngine;

namespace MagicHand
{
    /// <summary>自己強化：一定時間、移動速度を上昇させる。</summary>
    [CreateAssetMenu(fileName = "Scroll_SpeedBoost", menuName = "MagicHand/Scrolls/Speed Boost")]
    public class SpeedBoostEffectSO : ScrollEffectSO
    {
        [Header("Speed Boost")]
        [SerializeField, Min(1f)] private float speedMultiplier = 1.6f;
        [SerializeField, Min(0.1f)] private float duration = 4f;

        public override void Apply(PlayerController user)
        {
            if (user == null) return;

            SEPlayer.PlaySpeedUp();
            user.ApplySpeedMultiplier(speedMultiplier, duration);

            if (user.Status != null)
            {
                user.Status.Apply(PlayerStatusEffects.SpeedUp, "スピードUP", duration, DisplayColor);
            }
        }
    }
}
