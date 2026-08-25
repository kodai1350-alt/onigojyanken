using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// ほうきの見た目。持ち歩いている間は手に、飛んでいる間は股下に置き換える。
    ///
    /// ほうきはスクロール枠を共有しているので、発動した瞬間にストックからは消える。
    /// けれど見た目は着地するまで残さないと、乗っている本体が消えてしまう。
    /// そのため表示条件は「ストックに持っている」か「飛行中」のどちらかにしてある。
    ///
    /// 搭乗モーションは1ポーズしか用意していない。上昇・下降・旋回の手応えは、
    /// キャラクターごと傾ける「機体の傾き」で出している。
    /// 7ボーンのリグでポーズを何種類も作るより、乗り物ごと傾けたほうが
    /// 少ない手間で飛んでいる感じが出るため。
    /// </summary>
    public class PlayerBroomVisual : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private GameObject broom;

        [Header("Hold (持ち歩き)")]
        [Tooltip("ほうきを持たせる腕のボーン。左右前後の位置だけここから取る")]
        [SerializeField] private Transform arm;

        [Tooltip("ほうきの原点から石突き（穂の先）へ向かうベクトル（ほうきのローカル座標）")]
        [SerializeField] private Vector3 pivotToBottom = Vector3.down;

        [SerializeField] private float groundClearance = 0.1f;
        [SerializeField] private float forwardOffset = 0.12f;
        [SerializeField] private float tiltForward = 12f;
        [SerializeField] private float tiltSide = -6f;

        [Header("Ride (搭乗)")]
        [Tooltip("またがったときのほうきの位置（キャラの見た目ルート基準）")]
        [SerializeField] private Vector3 ridePosition = new Vector3(0f, 0.86f, -0.05f);

        [Tooltip("またがったときのほうきの角度。柄を前、穂を後ろへ向ける")]
        [SerializeField] private Vector3 rideRotation = new Vector3(90f, 0f, 0f);

        [Header("機体の傾き")]
        [Tooltip("上昇・下降でどれだけ機首を上げ下げするか")]
        [SerializeField] private float pitchAmount = 22f;

        [Tooltip("旋回でどれだけ機体を横に倒すか")]
        [SerializeField] private float rollAmount = 28f;

        [Tooltip("この速さで回頭しているときに傾きが最大になる（度/秒）")]
        [SerializeField, Min(1f)] private float rollReferenceTurnRate = 180f;

        [Tooltip("傾きの追従の速さ。大きいほどきびきび傾く")]
        [SerializeField, Min(0.1f)] private float tiltResponse = 8f;

        private float lastYaw;
        private float pitch;
        private float roll;

        private void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (broom != null) broom.SetActive(false);
            lastYaw = transform.eulerAngles.y;
        }

        // Animator がボーンを動かしたあとに上書きしたいので LateUpdate で置く
        private void LateUpdate()
        {
            if (player == null || broom == null) return;

            bool riding = player.IsRiding;
            bool carrying = !riding && HasBroomInStock();
            bool visible = riding || carrying;

            if (broom.activeSelf != visible) broom.SetActive(visible);

            UpdateTilt(riding);

            if (!visible) return;

            if (riding) PlaceOnRide();
            else PlaceInHand();
        }

        private bool HasBroomInStock()
        {
            ScrollStock stock = player.Scrolls;
            return stock != null && stock.Current is BroomEffectSO;
        }

        private void PlaceInHand()
        {
            HeldItemPose.PlaceUpright(broom.transform, transform, arm,
                                      pivotToBottom, groundClearance, forwardOffset,
                                      tiltForward, tiltSide);
        }

        /// <summary>またがった位置。傾けた見た目ルートの子として置くので、機体ごと一緒に傾く。</summary>
        private void PlaceOnRide()
        {
            Transform t = broom.transform;
            t.localPosition = ridePosition;
            t.localRotation = Quaternion.Euler(rideRotation);
        }

        /// <summary>
        /// キャラクターごと傾けて「乗り物を操っている」感じを出す。
        ///
        /// 傾ける先を見た目ルートに限っているのは、本体（PlayerController の Transform）を
        /// 傾けると当たり判定のカプセルまで倒れて接地判定が狂うため。
        /// </summary>
        private void UpdateTilt(bool riding)
        {
            float yaw = transform.eulerAngles.y;
            float yawDelta = Mathf.DeltaAngle(lastYaw, yaw);
            lastYaw = yaw;

            float targetPitch = 0f;
            float targetRoll = 0f;

            if (riding && player.Flight != null)
            {
                // 上昇で機首上げ。Unity の X 回転は正で機首下げなので符号を反転する
                targetPitch = -player.Flight.VerticalInput * pitchAmount;

                float turnRate = Time.deltaTime > 0f ? yawDelta / Time.deltaTime : 0f;
                targetRoll = -Mathf.Clamp(turnRate / rollReferenceTurnRate, -1f, 1f) * rollAmount;
            }

            float t = 1f - Mathf.Exp(-tiltResponse * Time.deltaTime);
            pitch = Mathf.Lerp(pitch, targetPitch, t);
            roll = Mathf.Lerp(roll, targetRoll, t);

            transform.localRotation = Quaternion.Euler(pitch, 0f, roll);
        }
    }
}
