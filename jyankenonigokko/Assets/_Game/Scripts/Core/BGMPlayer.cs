using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// BGMを1つの AudioSource で鳴らし続ける。
    ///
    /// タイトルだけ専用の曲にして、それ以外（準備ルーム～サドンデスまで）は
    /// ひとまとめに試合用の曲を流す。結果発表は別シーン（Victory）に移るので、
    /// そちらの曲は VictoryManager 側が持つ。
    ///
    /// 曲の切り替えは「今鳴らすべき曲」が変わったフレームだけ行う。
    /// 同じ曲のまま毎フレーム Play し直すと、そのたびに頭出しされて音楽が途切れて聞こえるため。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class BGMPlayer : MonoBehaviour
    {
        [SerializeField] private AudioClip titleClip;
        [SerializeField] private AudioClip playClip;
        [SerializeField, Range(0f, 1f)] private float volume = 0.01f;

        private AudioSource source;
        private AudioClip current;

        private void Awake()
        {
            source = GetComponent<AudioSource>();
            source.loop = true;
            source.playOnAwake = false;
            source.volume = volume;
        }

        private void Update()
        {
            GameManager manager = GameManager.Instance;
            if (manager == null) return;

            AudioClip wanted = manager.CurrentState == GameState.Title ? titleClip : playClip;
            if (wanted == current) return;

            current = wanted;
            source.Stop();
            source.clip = wanted;
            if (wanted != null) source.Play();
        }
    }
}
