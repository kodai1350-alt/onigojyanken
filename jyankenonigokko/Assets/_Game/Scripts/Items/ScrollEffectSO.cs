using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// スクロール（バフ・デバフ）の基底。Strategy パターンの「戦略」にあたる。
    ///
    /// 取得しても即発動せず ScrollStock に1つだけ保持され、
    /// プレイヤーが任意のタイミングで発動ボタンを押したときに Apply() が呼ばれる。
    ///
    /// 新しい効果を足すときはこのクラスを継承して Apply() を実装するだけ。
    /// </summary>
    public abstract class ScrollEffectSO : ItemDefinitionSO
    {
        public sealed override bool TryPickup(PlayerController player)
        {
            if (player == null || player.IsSelecting || player.IsDefeated) return false;

            // ストックが埋まっていれば取得できない（アイテムは場に残る）
            return player.Scrolls.TryStock(this);
        }

        /// <summary>発動ボタンが押されたときに実行される効果本体。</summary>
        public abstract void Apply(PlayerController user);
    }
}
