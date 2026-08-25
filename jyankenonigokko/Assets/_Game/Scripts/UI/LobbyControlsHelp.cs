using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// 準備ルームの操作説明。設定パネルの「操作説明」の行で開閉する。
    ///
    /// 以前はタイトル画面に敷き詰めていたが、1枚絵に差し替えたことで絵を隠すようになり、
    /// そもそも試合直前に読み返せないのが不便だった。準備ルームなら好きなときに開ける。
    ///
    /// 表示は1つしかないので、どちらかが開いていれば出す。
    /// 読むだけのものなので、二人が同時に開こうとしても取り合いにならない。
    /// </summary>
    public class LobbyControlsHelp : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private LobbySettingsPanel[] settingsPanels;

        private void Update()
        {
            if (panel == null) return;

            bool show = ShouldShow();
            if (panel.activeSelf != show) panel.SetActive(show);
        }

        private bool ShouldShow()
        {
            GameManager manager = GameManager.Instance;
            if (manager == null || manager.CurrentState != GameState.Lobby) return false;

            if (settingsPanels == null) return false;

            foreach (LobbySettingsPanel settings in settingsPanels)
            {
                if (settings != null && settings.ShowControls) return true;
            }

            return false;
        }
    }
}
