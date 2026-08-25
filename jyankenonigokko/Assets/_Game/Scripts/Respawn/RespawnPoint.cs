using UnityEngine;

namespace MagicHand
{
    /// <summary>ステージ上に手動配置するリスポーン地点のマーカー。</summary>
    public class RespawnPoint : MonoBehaviour
    {
        [SerializeField] private float gizmoRadius = 0.6f;

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.55f);
            Gizmos.DrawSphere(transform.position + Vector3.up * gizmoRadius, gizmoRadius);
            Gizmos.DrawRay(transform.position, transform.forward * 2f);
        }
    }
}
