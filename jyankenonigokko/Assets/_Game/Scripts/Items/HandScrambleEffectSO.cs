using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// 範囲内の相手の手を、使用者が勝てる手へ強制的に書き換える妨害スクロール。
    /// 撃ってから触りに行けばそのまま得点できるため、位置取りと合わせて使う攻めの札。
    ///
    /// 使用者の手が未選択（リスポーン直後など）のときは基準が無いので何もしない。
    ///
    /// 範囲内判定・無敵チェックは AreaScrollEffectSO に共通化済み。
    /// </summary>
    [CreateAssetMenu(fileName = "Scroll_HandScramble", menuName = "MagicHand/Scrolls/Hand Charm")]
    public class HandScrambleEffectSO : AreaScrollEffectSO
    {
        protected override void ApplyToTarget(PlayerController user, PlayerController target)
        {
            HandType mine = user.CurrentHand;
            if (mine == HandType.None) return;

            target.SetHand(mine.WeakerHand());
        }

        protected override void PlayActivationSound() => SEPlayer.PlayCharm();
    }
}
