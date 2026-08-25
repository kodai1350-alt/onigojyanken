using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// 頭上に今の手（グー／チョキ／パー）を表示するマーカー。
    ///
    /// 自分の手は選んだ本人がもう知っているので、自分には見せる意味が無い。
    /// 相手の頭上にだけ見えるようにすることで、色だけでなく形でも
    /// 相手の手が分かるようにする。自分からは見えないので視界の邪魔にならない。
    /// 「相手のカメラだけが映すレイヤー」への割り当てはビルダー側で行う。
    /// </summary>
    public class PlayerHandIndicator : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private GameObject guVisual;
        [SerializeField] private GameObject chokiVisual;
        [SerializeField] private GameObject paVisual;
        [SerializeField] private float spinSpeed = 40f;

        private HandType shown = HandType.None;

        private void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
        }

        private void Update()
        {
            HandType hand = player != null ? player.CurrentHand : HandType.None;

            if (hand != shown)
            {
                shown = hand;
                if (guVisual != null) guVisual.SetActive(hand == HandType.Gu);
                if (chokiVisual != null) chokiVisual.SetActive(hand == HandType.Choki);
                if (paVisual != null) paVisual.SetActive(hand == HandType.Pa);
            }

            transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
        }
    }
}
