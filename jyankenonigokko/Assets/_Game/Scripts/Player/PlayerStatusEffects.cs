using System.Collections.Generic;
using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// そのプレイヤーに今かかっている「時間制限のある効果」をまとめて持つ。
    ///
    /// HUD から PlayerController のタイマーを直接覗く作りにはしていない。
    /// タイマーは private なうえ、サーチのように残り時間がマーカー側にあるものもあり、
    /// 効果が増えるたびに HUD を直す羽目になるため。
    /// 効果を足す側がここへ登録し、HUD は登録されたものを並べるだけにしてある。
    ///
    /// 妨害（スタン・減速）は**かけられた側**に登録する。
    /// 表示は「自分が今どうなっているか」を伝えるためのもので、
    /// 相手に何をしたかは自分の画面には要らない。
    /// </summary>
    public class PlayerStatusEffects : MonoBehaviour
    {
        /// <summary>効果の識別子。同じものを掛け直したときに二重に並ばないよう、これで上書きする。</summary>
        public const string SpeedUp = "speed_up";
        public const string Stun = "stun";
        public const string RevealEnemy = "reveal_enemy";
        public const string Searched = "searched";
        public const string LandingSlow = "landing_slow";
        public const string Exposed = "exposed";

        /// <summary>あいこでぶつかったとき、点で負けている側だけが受け取る追い上げの加速。</summary>
        public const string Comeback = "comeback";

        /// <summary>リスポーンして手を決めた直後の無敵。</summary>
        public const string Invincible = "invincible";

        private class Active
        {
            public string Id;
            public string Label;
            public float Remaining;
            public Color Color;
        }

        private readonly List<Active> effects = new List<Active>();

        /// <summary>今かかっている効果の数。</summary>
        public int Count => effects.Count;

        /// <summary>残り時間の長い順に並べた i 番目の表示名。</summary>
        public string GetLabel(int index) => effects[index].Label;

        /// <summary>残り時間の長い順に並べた i 番目の残り秒数。</summary>
        public float GetRemaining(int index) => effects[index].Remaining;

        /// <summary>
        /// 残り時間の長い順に並べた i 番目の色。足元に出す発動中エフェクト（StatusAura）が使う。
        /// 一覧の先頭＝一番長く残っている効果の色を優先して見せる。
        /// </summary>
        public Color GetColor(int index) => effects[index].Color;

        /// <summary>指定の効果がかかっているか。画面外の矢印など、効果に連動する演出の判定に使う。</summary>
        public bool IsActive(string id)
        {
            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i].Id == id) return true;
            }

            return false;
        }

        /// <summary>指定の効果の色を取り出す。専用の演出（スピードUpの矢印など）が自分の色を知るために使う。</summary>
        public bool TryGetColor(string id, out Color color)
        {
            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i].Id != id) continue;
                color = effects[i].Color;
                return true;
            }

            color = default;
            return false;
        }

        /// <summary>
        /// 残り時間が最長の効果の色を返す。ただし excludedIds に含まれる識別子は候補から外す。
        /// スピードUp・スタンは専用演出（矢印・稲妻）を持つので、足元の輪（StatusAura）は
        /// それらを除いて選ぶ。
        /// </summary>
        public bool TryGetPrimaryColor(out Color color, params string[] excludedIds)
        {
            for (int i = 0; i < effects.Count; i++)
            {
                bool excluded = false;
                for (int j = 0; j < excludedIds.Length; j++)
                {
                    if (effects[i].Id != excludedIds[j]) continue;
                    excluded = true;
                    break;
                }

                if (excluded) continue;

                color = effects[i].Color;
                return true;
            }

            color = default;
            return false;
        }

        /// <summary>
        /// 効果を登録する。同じ識別子が既にあれば、残りが長い方を採用して上書きする。
        /// 短い効果を重ねて掛けられたときに、先にかかっていた長い効果が消えないようにするため。
        /// </summary>
        public void Apply(string id, string label, float duration, Color color)
        {
            if (duration <= 0f) return;

            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i].Id != id) continue;

                effects[i].Label = label;
                effects[i].Remaining = Mathf.Max(effects[i].Remaining, duration);
                effects[i].Color = color;
                Sort();
                return;
            }

            effects.Add(new Active { Id = id, Label = label, Remaining = duration, Color = color });
            Sort();
        }

        /// <summary>試合の初期化などで全部消す。</summary>
        public void Clear() => effects.Clear();

        private void Update()
        {
            if (effects.Count == 0) return;

            bool expired = false;

            for (int i = 0; i < effects.Count; i++)
            {
                effects[i].Remaining -= Time.deltaTime;
                if (effects[i].Remaining <= 0f) expired = true;
            }

            if (!expired) return;

            for (int i = effects.Count - 1; i >= 0; i--)
            {
                if (effects[i].Remaining <= 0f) effects.RemoveAt(i);
            }
        }

        /// <summary>残りの長い順。並べ替えるのは中身が変わったときだけで足りる。</summary>
        private void Sort() => effects.Sort((a, b) => b.Remaining.CompareTo(a.Remaining));
    }
}
