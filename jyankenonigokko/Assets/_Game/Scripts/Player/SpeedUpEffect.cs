using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// スピードUp中、自分の周りに小さな上矢印をいくつも駆け上がらせて疾走感を出す。
    ///
    /// 汎用の StatusAura（足元の輪）はスピードUpを候補から外してあり、
    /// スピードUpだけはこちらの専用演出に置き換わる。
    ///
    /// 矢印は `LineAlignment.View` の LineRenderer（ビルダー側で設定済み）なので、
    /// どちらのカメラから見ても常にこちらを向く。幅を 0→最大→0 と正弦波で増減させて、
    /// 現れて駆け上がって消える動きを、透明度を使わずに表現している
    /// （共有マテリアルを Opaque のまま使い続けられる）。
    /// </summary>
    public class SpeedUpEffect : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private LineRenderer[] arrows;

        [Header("Layout")]
        [SerializeField] private float ringRadius = 0.5f;

        [Header("Motion")]
        [SerializeField, Min(0.1f)] private float cycleDuration = 0.9f;
        [SerializeField] private float minY = 0.05f;
        [SerializeField] private float maxY = 1.6f;
        [SerializeField] private float maxWidth = 0.05f;

        private MaterialPropertyBlock block;
        private float[] progress;
        private bool visible;

        private void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            block = new MaterialPropertyBlock();

            progress = new float[arrows.Length];

            for (int i = 0; i < arrows.Length; i++)
            {
                // 開始位相をずらして、全部が同時に駆け上がらないようにする
                progress[i] = (float)i / arrows.Length;

                float angle = i * Mathf.PI * 2f / arrows.Length;
                Vector3 pos = arrows[i].transform.localPosition;
                pos.x = Mathf.Cos(angle) * ringRadius;
                pos.z = Mathf.Sin(angle) * ringRadius;
                arrows[i].transform.localPosition = pos;

                arrows[i].enabled = false;
            }
        }

        private void Update()
        {
            PlayerStatusEffects status = player != null ? player.Status : null;
            Color color = default;
            bool shouldShow = status != null && status.TryGetColor(PlayerStatusEffects.SpeedUp, out color);

            if (shouldShow != visible)
            {
                visible = shouldShow;
                for (int i = 0; i < arrows.Length; i++) arrows[i].enabled = visible;
            }

            if (!visible) return;

            for (int i = 0; i < arrows.Length; i++)
            {
                progress[i] += Time.deltaTime / cycleDuration;
                if (progress[i] > 1f) progress[i] -= 1f;

                Vector3 pos = arrows[i].transform.localPosition;
                pos.y = Mathf.Lerp(minY, maxY, progress[i]);
                arrows[i].transform.localPosition = pos;

                // sin(0)=0, sin(π)=0 なので、駆け上がりの最初と最後で自然に幅0(=消えて見える)になる
                float width = maxWidth * Mathf.Sin(progress[i] * Mathf.PI);
                arrows[i].widthMultiplier = width;

                arrows[i].GetPropertyBlock(block);
                block.SetColor("_BaseColor", color);
                block.SetColor("_Color", color);
                arrows[i].SetPropertyBlock(block);
            }
        }
    }
}
