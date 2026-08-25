using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// 長物（杖・ほうき）を手に持たせる姿勢の計算。杖とほうきで同じ持ち方をするので共有する。
    ///
    /// このキャラの骨格は Main_Rig → Spine00 → Arm_L / Arm_R の7本しかなく、手のボーンが無い。
    /// さらに腕のボーンには 0.28 倍のスケールが入っていて、子にすると持ち物が縮む。
    /// そのため持ち物は腕の子にせず、毎フレームここで位置を計算して置き直している。
    ///
    /// 方針は「左右前後は腕のボーンに追従、角度と高さは足元基準で固定」。
    /// 高さを腕から取ると破綻する。Arm_R の原点は手の位置ではないうえ
    /// （身長1.92mに対しY=0.84）、アニメーションで上下するので、
    /// そのまま握り位置として使うと2m超の杖の石突きが地面を突き抜ける。
    /// </summary>
    public static class HeldItemPose
    {
        /// <summary>
        /// 縦に構えた持ち物を、腕の位置に合わせて置く。
        /// </summary>
        /// <param name="item">置く対象。</param>
        /// <param name="owner">キャラクターの見た目ルート。足元の高さと向きの基準になる。</param>
        /// <param name="arm">追従する腕のボーン。左右前後だけを借りる。</param>
        /// <param name="pivotToBottom">持ち物の原点から石突きへ向かうベクトル（持ち物のローカル座標）。</param>
        /// <param name="groundClearance">石突きを足元からどれだけ浮かせるか。</param>
        /// <param name="forwardOffset">体から前方向へどれだけ離すか。</param>
        /// <param name="tiltForward">前傾させる角度。</param>
        /// <param name="tiltSide">横へ傾ける角度。</param>
        public static void PlaceUpright(Transform item, Transform owner, Transform arm,
                                        Vector3 pivotToBottom, float groundClearance, float forwardOffset,
                                        float tiltForward, float tiltSide)
        {
            if (item == null || owner == null || arm == null) return;

            item.rotation = owner.rotation * Quaternion.Euler(tiltForward, 0f, tiltSide);

            Vector3 target = new Vector3(arm.position.x,
                                         owner.position.y + groundClearance,
                                         arm.position.z)
                             + owner.forward * forwardOffset;

            item.position = target - item.rotation * pivotToBottom;
        }
    }
}
