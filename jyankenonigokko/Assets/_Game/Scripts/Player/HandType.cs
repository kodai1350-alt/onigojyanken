using UnityEngine;

namespace MagicHand
{
    /// <summary>じゃんけんの属性。None は「未選択」を表す。</summary>
    public enum HandType
    {
        None = 0,
        Gu = 1,
        Choki = 2,
        Pa = 3
    }

    public static class HandTypeUtil
    {
        /// <summary>self が other に勝つなら true。あいこ・負け・None は false。</summary>
        public static bool Beats(this HandType self, HandType other)
        {
            switch (self)
            {
                case HandType.Gu: return other == HandType.Choki;
                case HandType.Choki: return other == HandType.Pa;
                case HandType.Pa: return other == HandType.Gu;
                default: return false;
            }
        }

        /// <summary>self が勝てる手（グー→チョキ）。None なら None。</summary>
        public static HandType WeakerHand(this HandType self)
        {
            switch (self)
            {
                case HandType.Gu: return HandType.Choki;
                case HandType.Choki: return HandType.Pa;
                case HandType.Pa: return HandType.Gu;
                default: return HandType.None;
            }
        }

        /// <summary>プレースホルダー表示用の色。本番アセット導入後は不要になる想定。</summary>
        public static Color ToColor(this HandType self)
        {
            switch (self)
            {
                case HandType.Gu: return new Color(0.90f, 0.25f, 0.25f);
                case HandType.Choki: return new Color(0.25f, 0.80f, 0.35f);
                case HandType.Pa: return new Color(0.25f, 0.50f, 0.95f);
                default: return new Color(0.6f, 0.6f, 0.6f);
            }
        }

        public static string ToLabel(this HandType self)
        {
            switch (self)
            {
                case HandType.Gu: return "グー";
                case HandType.Choki: return "チョキ";
                case HandType.Pa: return "パー";
                default: return "未選択";
            }
        }
    }
}
