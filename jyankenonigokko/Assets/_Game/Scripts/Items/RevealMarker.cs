using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// 対象の頭上に付いてくる、壁越しでも見えるマーカー。
    ///
    /// 専用シェーダー（MagicHand/XRayMarker）で深度テストを無視して描くため、
    /// 障害物の裏にいても位置が分かる。
    /// 使用者だけに見せたいので、生成時に「そのプレイヤーのカメラだけが映すレイヤー」へ乗せる。
    /// </summary>
    public class RevealMarker : MonoBehaviour
    {
        [SerializeField] private float spinSpeed = 120f;
        [SerializeField] private float bobAmplitude = 0.2f;
        [SerializeField] private float bobSpeed = 3f;

        private Transform target;
        private Vector3 offset;
        private float remaining;

        /// <summary>マーカーを1つ出す。対象が消えるか時間切れで自動的に片付く。</summary>
        public static void Spawn(RevealMarker prefab, Transform target, Vector3 offset, float duration, int layer)
        {
            if (prefab == null || target == null) return;

            RevealMarker marker = Instantiate(prefab, target.position + offset, Quaternion.identity);
            marker.target = target;
            marker.offset = offset;
            marker.remaining = duration;

            if (layer >= 0) SetLayerRecursively(marker.gameObject, layer);
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        private void LateUpdate()
        {
            remaining -= Time.deltaTime;

            GameManager manager = GameManager.Instance;
            bool inPlay = manager != null
                          && (manager.CurrentState == GameState.InGame || manager.CurrentState == GameState.Lobby);

            // 対象が拾われて消えた／時間切れ／試合が終わった、のいずれでも片付ける
            if (remaining <= 0f || target == null || !inPlay)
            {
                Destroy(gameObject);
                return;
            }

            float bob = Mathf.Sin(Time.time * bobSpeed) * bobAmplitude;
            transform.position = target.position + offset + Vector3.up * bob;
            transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
        }
    }
}
