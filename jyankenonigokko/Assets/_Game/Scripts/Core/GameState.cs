namespace MagicHand
{
    /// <summary>
    /// ゲーム全体の進行ステート。GameManager が単一シーン内で切り替える。
    /// </summary>
    public enum GameState
    {
        Title,

        /// <summary>操作に慣れ、設定を詰めるための準備ルーム。試合前に必ず通る。</summary>
        Lobby,

        Selection,
        InGame,

        /// <summary>
        /// 時間切れで同点だったときの分岐。サドンデスに進むか結果発表へ行くかを選ぶ。
        /// InGame と Result の間に挟むので、どちらの後始末も済ませずに待たせる形になる。
        /// </summary>
        TieBreak,

        /// <summary>
        /// 試合が終わった瞬間からResultへ移るまでの一瞬の間。「FINISH」を出すだけの短い待ち時間。
        /// 通常の時間切れ・サドンデスの決着・同点分岐での「結果発表」選択、すべてここを必ず通る。
        /// </summary>
        Finish,

        Result
    }
}
