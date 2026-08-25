using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// 効果音（SE）をワンショットで鳴らす。
    ///
    /// BGMと違い同時に重なって鳴っても構わないので、単一の AudioSource に
    /// PlayOneShot で重ねて鳴らす方式にしてある（鳴らすたびに新しい AudioSource を
    /// 作る必要が無く、鳴らした瞬間に前の音を止めてしまうこともない）。
    ///
    /// 呼び出し側（GameManager・TitleUI・ItemPickup 等）は「何が起きたか」だけを知っていればよく、
    /// どのクリップを鳴らすかはここに集約する。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class SEPlayer : MonoBehaviour
    {
        [Header("Clips")]
        [SerializeField] private AudioClip startButtonClip;
        [SerializeField] private AudioClip countdownClip;
        [SerializeField] private AudioClip defeatClip;
        [SerializeField] private AudioClip drawClip;
        [SerializeField] private AudioClip itemPickupClip;

        [Header("Scroll Activation")]
        [SerializeField] private AudioClip stunClip;
        [SerializeField] private AudioClip blinkClip;
        [SerializeField] private AudioClip speedUpClip;
        [SerializeField] private AudioClip charmClip;
        [SerializeField] private AudioClip broomClip;

        [SerializeField, Range(0f, 1f)] private float volume = 0.15f;

        [Tooltip("3-2-1のカウントダウン音だけ他のSEより耳につきやすいという指摘で、専用の音量を持たせた")]
        [SerializeField, Range(0f, 1f)] private float countdownVolume = 0.01f;

        private AudioSource source;

        public static SEPlayer Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>タイトル画面でSTARTを押した瞬間。</summary>
        public static void PlayStartButton() => Play(Instance != null ? Instance.startButtonClip : null);

        /// <summary>試合開始前の最初のカウントダウンが始まった瞬間。他のSEより控えめな専用音量で鳴らす。</summary>
        public static void PlayCountdown() => Play(Instance != null ? Instance.countdownClip : null,
                                                    Instance != null ? Instance.countdownVolume : 0f);

        /// <summary>勝ち手で接触し、相手が倒れた瞬間。</summary>
        public static void PlayDefeat() => Play(Instance != null ? Instance.defeatClip : null);

        /// <summary>あいこで接触し、互いに弾かれた瞬間。</summary>
        public static void PlayDraw() => Play(Instance != null ? Instance.drawClip : null);

        /// <summary>アイテムを取得した瞬間。</summary>
        public static void PlayItemPickup() => Play(Instance != null ? Instance.itemPickupClip : null);

        /// <summary>スタン系スクロールを発動した瞬間。</summary>
        public static void PlayStun() => Play(Instance != null ? Instance.stunClip : null);

        /// <summary>ワープを発動した瞬間。</summary>
        public static void PlayBlink() => Play(Instance != null ? Instance.blinkClip : null);

        /// <summary>スピードUPを発動した瞬間。</summary>
        public static void PlaySpeedUp() => Play(Instance != null ? Instance.speedUpClip : null);

        /// <summary>チェンジ（手を変える妨害）を発動した瞬間。</summary>
        public static void PlayCharm() => Play(Instance != null ? Instance.charmClip : null);

        /// <summary>ほうきに乗った瞬間。</summary>
        public static void PlayBroom() => Play(Instance != null ? Instance.broomClip : null);

        private static void Play(AudioClip clip)
        {
            if (Instance == null) return;
            Play(clip, Instance.volume);
        }

        private static void Play(AudioClip clip, float vol)
        {
            if (Instance == null || clip == null) return;
            Instance.source.PlayOneShot(clip, vol);
        }
    }
}
