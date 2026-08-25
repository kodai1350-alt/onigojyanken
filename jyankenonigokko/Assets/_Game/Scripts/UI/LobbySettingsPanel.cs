using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MagicHand
{
    /// <summary>
    /// 準備ルームの設定パネル1枚ぶん。上下でカーソル移動、左右で値の増減。
    ///
    /// アイテムのオンオフは毎回いじる設定ではないので常時は畳んでおき、
    /// 「アイテム設定」の行で決定したときだけ展開する。
    /// 常に全種類を並べるとパネルが縦に伸びて、肝心のプレイ画面を圧迫するため。
    ///
    /// 共通設定（制限時間・アイテム）は 1P のパネルにだけ載せる。
    /// 両方に載せると二人が同時に別の値へ変えようとしたときの調停が必要になるため、
    /// 構造として競合が起きないようにしている。
    /// </summary>
    public class LobbySettingsPanel : MonoBehaviour
    {
        private enum RowKind { Sensitivity, InvertLook, Fov, ItemDescription, Duration, EasyMode, Controls, ItemFolder, ItemToggle, Back }

        private struct Row
        {
            public RowKind Kind;
            public int Index;   // ItemToggle のときだけ使う
        }

        [SerializeField] private int playerIndex;
        [Tooltip("制限時間とアイテムのオンオフを載せるか。1Pのパネルだけ true にする")]
        [SerializeField] private bool includeSharedSettings;

        [SerializeField] private Text titleText;
        [SerializeField] private Text[] rowTexts;

        [Tooltip("使っている行だけを覆う背景。畳んでいるときに余白が伸びないよう、行数に合わせて縮める")]
        [SerializeField] private RectTransform background;

        [Tooltip("一番下の行から背景をどれだけ下へ余らせるか")]
        [SerializeField] private float backgroundPadding = 0.02f;

        [Header("Colors")]
        [SerializeField] private Color selectedColor = new Color(1f, 0.92f, 0.45f);
        [SerializeField] private Color normalColor = new Color(0.85f, 0.85f, 0.9f);
        [SerializeField] private Color disabledItemColor = new Color(0.5f, 0.5f, 0.55f);

        private readonly List<Row> rows = new List<Row>();
        private int cursor;
        private bool itemsExpanded;
        private bool built;

        /// <summary>操作説明を出したいか。実際に出すかは LobbyControlsHelp が2人ぶんを見て決める。</summary>
        public bool ShowControls { get; private set; }

        private void Update()
        {
            MatchSettings settings = MatchSettings.Instance;
            if (settings == null || rowTexts == null) return;

            if (!built) Rebuild(settings);
            Refresh(settings);
        }

        /// <summary>今の展開状態に合わせて行を組み直す。</summary>
        private void Rebuild(MatchSettings settings)
        {
            rows.Clear();
            rows.Add(new Row { Kind = RowKind.Sensitivity });
            rows.Add(new Row { Kind = RowKind.InvertLook });
            rows.Add(new Row { Kind = RowKind.Fov });
            rows.Add(new Row { Kind = RowKind.ItemDescription });

            if (includeSharedSettings) rows.Add(new Row { Kind = RowKind.Duration });
            if (includeSharedSettings) rows.Add(new Row { Kind = RowKind.EasyMode });

            // 操作説明はアイテム設定より前に置く。後ろだと展開時にアイテムの列に埋もれる
            rows.Add(new Row { Kind = RowKind.Controls });

            if (includeSharedSettings)
            {
                rows.Add(new Row { Kind = RowKind.ItemFolder });

                if (itemsExpanded)
                {
                    for (int i = 0; i < settings.ItemToggles.Count; i++)
                    {
                        rows.Add(new Row { Kind = RowKind.ItemToggle, Index = i });
                    }

                    rows.Add(new Row { Kind = RowKind.Back });
                }
            }

            cursor = Mathf.Clamp(cursor, 0, Mathf.Max(0, rows.Count - 1));
            if (titleText != null) titleText.text = $"{playerIndex + 1}P 設定";
            ResizeBackground();
            built = true;
        }

        /// <summary>
        /// 背景を、実際に使っている行の下端までに縮める。
        ///
        /// 行の枠はアイテムを開いた最大の数に合わせて確保してあるので、
        /// 畳んでいるときは下が丸ごと空く。背景を出しっぱなしにすると
        /// 中身の無い灰色の板が縦に伸びて、後ろのプレイ画面を無駄に隠してしまう。
        /// </summary>
        private void ResizeBackground()
        {
            if (background == null || rowTexts == null || rows.Count == 0) return;

            int last = Mathf.Min(rows.Count, rowTexts.Length) - 1;
            if (last < 0 || rowTexts[last] == null) return;

            // 行の位置は親（このパネル）に対する割合で置かれているので、そのまま下端に使える
            float bottom = rowTexts[last].rectTransform.anchorMin.y - backgroundPadding;

            Vector2 min = background.anchorMin;
            min.y = Mathf.Clamp01(bottom);
            background.anchorMin = min;
            background.offsetMin = Vector2.zero;
            background.offsetMax = Vector2.zero;
        }

        /// <summary>LobbyMenuController から呼ばれる。上下でカーソル、左右で値、決定は左右と兼用。</summary>
        public void Navigate(int vertical, int horizontal)
        {
            MatchSettings settings = MatchSettings.Instance;
            if (settings == null || rows.Count == 0) return;

            if (vertical != 0)
            {
                // 画面の上が先頭なので、上入力でインデックスを減らす
                cursor = (cursor - vertical + rows.Count) % rows.Count;
                return;
            }

            if (horizontal == 0) return;

            Row row = rows[cursor];
            switch (row.Kind)
            {
                case RowKind.Sensitivity:
                    settings.AdjustSensitivity(playerIndex, horizontal);
                    break;

                case RowKind.InvertLook:
                    settings.ToggleInvertLook(playerIndex);
                    break;

                case RowKind.Fov:
                    settings.AdjustFieldOfView(playerIndex, horizontal);
                    break;

                case RowKind.ItemDescription:
                    settings.ToggleItemDescription(playerIndex);
                    break;

                case RowKind.Duration:
                    settings.AdjustDuration(horizontal);
                    break;

                case RowKind.EasyMode:
                    settings.ToggleEasyMode();
                    break;

                case RowKind.Controls:
                    ShowControls = !ShowControls;
                    break;

                case RowKind.ItemFolder:
                    SetItemsExpanded(true, settings);
                    break;

                case RowKind.ItemToggle:
                    settings.ToggleItem(row.Index);
                    break;

                case RowKind.Back:
                    SetItemsExpanded(false, settings);
                    break;
            }
        }

        /// <summary>
        /// LobbyMenuController から呼ばれる。B（Xbox）／○（PS）でも、左右の決定と同じ効果を出す。
        ///
        /// 感度・視野角・制限時間は左右で結果が変わる値調整の行なので、決定ボタンを押しても
        /// 何もしない（どちらに動かすかが決まらないため）。トグル/展開系の行だけ反応する。
        /// </summary>
        public void Confirm()
        {
            if (rows.Count == 0) return;

            switch (rows[cursor].Kind)
            {
                case RowKind.Sensitivity:
                case RowKind.Fov:
                case RowKind.Duration:
                    return;
            }

            Navigate(0, 1);
        }

        private void SetItemsExpanded(bool expanded, MatchSettings settings)
        {
            if (itemsExpanded == expanded) return;

            itemsExpanded = expanded;
            built = false;
            Rebuild(settings);

            // 開いたら先頭のアイテム、閉じたら「アイテム設定」の行へカーソルを戻す
            // （ItemDescriptionの行が1つ増えたぶん、以前より+1した位置になる）
            cursor = expanded ? Mathf.Min(7, rows.Count - 1) : 6;
        }

        private void Refresh(MatchSettings settings)
        {
            for (int i = 0; i < rowTexts.Length; i++)
            {
                Text text = rowTexts[i];
                if (text == null) continue;

                bool used = i < rows.Count;
                if (text.gameObject.activeSelf != used) text.gameObject.SetActive(used);
                if (!used) continue;

                bool selected = i == cursor;
                text.text = (selected ? "▶ " : "   ") + DescribeRow(settings, rows[i]);
                text.color = selected ? selectedColor : RowColor(settings, rows[i]);
            }
        }

        private string DescribeRow(MatchSettings settings, Row row)
        {
            switch (row.Kind)
            {
                case RowKind.Sensitivity:
                    return $"視点感度   ◀ {settings.GetSensitivity(playerIndex):0.0} ▶";

                case RowKind.InvertLook:
                    return $"上下反転   {(settings.IsLookInverted(playerIndex) ? "ON" : "OFF")}";

                case RowKind.Fov:
                    return $"視野角   ◀ {settings.GetFieldOfView(playerIndex):0} ▶";

                case RowKind.ItemDescription:
                    return $"アイテム説明   {(settings.IsItemDescriptionEnabled(playerIndex) ? "ON" : "OFF")}";

                case RowKind.Duration:
                    return $"制限時間   ◀ {settings.DurationText()} ▶";

                case RowKind.EasyMode:
                    return $"イージーモード   {(settings.EasyMode ? "ON" : "OFF")}";

                case RowKind.Controls:
                    return $"操作説明   {(ShowControls ? "閉じる ▶" : "開く ▶")}";

                case RowKind.ItemFolder:
                    return $"アイテム設定   {settings.EnabledItemCount()}/{settings.ItemToggles.Count} ▶";

                case RowKind.ItemToggle:
                {
                    MatchSettings.ItemToggle toggle = settings.ItemToggles[row.Index];
                    string name = toggle.item != null ? toggle.item.DisplayName : "???";
                    return $"　{name}   {(toggle.enabled ? "ON" : "OFF")}";
                }

                case RowKind.Back:
                    return "◀ 戻る";
            }

            return string.Empty;
        }

        private Color RowColor(MatchSettings settings, Row row)
        {
            if (row.Kind != RowKind.ItemToggle) return normalColor;
            return settings.ItemToggles[row.Index].enabled ? normalColor : disabledItemColor;
        }
    }
}
