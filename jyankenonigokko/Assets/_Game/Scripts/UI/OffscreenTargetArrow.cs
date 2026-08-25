using UnityEngine;
using UnityEngine.UI;

namespace MagicHand
{
    /// <summary>
    /// サーチ中、相手が画面の外にいるあいだ、画面の縁に矢印を出して方向を示す。
    ///
    /// 壁越しマーカーは相手が画面に入っていないと何も見えず、
    /// 「どっちを向けばいいのか分からない」ままサーチの時間が過ぎてしまう。
    ///
    /// 出すのは相手の位置が見えていい間だけ。常時出すと索敵スクロールの価値が消える。
    /// 見えていい場面は2つある。自分がサーチを使っているときと、
    /// 相手がほうきで着地して位置を晒しているとき。どちらも壁越しマーカーが出ているので、
    /// 矢印はそのマーカーを画面内に導くための案内になる。
    /// 相手が画面に入ったら消して、壁越しマーカーに任せる。
    /// </summary>
    public class OffscreenTargetArrow : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private RectTransform arrow;
        [SerializeField] private RectTransform canvasArea;

        [Tooltip("画面の縁からどれだけ内側に出すか（画面短辺に対する割合）")]
        [SerializeField, Range(0.01f, 0.2f)] private float edgeMargin = 0.06f;

        private void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (canvasArea == null) canvasArea = transform as RectTransform;
            if (arrow != null) arrow.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (arrow == null || player == null || viewCamera == null) return;

            Vector3 targetPosition;
            bool show = ShouldShow(out targetPosition) && !IsOnScreen(targetPosition);

            if (arrow.gameObject.activeSelf != show) arrow.gameObject.SetActive(show);
            if (!show) return;

            Place(targetPosition);
        }

        private bool ShouldShow(out Vector3 targetPosition)
        {
            targetPosition = Vector3.zero;

            GameManager manager = GameManager.Instance;
            if (manager == null || manager.CurrentState != GameState.InGame) return false;

            PlayerController opponent = manager.GetOpponent(player);
            if (opponent == null) return false;

            if (!IsSearching() && !IsOpponentExposed(opponent)) return false;

            targetPosition = opponent.transform.position + Vector3.up * 1.2f;
            return true;
        }

        /// <summary>自分がサーチスクロールを使っている最中か。</summary>
        private bool IsSearching()
        {
            PlayerStatusEffects status = player.Status;
            return status != null && status.IsActive(PlayerStatusEffects.RevealEnemy);
        }

        /// <summary>
        /// 相手がほうきで着地して位置を晒している最中か。
        ///
        /// 着地の露出は相手の画面にだけ壁越しマーカーを出しているが、
        /// 3秒しかないので画面外に居られるとそのまま気づかず終わってしまう。
        /// </summary>
        private static bool IsOpponentExposed(PlayerController opponent)
        {
            PlayerFlight flight = opponent.Flight;
            return flight != null && flight.ExposureRemaining > 0f;
        }

        private bool IsOnScreen(Vector3 worldPosition)
        {
            Vector3 view = viewCamera.WorldToViewportPoint(worldPosition);
            return view.z > 0f && view.x > 0f && view.x < 1f && view.y > 0f && view.y < 1f;
        }

        /// <summary>
        /// 矢印を画面の縁へ寄せ、相手の方向へ向ける。
        ///
        /// 背後にいるときの扱いが要注意。WorldToViewportPoint は
        /// カメラの後ろにある点でも座標を返すが、**上下左右が反転している**。
        /// z が負のときに座標をそのまま使うと、真後ろの相手を指しているつもりで
        /// 正反対の縁に矢印が出てしまう。
        /// </summary>
        private void Place(Vector3 worldPosition)
        {
            Vector3 view = viewCamera.WorldToViewportPoint(worldPosition);

            // 画面中心を原点にした方向ベクトルへ直す
            Vector2 direction = new Vector2(view.x - 0.5f, view.y - 0.5f);

            if (view.z < 0f) direction = -direction;
            if (direction.sqrMagnitude < 0.000001f) direction = Vector2.up;

            direction.Normalize();

            Rect area = canvasArea.rect;
            float margin = Mathf.Min(area.width, area.height) * edgeMargin;

            // 中心から direction 方向へ伸ばし、画面の枠に当たったところで止める
            float halfWidth = area.width * 0.5f - margin;
            float halfHeight = area.height * 0.5f - margin;

            float scaleX = Mathf.Abs(direction.x) > 0.0001f ? halfWidth / Mathf.Abs(direction.x) : float.MaxValue;
            float scaleY = Mathf.Abs(direction.y) > 0.0001f ? halfHeight / Mathf.Abs(direction.y) : float.MaxValue;
            float scale = Mathf.Min(scaleX, scaleY);

            arrow.anchoredPosition = direction * scale;
            arrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        }
    }
}
