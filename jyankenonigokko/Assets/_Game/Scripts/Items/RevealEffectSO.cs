using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// 一定時間、相手プレイヤーの位置に壁越しで見えるマーカーを出す索敵スクロール（サーチ）。
    /// マーカーは使用者のカメラにしか映らないレイヤーへ乗せるので、相手には見えない。
    /// </summary>
    [CreateAssetMenu(fileName = "Scroll_Reveal", menuName = "MagicHand/Scrolls/Reveal")]
    public class RevealEffectSO : ScrollEffectSO
    {
        [Header("Reveal")]
        [SerializeField, Min(1f)] private float duration = 10f;
        [SerializeField] private RevealMarker markerPrefab;
        [SerializeField] private Vector3 markerOffset = new Vector3(0f, 2.8f, 0f);

        [Header("Wave")]
        [Tooltip("発動時、使用者を中心に周囲へ広げる走査波動の演出")]
        [SerializeField] private SearchWaveEffect wavePrefab;
        [SerializeField] private Vector3 waveOffset = new Vector3(0f, 1.1f, 0f);

        public override void Apply(PlayerController user)
        {
            if (user == null || markerPrefab == null) return;

            GameManager manager = GameManager.Instance;
            if (manager == null) return;

            foreach (PlayerController other in manager.Players)
            {
                if (other == null || other == user) continue;
                RevealMarker.Spawn(markerPrefab, other.transform, markerOffset, duration, user.OwnViewLayer);
            }

            SearchWaveEffect.Spawn(wavePrefab, user.transform.position + waveOffset, DisplayColor);

            // 残り時間はマーカー側にしか無く、本人は自分が索敵中かどうかも分からなかった。
            // 画面外の相手を指す矢印もこの登録を見て出す
            if (user.Status != null)
            {
                user.Status.Apply(PlayerStatusEffects.RevealEnemy, "サーチ", duration, DisplayColor);
            }

            NotifySearchedTargets(user, manager);
        }

        /// <summary>
        /// 見られている側にも知らせる。
        ///
        /// サーチは相手の画面にだけ情報が出るので、見られている本人は
        /// 何をされたかも分からないまま追い詰められることになる。
        /// 隠れるのか逃げるのか、対処を選べるようにするための通知。
        /// </summary>
        private void NotifySearchedTargets(PlayerController user, GameManager manager)
        {
            foreach (PlayerController other in manager.Players)
            {
                if (other == null || other == user || other.Status == null) continue;
                other.Status.Apply(PlayerStatusEffects.Searched, "サーチされている", duration, DisplayColor);
            }
        }
    }
}
