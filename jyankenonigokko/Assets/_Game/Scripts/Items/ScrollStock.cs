using System;
using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// スクロールのストック枠。仕様上、常に1つだけ保持できる。
    /// </summary>
    public class ScrollStock : MonoBehaviour
    {
        [Tooltip("発動した瞬間に足元へ出す演出。全種類共通で、色だけアイテムに合わせる")]
        [SerializeField] private CastEffect castEffectPrefab;

        public ScrollEffectSO Current { get; private set; }
        public bool HasStock => Current != null;

        /// <summary>ストック内容が変わったとき（取得・使用・クリア）に通知する。</summary>
        public event Action<ScrollEffectSO> StockChanged;

        /// <summary>空いていれば格納する。埋まっていれば false（＝取得させない）。</summary>
        public bool TryStock(ScrollEffectSO scroll)
        {
            if (scroll == null || HasStock) return false;

            Current = scroll;
            StockChanged?.Invoke(Current);
            return true;
        }

        /// <summary>ストックを消費して効果を発動する。</summary>
        public bool TryUse(PlayerController user)
        {
            if (!HasStock) return false;

            ScrollEffectSO scroll = Current;
            Current = null;
            StockChanged?.Invoke(null);

            if (user != null) CastEffect.Spawn(castEffectPrefab, user.transform.position, scroll.DisplayColor);
            scroll.Apply(user);
            return true;
        }

        /// <summary>
        /// デバッグ用。通常の1個ストック制限を無視し、埋まっていても即座に上書きする。
        /// </summary>
        public void ForceStock(ScrollEffectSO scroll)
        {
            if (scroll == null) return;

            Current = scroll;
            StockChanged?.Invoke(Current);
        }

        public void Clear()
        {
            if (!HasStock) return;

            Current = null;
            StockChanged?.Invoke(null);
        }
    }
}
