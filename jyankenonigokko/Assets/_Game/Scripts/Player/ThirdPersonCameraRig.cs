using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// フォートナイト等のTPSに近い「肩越し追従カメラ」。Camera を持つ GameObject にアタッチする。
    ///
    /// この方式の要点:
    ///  - カメラはキャラの真後ろではなく、肩の横（shoulderOffset）にずらして構える
    ///  - 視点入力（右スティック／矢印キー）がそのままカメラの向きになる
    ///  - キャラはカメラの向きに追従して回頭する（PlayerController 側で実施）ため、
    ///    左右入力は「向きを変える」のではなく「ストレイフ（横歩き）」になる
    ///
    /// PlayerController から SetLookInput() で視点入力を受け取り、
    /// Yaw / PlanarForward / PlanarRight を移動と回頭の基準として提供する。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class ThirdPersonCameraRig : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform target;
        [SerializeField] private float targetHeight = 1.5f;

        [Tooltip("肩越し視点の横ずらし量。正で右肩越し、0で真後ろ")]
        [SerializeField] private float shoulderOffset = 0.7f;

        [Header("Framing")]
        [SerializeField, Min(0.5f)] private float distance = 5f;
        [SerializeField] private float minPitch = -40f;
        [SerializeField] private float maxPitch = 70f;
        [SerializeField] private float startPitch = 12f;

        [Tooltip("飛行中に使う下向きの限界。高度14mから着地地点を見下ろすには通常の-40度では足りない")]
        [SerializeField] private float flightMinPitch = -70f;

        [Header("Look Speed")]
        [SerializeField] private float yawSpeed = 260f;
        [SerializeField] private float pitchSpeed = 180f;
        [SerializeField] private bool invertPitch = false;

        [Header("Smoothing / Collision")]
        [Tooltip("TPSでは視点が遅れると狙いにくいので、追従の遅れはごく小さくする")]
        [SerializeField] private float followSmoothTime = 0.03f;
        [SerializeField] private LayerMask collisionMask = ~0;
        [SerializeField, Min(0f)] private float collisionRadius = 0.25f;

        [Tooltip("障害物に押し込まれても、これより近くにはカメラを寄せない。" +
                 "UI Canvas の planeDistance より必ず大きくすること（小さいとキャラがUIより手前に描画される）")]
        [SerializeField, Min(0.5f)] private float minDistance = 3f;

        private Vector2 lookInput;
        private Vector3 followVelocity;
        private float yaw;
        private float pitch;
        private float sensitivity = 1f;
        private bool allowDeepLook;
        private Camera cam;

        /// <summary>準備ルームやオプションで設定した視野角（度）。</summary>
        public void SetFieldOfView(float degrees)
        {
            if (cam == null) cam = GetComponent<Camera>();
            cam.fieldOfView = degrees;
        }

        /// <summary>飛行中だけ下向きの限界を広げる。着地地点を見下ろせるようにするため。</summary>
        public void SetDeepLookAllowed(bool allowed)
        {
            allowDeepLook = allowed;
        }

        /// <summary>準備ルームやオプションで設定した視点感度の倍率。</summary>
        public void SetSensitivity(float multiplier)
        {
            sensitivity = Mathf.Max(0.1f, multiplier);
        }

        /// <summary>上下反転の設定。</summary>
        public void SetInvertPitch(bool invert)
        {
            invertPitch = invert;
        }

        /// <summary>カメラの水平方向の向き。キャラはこの角度に回頭する。</summary>
        public float Yaw => yaw;

        /// <summary>水平面に投影したカメラ前方向（移動入力の基準）。</summary>
        public Vector3 PlanarForward => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

        /// <summary>水平面に投影したカメラ右方向（移動入力の基準）。</summary>
        public Vector3 PlanarRight => Quaternion.Euler(0f, yaw, 0f) * Vector3.right;

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            SnapToTarget();
        }

        public void SetLookInput(Vector2 input)
        {
            lookInput = input;
        }

        private void Awake()
        {
            pitch = startPitch;
            if (target != null) yaw = target.eulerAngles.y;
        }

        private void Start()
        {
            SnapToTarget();
        }

        /// <summary>リスポーン等でワープした際に補間を挟まず即座に追従させる。</summary>
        public void SnapToTarget()
        {
            if (target == null) return;

            yaw = target.eulerAngles.y;
            followVelocity = Vector3.zero;
            transform.position = ResolvePosition(out Quaternion rotation);
            transform.rotation = rotation;
        }

        private void LateUpdate()
        {
            if (target == null) return;

            float dt = Time.deltaTime;
            yaw += lookInput.x * yawSpeed * sensitivity * dt;
            pitch += (invertPitch ? lookInput.y : -lookInput.y) * pitchSpeed * sensitivity * dt;
            pitch = Mathf.Clamp(pitch, allowDeepLook ? flightMinPitch : minPitch, maxPitch);

            // 飛行が終わったあとに深く見下ろしたままだと画面が地面で埋まるので、通常の範囲へ戻す
            if (!allowDeepLook && pitch < minPitch)
            {
                pitch = Mathf.MoveTowards(pitch, minPitch, pitchSpeed * dt);
            }

            Vector3 desired = ResolvePosition(out Quaternion rotation);
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref followVelocity, followSmoothTime);
            transform.rotation = rotation;
        }

        /// <summary>
        /// 肩越しの注視点（キャラの頭あたりを、カメラ基準で横にずらした点）。
        /// 横ずらしはカメラのyawに合わせて回すので、視点を回しても常に同じ肩側に留まる。
        /// </summary>
        private Vector3 FocusPoint =>
            target.position
            + Vector3.up * targetHeight
            + Quaternion.Euler(0f, yaw, 0f) * Vector3.right * shoulderOffset;

        /// <summary>障害物を考慮したカメラ位置を求める。追従対象自身のコライダーは無視する。</summary>
        private Vector3 ResolvePosition(out Quaternion rotation)
        {
            rotation = Quaternion.Euler(pitch, yaw, 0f);

            Vector3 focus = FocusPoint;
            Vector3 direction = rotation * Vector3.back;
            float allowed = distance;

            RaycastHit[] hits = Physics.SphereCastAll(focus, collisionRadius, direction, distance, collisionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                // 自分自身（追従対象）に埋まって手前に寄ってしまうのを防ぐ
                if (hits[i].transform.IsChildOf(target)) continue;
                allowed = Mathf.Min(allowed, hits[i].distance);
            }

            return focus + direction * Mathf.Max(minDistance, allowed);
        }
    }
}
