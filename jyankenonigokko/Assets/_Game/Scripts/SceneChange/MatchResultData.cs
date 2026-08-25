namespace MagicHand
{
    public static class MatchResultData
    {
        public static int Player1Score { get; set; }
        public static int Player2Score { get; set; }

        public static int WinnerIndex
        {
            get
            {
                if (Player1Score > Player2Score) return 0;
                if (Player2Score > Player1Score) return 1;
                return -1;
            }
        }

        /// <summary>
        /// リセット処理（次のゲームを始める前に呼び出す）
        /// </summary>
        public static void Reset()
        {
            Player1Score = 0;
            Player2Score = 0;
        }
    }
}