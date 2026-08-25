using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// ステージ上に湧く全アイテムの基底データ。
    ///
    /// 【拡張のしかた】
    /// 新しいアイテムを追加したいときは、このクラス（またはScrollEffectSO）を継承した
    /// ScriptableObject を1つ作り、TryPickup()／Apply() を実装してアセットを作成し、
    /// ItemSpawnManager の抽選テーブルに登録するだけでよい。
    /// PlayerController / GameManager / ItemPickup は一切変更する必要がない。
    /// </summary>
    public abstract class ItemDefinitionSO : ScriptableObject
    {
        [Header("Presentation")]
        [SerializeField] private string displayName = "Item";
        [SerializeField] private Color displayColor = Color.white;
        [SerializeField, TextArea] private string description;

        [Tooltip("HUDのアイテム枠に出す絵。無ければ色だけの四角で代替される")]
        [SerializeField] private Sprite icon;

        public string DisplayName => displayName;
        public Color DisplayColor => displayColor;
        public string Description => description;
        public Sprite Icon => icon;

        /// <summary>
        /// プレイヤーが接触したときに呼ばれる。
        /// 取得が成立した場合のみ true を返す（false ならアイテムは場に残る）。
        /// </summary>
        public abstract bool TryPickup(PlayerController player);
    }
}
