using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

namespace MagicHand
{
    public class VictoryManager : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TMP_Text resultText;
        [SerializeField] private TMP_Text scoreText;

        [Header("Scene Settings")]
        [SerializeField] private string readySceneName = "Ready";

        [Header("Avatar Animation References")]
        [SerializeField] private Animator player1Animator;
        [SerializeField] private Animator player2Animator;

        [Header("Animator Trigger Names")]
        [SerializeField] private string winTriggerName = "Win";
        [SerializeField] private string loseTriggerName = "Lose";
        [SerializeField] private string drawTriggerName = "Draw";

        [Header("BGM")]
        [SerializeField] private AudioClip resultBgm;
        [SerializeField, Range(0f, 1f)] private float bgmVolume = 0.01f;

        private void Start()
        {
            int p1 = MatchResultData.Player1Score;
            int p2 = MatchResultData.Player2Score;

            if (scoreText != null)
            {
                scoreText.text = $"{p1}  -  {p2}";
            }

            int winner = MatchResultData.WinnerIndex;

            if (resultText != null)
            {
                if (winner == 0) resultText.text = "1P WIN!";
                else if (winner == 1) resultText.text = "2P WIN!";
                else resultText.text = "DRAW";
            }

            // アニメーション再生処理の呼出
            PlayAvatarAnimations(winner);

            PlayResultBgm();
        }

        /// <summary>
        /// リザルトBGMを鳴らす。専用の AudioSource が置かれていないシーンでも動くよう、
        /// このスクリプト自身のオブジェクトへ実行時に追加する。
        /// </summary>
        private void PlayResultBgm()
        {
            if (resultBgm == null) return;

            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.clip = resultBgm;
            source.loop = true;
            source.playOnAwake = false;
            source.volume = bgmVolume;
            source.Play();
        }

        /// <summary>
        /// 勝敗結果に応じて各プレイヤーのアニメーションを再生する
        /// </summary>
        /// <param name="winner">0: 1P勝利, 1: 2P勝利, その他: 引き分け</param>
        private void PlayAvatarAnimations(int winner)
        {
            if (winner == 0)
            {
                // 1P 勝利 / 2P 敗北
                if (player1Animator != null) player1Animator.SetTrigger(winTriggerName);
                if (player2Animator != null) player2Animator.SetTrigger(loseTriggerName);
            }
            else if (winner == 1)
            {
                // 1P 敗北 / 2P 勝利
                if (player1Animator != null) player1Animator.SetTrigger(loseTriggerName);
                if (player2Animator != null) player2Animator.SetTrigger(winTriggerName);
            }
            else
            {
                // 引き分け
                if (player1Animator != null) player1Animator.SetTrigger(drawTriggerName);
                if (player2Animator != null) player2Animator.SetTrigger(drawTriggerName);
            }
        }

        public void ReturnToReadyScene()
        {
            MatchResultData.Reset();
            // インスペクターで設定した readySceneName を使用するように変更
            SceneManager.LoadScene("MainScene");
        }

        private void Update()
        {
          
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetButtonDown("Submit"))
            {
                ReturnToReadyScene();
            }
        }
    }
}