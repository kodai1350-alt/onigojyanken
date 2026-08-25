using UnityEngine;

namespace MagicHand
{
    /// <summary>脱出用：自身を前方へ瞬間移動させる。障害物があれば手前で止まる。</summary>
    [CreateAssetMenu(fileName = "Scroll_Teleport", menuName = "MagicHand/Scrolls/Teleport")]
    public class TeleportEffectSO : ScrollEffectSO
    {
        [Header("Teleport")]
        [SerializeField, Min(1f)] private float distance = 14f;
        [SerializeField] private LayerMask blockMask = ~0;

        /// <summary>着地点の下見に使う。表示側が同じ条件で計算できるよう公開している。</summary>
        public float Distance => distance;

        public LayerMask BlockMask => blockMask;

        public override void Apply(PlayerController user)
        {
            if (user == null) return;
            SEPlayer.PlayBlink();
            user.TeleportForward(distance, blockMask);
        }
    }
}
