using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// ほうき。発動すると一定時間の自由飛行に入る。
    ///
    /// スクロールと同じ1枠を共有するので、ほうきを持っている間は他のスクロールを拾えない。
    /// 手に持つものが常に1つに限られるおかげで、
    /// 相手が杖を持っているのかほうきを持っているのかが見た目だけで判別できる。
    ///
    /// 効果の中身は PlayerFlight が持つ。ここは「発動の合図を送るだけ」に留めてある。
    /// 飛行は3秒間の状態遷移（飛行→滑空→着地→ペナルティ）を伴い、
    /// 発動した瞬間に終わる他のスクロールとは性質が違うため。
    /// </summary>
    [CreateAssetMenu(fileName = "Scroll_Broom", menuName = "MagicHand/Scrolls/Broom")]
    public class BroomEffectSO : ScrollEffectSO
    {
        public override void Apply(PlayerController user)
        {
            if (user == null) return;

            PlayerFlight flight = user.Flight;
            if (flight == null)
            {
                Debug.LogWarning("[MagicHand] PlayerFlight が付いていないので飛行できません");
                return;
            }

            SEPlayer.PlayBroom();
            flight.BeginFlight();
        }
    }
}
