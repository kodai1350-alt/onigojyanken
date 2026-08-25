using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// プレイヤー同士の接触判定。トリガーコライダーと同じ GameObject に置く。
    ///
    /// 決着は「勝っている側だけ」が呼ぶので、1回の接触が二重に処理されない。
    /// あいこは勝ち負けが無く、どちらからも同じ条件で呼ばれてしまうため、
    /// 番号の小さい側だけが呼ぶという別の決め方をしている。
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerCombat : MonoBehaviour
    {
        private PlayerController owner;

        private void Awake()
        {
            owner = GetComponent<PlayerController>();
        }

        private void OnTriggerEnter(Collider other) => TryResolve(other);

        // 属性がアイテムで書き換わった瞬間など、重なり続けている状態でも成立させる
        private void OnTriggerStay(Collider other) => TryResolve(other);

        private void TryResolve(Collider other)
        {
            GameManager manager = GameManager.Instance;
            if (manager == null || manager.CurrentState != GameState.InGame) return;

            PlayerController opponent = other.GetComponentInParent<PlayerController>();
            if (opponent == null || opponent == owner) return;

            if (owner.IsInvincible || opponent.IsInvincible) return;
            if (owner.IsSelecting || opponent.IsSelecting) return;

            // 勝っている側のみが決着を処理する（負けた側は何もしない）
            if (owner.CurrentHand.Beats(opponent.CurrentHand))
            {
                manager.ResolveContact(owner, opponent);
                return;
            }

            // ここから先はあいこだけ。手が違えば勝っている側の呼び出しに任せる
            if (owner.CurrentHand != opponent.CurrentHand) return;
            if (owner.CurrentHand == HandType.None) return;

            // あいこは勝ち負けが無く、どちらからも同じ条件で呼ばれてしまう。
            // 二重に弾かないよう、番号の小さい側だけが処理する
            if (owner.PlayerIndex > opponent.PlayerIndex) return;

            manager.ResolveDraw(owner, opponent);
        }
    }
}
