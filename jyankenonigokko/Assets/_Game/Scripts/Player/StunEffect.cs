using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// スタン中、体の周りに稲妻がビリビリと不規則に明滅する演出。
    ///
    /// SpeedUpEffect の矢印がなめらかに規則正しく動くのに対し、こちらは
    /// 「感電」を表すため**わざと不規則**にしてある。明滅の間隔・出るかどうか・
    /// ジグザグの形を毎回すべて乱数で作り直し、駆け上がるような滑らかさを避けている。
    /// </summary>
    public class StunEffect : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private LineRenderer[] bolts;

        [Header("Layout")]
        [SerializeField] private float radius = 0.4f;
        [SerializeField] private float minHeight = 0.3f;
        [SerializeField] private float maxHeight = 1.7f;
        [SerializeField] private float boltLength = 0.4f;
        [SerializeField] private float jitter = 0.08f;
        [SerializeField] private float lineWidth = 0.03f;

        [Header("Flicker")]
        [SerializeField] private float minFlickerInterval = 0.03f;
        [SerializeField] private float maxFlickerInterval = 0.12f;
        [SerializeField, Range(0f, 1f)] private float showChance = 0.6f;

        private MaterialPropertyBlock block;
        private float[] nextFlicker;
        private bool visible;

        private void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            block = new MaterialPropertyBlock();

            nextFlicker = new float[bolts.Length];
            for (int i = 0; i < bolts.Length; i++)
            {
                bolts[i].enabled = false;
            }
        }

        private void Update()
        {
            PlayerStatusEffects status = player != null ? player.Status : null;
            Color color = default;
            bool shouldShow = status != null && status.TryGetColor(PlayerStatusEffects.Stun, out color);

            if (shouldShow != visible)
            {
                visible = shouldShow;
                if (!visible)
                {
                    for (int i = 0; i < bolts.Length; i++) bolts[i].enabled = false;
                }
            }

            if (!visible) return;

            for (int i = 0; i < bolts.Length; i++)
            {
                nextFlicker[i] -= Time.deltaTime;
                if (nextFlicker[i] > 0f) continue;

                nextFlicker[i] = Random.Range(minFlickerInterval, maxFlickerInterval);

                bool show = Random.value < showChance;
                bolts[i].enabled = show;
                if (!show) continue;

                PlaceBolt(bolts[i]);

                bolts[i].GetPropertyBlock(block);
                block.SetColor("_BaseColor", color);
                block.SetColor("_Color", color);
                bolts[i].SetPropertyBlock(block);
            }
        }

        /// <summary>1本の稲妻を、体の周りのランダムな位置・ランダムなジグザグで置き直す。</summary>
        private void PlaceBolt(LineRenderer bolt)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float baseHeight = Random.Range(minHeight, maxHeight);
            bolt.transform.localPosition =
                new Vector3(Mathf.Cos(angle) * radius, baseHeight, Mathf.Sin(angle) * radius);
            bolt.widthMultiplier = lineWidth;

            int segments = bolt.positionCount;
            for (int i = 0; i < segments; i++)
            {
                float t = (float)i / (segments - 1);
                float x = Random.Range(-jitter, jitter);
                float y = -t * boltLength;
                bolt.SetPosition(i, new Vector3(x, y, 0f));
            }
        }
    }
}
