using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MagicHand
{
    /// <summary>
    /// 準備ルームで、アイテムの見本に近づいた人がいる間だけ効果の説明を画面に出す。
    ///
    /// 見本ごとに名札（<see cref="ProximityLabel"/>）はあるが、名前だけでは
    /// 効果が分からない。8種を常時ぜんぶ表示すると画面が文字だらけになるため、
    /// 「今いちばん近い1つ」だけを画面の決まった位置・決まった大きさで出す。
    ///
    /// 1Pと2Pは別々の場所を見ていることが多いので、2人ぶんを1つにまとめず、
    /// 画面の左右にそれぞれ独立して表示する（配列の0番=1P=左、1番=2P=右）。
    /// </summary>
    public class ItemDescriptionDisplay : MonoBehaviour
    {
        [SerializeField] private GameObject[] panels = new GameObject[2];
        [SerializeField] private Text[] nameTexts = new Text[2];
        [SerializeField] private Text[] descriptionTexts = new Text[2];

        [Tooltip("この距離まで近づいたら表示する（水平距離で判定）")]
        [SerializeField, Min(1f)] private float showDistance = 5f;

        [Tooltip("見本アイテム（ロビーのItemSamples）。シーン生成時に登録される")]
        [SerializeField] private List<ItemPickup> samples = new List<ItemPickup>();

        private void Update()
        {
            GameManager manager = GameManager.Instance;
            bool inLobby = manager != null && manager.CurrentState == GameState.Lobby;

            for (int i = 0; i < panels.Length; i++)
            {
                GameObject panel = panels[i];
                if (panel == null) continue;

                bool enabled = MatchSettings.Instance == null || MatchSettings.Instance.IsItemDescriptionEnabled(i);
                PlayerController player = (inLobby && enabled && i < manager.Players.Count) ? manager.Players[i] : null;
                ItemPickup nearest = player != null ? FindNearest(player) : null;

                if (panel.activeSelf != (nearest != null)) panel.SetActive(nearest != null);
                if (nearest == null) continue;

                Text nameText = i < nameTexts.Length ? nameTexts[i] : null;
                if (nameText != null)
                {
                    nameText.text = nearest.Definition != null ? nearest.Definition.DisplayName : string.Empty;
                    nameText.color = nearest.Definition != null ? nearest.Definition.DisplayColor : Color.white;
                }

                Text descriptionText = i < descriptionTexts.Length ? descriptionTexts[i] : null;
                if (descriptionText != null)
                {
                    descriptionText.text = nearest.Definition != null ? nearest.Definition.Description : string.Empty;
                }
            }
        }

        /// <summary>そのプレイヤーにいちばん近い見本を1つだけ選ぶ。範囲外しかなければ何も出さない。</summary>
        private ItemPickup FindNearest(PlayerController player)
        {
            float bestSqr = showDistance * showDistance;
            ItemPickup best = null;

            foreach (ItemPickup sample in samples)
            {
                if (sample == null) continue;

                Vector3 delta = player.transform.position - sample.transform.position;
                delta.y = 0f;
                float sqr = delta.sqrMagnitude;

                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    best = sample;
                }
            }

            return best;
        }
    }
}
