using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// アイテム（スクロール）を発動した瞬間に一度だけ出す演出。
    ///
    /// 使用者の足元で輪が一瞬で広がって消える、それだけの単純な見た目にしてある。
    /// 「何かを使った」という手応えが無いと、発動ボタンを押したこと自体が伝わらない
    /// （特に相手にしか効果が及ばない妨害系は、使用者自身の見た目には何も起きないため気づきにくい）。
    /// 色をアイテムの DisplayColor に合わせて、「何を使ったか」もついでに伝わるようにしてある。
    ///
    /// 見た目（LineRenderer の設定・マテリアル）はビルダー側でプレハブに焼き込んであり、
    /// ここでは半径・太さ・色を毎フレーム書き換えて広がって消える動きだけを作る。
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class CastEffect : MonoBehaviour
    {
        [SerializeField, Range(8, 64)] private int segments = 36;
        [SerializeField] private float duration = 0.45f;
        [SerializeField] private float startRadius = 0.3f;
        [SerializeField] private float endRadius = 2.2f;
        [SerializeField] private float startWidth = 0.16f;

        private LineRenderer ring;
        private MaterialPropertyBlock block;
        private Color color = Color.white;
        private float elapsed;

        /// <summary>指定位置・指定色で発動エフェクトを1つ出す。実際の効果範囲や対象とは無関係の演出。</summary>
        public static void Spawn(CastEffect prefab, Vector3 position, Color color)
        {
            if (prefab == null) return;

            CastEffect effect = Instantiate(prefab, position, prefab.transform.rotation);
            effect.color = color;
        }

        private void Awake()
        {
            ring = GetComponent<LineRenderer>();
            block = new MaterialPropertyBlock();
            ring.positionCount = segments;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            BuildCircle(Mathf.Lerp(startRadius, endRadius, t));

            // 透明度ではなく太さを0へ細らせて消す。共有マテリアルの Surface Type を
            // 変えずに済み、他の輪表示（範囲円・着地点）にも影響しない
            ring.widthMultiplier = Mathf.Lerp(startWidth, 0f, t);

            ring.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            ring.SetPropertyBlock(block);

            if (t >= 1f) Destroy(gameObject);
        }

        private void BuildCircle(float radius)
        {
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
            }
        }
    }
}
