using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// 手変更アイテム。取得すると現在の属性が即座に上書きされる。
    /// </summary>
    [CreateAssetMenu(fileName = "HandItem", menuName = "MagicHand/Items/Hand Item")]
    public class HandItemSO : ItemDefinitionSO
    {
        [Header("Hand")]
        [SerializeField] private HandType hand = HandType.Gu;

        public HandType Hand => hand;

        public override bool TryPickup(PlayerController player)
        {
            // 属性選択中のプレイヤーは拾えない
            if (player == null || player.IsSelecting || player.IsDefeated) return false;

            player.SetHand(hand);
            return true;
        }
    }
}
