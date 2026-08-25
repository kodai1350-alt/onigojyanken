using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// シーンを切り替える瞬間に画面全体を覆う黒い板。
    ///
    /// SceneManager.LoadScene はその場でシーンを差し替えず、
    /// 呼んだフレームの Update と描画は最後まで走ってから切り替わる。
    /// そのため勝負がついた直後の試合画面が1フレームだけ映り込む。
    /// 覆いを先に出しておけば、映るのは黒一色になる。
    ///
    /// 画面の一番上に来る必要があるので、UIの並びでは最後の子に置くこと。
    /// </summary>
    public class SceneCover : MonoBehaviour
    {
        [SerializeField] private GameObject cover;

        /// <summary>シーンのどこからでも呼べるよう、生成済みの1枚を持ち回す。</summary>
        public static SceneCover Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            if (cover != null) cover.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>画面を覆う。以降このシーンが消えるまで開けない。</summary>
        public void Show()
        {
            if (cover == null) return;

            // 覆いより後に生成されたUIが上に来ていることがあるので、出すたびに最前面へ回す
            cover.transform.SetAsLastSibling();
            cover.SetActive(true);
        }

        /// <summary>覆いが用意されていなくても呼び出し側が困らないようにした入口。</summary>
        public static void Cover()
        {
            if (Instance != null) Instance.Show();
        }
    }
}
