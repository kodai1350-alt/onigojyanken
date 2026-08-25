using System.Collections.Generic;
using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// リスポーン地点の管理。相手プレイヤーから十分離れた地点の中からランダムに選ぶ。
    ///
    /// 以前は「最も遠い1地点」だけを採っていたが、地点を20箇所へ増やすと
    /// マップの最外周にある2階の回廊が常に最遠になり、実質4箇所しか使われなくなった。
    /// 狙いは「相手のそばに湧かない」ことであって最遠である必要はないので、
    /// 最遠距離に対する割合でしきい値を引き、その圏内から抽選している。
    /// </summary>
    public class RespawnManager : MonoBehaviour
    {
        [SerializeField] private List<RespawnPoint> points = new List<RespawnPoint>();

        [Tooltip("最遠距離のこの割合以上を「十分離れている」とみなして抽選対象にする")]
        [SerializeField, Range(0.1f, 1f)] private float safeRatio = 0.6f;

        private readonly List<RespawnPoint> candidates = new List<RespawnPoint>();

        public IReadOnlyList<RespawnPoint> Points => points;

        private void Awake()
        {
            if (points.Count == 0)
            {
                points.AddRange(GetComponentsInChildren<RespawnPoint>());
            }
        }

        /// <summary>指定位置から十分離れたリスポーン地点を返す。</summary>
        public RespawnPoint GetFarthestFrom(Vector3 origin)
        {
            if (points.Count == 0) return null;

            float best = 0f;
            foreach (RespawnPoint point in points)
            {
                if (point == null) continue;
                float distance = Vector3.Distance(point.transform.position, origin);
                if (distance > best) best = distance;
            }

            // 割合で引くので、相手が中央にいて全体的に距離が縮むときも自動で追随する
            float threshold = best * safeRatio;

            candidates.Clear();
            foreach (RespawnPoint point in points)
            {
                if (point == null) continue;
                if (Vector3.Distance(point.transform.position, origin) >= threshold)
                {
                    candidates.Add(point);
                }
            }

            if (candidates.Count == 0) return null;
            return candidates[Random.Range(0, candidates.Count)];
        }

        /// <summary>まだ使われていない任意の地点（試合開始時の配置用）。</summary>
        public RespawnPoint GetRandomPoint()
        {
            if (points.Count == 0) return null;
            return points[Random.Range(0, points.Count)];
        }
    }
}
