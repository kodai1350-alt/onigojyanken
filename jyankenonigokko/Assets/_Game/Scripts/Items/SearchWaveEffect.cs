using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// サーチ発動時、使用者を中心に周囲へ広がる走査波動。
    ///
    /// CastEffect（地面に寝かせた1枚の輪。全アイテム共通の発動演出）とは別に、
    /// サーチだけこちらを追加で出す。互いに直交する3枚の輪（前後・左右・上下）を
    /// 同時に同じ半径で広げることで、特定の向きだけでなく全方位へ波が
    /// 広がっているように見せている（3D同心円）。
    ///
    /// 進むほど太さを0へ細らせることで「遠くへ行くほど薄くなる」見た目を作る
    /// （マテリアルの Surface Type を Transparent にせずに済む、他の演出と同じ手法）。
    /// </summary>
    public class SearchWaveEffect : MonoBehaviour
    {
        [SerializeField, Range(12, 128)] private int segments = 48;
        [SerializeField] private LineRenderer[] rings;

        [SerializeField] private float duration = 1.2f;
        [SerializeField] private float startRadius = 0.3f;
        [SerializeField] private float endRadius = 27f;
        [SerializeField] private float startWidth = 0.12f;

        private Color color = Color.white;
        private float elapsed;
        private MaterialPropertyBlock block;

        /// <summary>指定位置・指定色で波動を1つ出す。</summary>
        public static void Spawn(SearchWaveEffect prefab, Vector3 position, Color color)
        {
            if (prefab == null) return;

            SearchWaveEffect effect = Instantiate(prefab, position, Quaternion.identity);
            effect.color = color;
        }

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            for (int i = 0; i < rings.Length; i++) rings[i].positionCount = segments;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            float radius = Mathf.Lerp(startRadius, endRadius, t);
            float width = Mathf.Lerp(startWidth, 0f, t);

            for (int i = 0; i < rings.Length; i++)
            {
                BuildCircle(rings[i], radius);
                rings[i].widthMultiplier = width;

                rings[i].GetPropertyBlock(block);
                block.SetColor("_BaseColor", color);
                block.SetColor("_Color", color);
                rings[i].SetPropertyBlock(block);
            }

            if (t >= 1f) Destroy(gameObject);
        }

        private void BuildCircle(LineRenderer ring, float radius)
        {
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
            }
        }
    }
}
