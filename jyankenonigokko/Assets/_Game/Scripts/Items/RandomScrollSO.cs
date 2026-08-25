using System.Collections.Generic;
using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// 地面に落ちている「中身が決まっていない巻物」。
    ///
    /// 以前は湧いた瞬間に5種類のうちどれかへ確定していた。これだと同じ巻物を眺めている間、
    /// 中身は既に決まっているのに見た目上は分からないだけ、という状態になる。
    /// 拾った瞬間に候補から抽選することで、実際に「拾うまで何も決まっていない」にした。
    ///
    /// 準備ルームで無効化された種類は候補から除外する。全種類無効化されていた場合は
    /// 取得を成立させない（場に残る）。
    /// </summary>
    [CreateAssetMenu(menuName = "MagicHand/Items/Random Scroll")]
    public class RandomScrollSO : ItemDefinitionSO
    {
        [Tooltip("拾ったときの抽選候補")]
        [SerializeField] private List<ScrollEffectSO> candidates = new List<ScrollEffectSO>();

        public IReadOnlyList<ScrollEffectSO> Candidates => candidates;

        public override bool TryPickup(PlayerController player)
        {
            if (player == null || player.IsSelecting || player.IsDefeated) return false;

            ScrollEffectSO chosen = PickEnabledCandidate();
            if (chosen == null) return false;

            return player.Scrolls.TryStock(chosen);
        }

        private ScrollEffectSO PickEnabledCandidate()
        {
            List<ScrollEffectSO> enabled = new List<ScrollEffectSO>();

            foreach (ScrollEffectSO candidate in candidates)
            {
                if (candidate == null) continue;
                if (MatchSettings.Instance != null && !MatchSettings.Instance.IsItemEnabled(candidate)) continue;
                enabled.Add(candidate);
            }

            if (enabled.Count == 0) return null;
            return enabled[Random.Range(0, enabled.Count)];
        }
    }
}
