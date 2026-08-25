using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// スクロールを持っている間だけ、手に杖を出す。
    ///
    /// 「今スクロールを持っているか」は本来HUDでしか分からない情報だったが、
    /// 見た目に出すことで自分も相手も一目で分かるようにする。
    /// 相手に手の内が伝わるのは承知のうえで、緊張感を生む演出として入れている。
    ///
    /// 位置合わせは毎フレーム <see cref="HeldItemPose"/> に任せている。理由はそちらに書いてある。
    /// ほうきも同じ持ち方をするが、乗る姿勢を別に持つので PlayerBroomVisual が受け持つ。
    /// </summary>
    public class PlayerStaffVisual : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private GameObject staff;

        [Tooltip("杖を持たせる腕のボーン。左右前後の位置だけここから取る")]
        [SerializeField] private Transform arm;

        [Tooltip("杖の原点から石突きへ向かうベクトル（杖のローカル座標）")]
        [SerializeField] private Vector3 pivotToBottom = Vector3.down;

        [Tooltip("石突きを足元からどれだけ浮かせるか")]
        [SerializeField] private float groundClearance = 0.1f;

        [Tooltip("体から前方向へどれだけ離すか")]
        [SerializeField] private float forwardOffset = 0.12f;

        [SerializeField] private float tiltForward = 12f;
        [SerializeField] private float tiltSide = -6f;

        private void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (staff != null) staff.SetActive(false);
        }

        // Animator がボーンを動かしたあとに上書きしたいので LateUpdate で置く
        private void LateUpdate()
        {
            if (player == null || staff == null) return;

            // ほうきは杖ではなくほうきの見た目で持たせるので、ここでは除く
            ScrollStock stock = player.Scrolls;
            bool hasStaff = stock != null && stock.HasStock && !(stock.Current is BroomEffectSO);

            if (staff.activeSelf != hasStaff) staff.SetActive(hasStaff);
            if (!hasStaff || arm == null) return;

            Place();
        }

        private void Place()
        {
            HeldItemPose.PlaceUpright(staff.transform, transform, arm,
                                      pivotToBottom, groundClearance, forwardOffset,
                                      tiltForward, tiltSide);
        }
    }
}
