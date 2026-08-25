using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// 引き伸ばした Cube や Plane に、実寸に合わせたテクスチャの繰り返しを与える。
    ///
    /// ステージは大きさを変えたプリミティブで組んでいるため、
    /// 1m単位で作られた石材のテクスチャをそのまま貼ると盛大に伸びてしまう。
    /// MaterialPropertyBlock はシーンに保存されないので、
    /// 実行時に毎回かけ直せるようコンポーネントとして持たせている。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public class WorldScaleTiling : MonoBehaviour
    {
        [Tooltip("テクスチャ1枚が何メートルぶんに相当するか")]
        [SerializeField, Min(0.1f)] private float unitsPerTile = 3f;

        [Tooltip("スケール1のときのメッシュの一辺。Cube は 1、Plane は 10")]
        [SerializeField, Min(0.01f)] private float meshUnitSize = 1f;

        [Tooltip("縦方向に高さ(Y)を使うか。壁は true、床は false（奥行きZを使う）")]
        [SerializeField] private bool verticalUsesHeight;

        [SerializeField] private string textureProperty = "_BaseMap";

        private MaterialPropertyBlock block;

        private void OnEnable() => Apply();

        private void OnValidate() => Apply();

        private void Apply()
        {
            Renderer target = GetComponent<Renderer>();
            if (target == null) return;

            block ??= new MaterialPropertyBlock();

            Vector3 scale = transform.lossyScale;
            float width = Mathf.Abs(scale.x) * meshUnitSize;
            float height = verticalUsesHeight
                ? Mathf.Abs(scale.y) * meshUnitSize
                : Mathf.Abs(scale.z) * meshUnitSize;

            Vector2 tiling = new Vector2(
                Mathf.Max(1f, width / unitsPerTile),
                Mathf.Max(1f, height / unitsPerTile));

            target.GetPropertyBlock(block);
            block.SetVector($"{textureProperty}_ST", new Vector4(tiling.x, tiling.y, 0f, 0f));
            target.SetPropertyBlock(block);
        }
    }
}
