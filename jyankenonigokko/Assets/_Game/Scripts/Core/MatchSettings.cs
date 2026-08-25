using System;
using System.Collections.Generic;
using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// 準備ルームで調整し、試合に持ち込む設定値の置き場。
    /// 「誰が変えたか」ではなく「今どうなっているか」だけを持つ単純な入れ物にしてあり、
    /// 反映側（カメラ・タイマー・アイテム抽選）がここを参照しに来る。
    /// </summary>
    public class MatchSettings : MonoBehaviour
    {
        public static MatchSettings Instance { get; private set; }

        /// <summary>アイテム1種類ぶんの有効・無効。</summary>
        [Serializable]
        public class ItemToggle
        {
            public ItemDefinitionSO item;
            public bool enabled = true;
        }

        [Header("Look (per player)")]
        [SerializeField] private float[] lookSensitivity = { 1f, 1f };
        [SerializeField] private bool[] invertLook = { false, false };
        [SerializeField] private float sensitivityStep = 0.1f;
        [SerializeField] private float sensitivityMin = 0.4f;
        [SerializeField] private float sensitivityMax = 2.5f;

        [Header("Field of View (per player)")]
        [SerializeField] private float[] fieldOfView = { 60f, 60f };
        [SerializeField] private float fovStep = 5f;
        [SerializeField] private float fovMin = 60f;
        [SerializeField] private float fovMax = 80f;

        [Header("Match Duration (shared)")]
        [SerializeField] private float matchDuration = 300f;
        [SerializeField] private float durationStep = 30f;
        [SerializeField] private float durationMin = 60f;
        [SerializeField] private float durationMax = 600f;

        [Header("Item On/Off (shared)")]
        [SerializeField] private List<ItemToggle> itemToggles = new List<ItemToggle>();

        [Header("Easy Mode (shared)")]
        [Tooltip("縮小マップ・アイテム減少で遊べる練習向けモード")]
        [SerializeField] private bool easyMode;

        [Header("Item Description (per player)")]
        [Tooltip("準備ルームで見本アイテムに近づいたときの説明表示。個々に切れるよう1P/2Pで別に持つ")]
        [SerializeField] private bool[] showItemDescription = { true, true };

        public float MatchDuration => matchDuration;
        public IReadOnlyList<ItemToggle> ItemToggles => itemToggles;
        public bool EasyMode => easyMode;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---- 視点感度 -------------------------------------------------------

        public float GetSensitivity(int playerIndex)
        {
            if (lookSensitivity == null || playerIndex < 0 || playerIndex >= lookSensitivity.Length) return 1f;
            return lookSensitivity[playerIndex];
        }

        public void AdjustSensitivity(int playerIndex, int direction)
        {
            if (lookSensitivity == null || playerIndex < 0 || playerIndex >= lookSensitivity.Length) return;

            float next = lookSensitivity[playerIndex] + direction * sensitivityStep;
            lookSensitivity[playerIndex] = Mathf.Clamp(Mathf.Round(next * 10f) / 10f, sensitivityMin, sensitivityMax);
        }

        public bool IsLookInverted(int playerIndex)
        {
            if (invertLook == null || playerIndex < 0 || playerIndex >= invertLook.Length) return false;
            return invertLook[playerIndex];
        }

        public void ToggleInvertLook(int playerIndex)
        {
            if (invertLook == null || playerIndex < 0 || playerIndex >= invertLook.Length) return;
            invertLook[playerIndex] = !invertLook[playerIndex];
        }

        // ---- 視野角 ---------------------------------------------------------

        public float GetFieldOfView(int playerIndex)
        {
            if (fieldOfView == null || playerIndex < 0 || playerIndex >= fieldOfView.Length) return fovMin;
            return fieldOfView[playerIndex];
        }

        public void AdjustFieldOfView(int playerIndex, int direction)
        {
            if (fieldOfView == null || playerIndex < 0 || playerIndex >= fieldOfView.Length) return;

            float next = fieldOfView[playerIndex] + direction * fovStep;
            fieldOfView[playerIndex] = Mathf.Clamp(next, fovMin, fovMax);
        }

        // ---- 制限時間 -------------------------------------------------------

        public void AdjustDuration(int direction)
        {
            matchDuration = Mathf.Clamp(matchDuration + direction * durationStep, durationMin, durationMax);
        }

        public string DurationText()
        {
            int total = Mathf.RoundToInt(matchDuration);
            return $"{total / 60}:{total % 60:00}";
        }

        // ---- イージーモード ---------------------------------------------------

        public void ToggleEasyMode()
        {
            easyMode = !easyMode;
        }

        // ---- アイテムの説明表示 -----------------------------------------------

        public bool IsItemDescriptionEnabled(int playerIndex)
        {
            if (showItemDescription == null || playerIndex < 0 || playerIndex >= showItemDescription.Length) return true;
            return showItemDescription[playerIndex];
        }

        public void ToggleItemDescription(int playerIndex)
        {
            if (showItemDescription == null || playerIndex < 0 || playerIndex >= showItemDescription.Length) return;
            showItemDescription[playerIndex] = !showItemDescription[playerIndex];
        }

        // ---- アイテムのオン・オフ -------------------------------------------

        /// <summary>抽選テーブルに載っている種類を、まだ登録されていなければ取り込む。</summary>
        public void RegisterItems(IEnumerable<ItemDefinitionSO> items)
        {
            foreach (ItemDefinitionSO item in items)
            {
                if (item == null || FindToggle(item) != null) continue;
                itemToggles.Add(new ItemToggle { item = item, enabled = true });
            }
        }

        public bool IsItemEnabled(ItemDefinitionSO item)
        {
            ItemToggle toggle = FindToggle(item);
            return toggle == null || toggle.enabled;
        }

        public void ToggleItem(int index)
        {
            if (index < 0 || index >= itemToggles.Count) return;
            itemToggles[index].enabled = !itemToggles[index].enabled;
        }

        /// <summary>畳んだ状態でも何個有効かが分かるように、件数だけ返す。</summary>
        public int EnabledItemCount()
        {
            int count = 0;
            foreach (ItemToggle toggle in itemToggles)
            {
                if (toggle.enabled) count++;
            }

            return count;
        }

        private ItemToggle FindToggle(ItemDefinitionSO item)
        {
            foreach (ItemToggle toggle in itemToggles)
            {
                if (toggle.item == item) return toggle;
            }

            return null;
        }
    }
}
