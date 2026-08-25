using System;
using UnityEngine;

namespace MagicHand
{
    /// <summary>
    /// 試合の制限時間カウントダウン。GameManager から Tick() を回す純粋なロジック。
    /// </summary>
    [Serializable]
    public class MatchTimer
    {
        [SerializeField, Min(1f)] private float duration = 180f;

        public float Duration => duration;
        public float Remaining { get; private set; }
        public bool IsRunning { get; private set; }
        public bool IsFinished => Remaining <= 0f;

        public void SetDuration(float seconds)
        {
            duration = Mathf.Max(1f, seconds);
        }

        public void ResetTimer()
        {
            Remaining = duration;
            IsRunning = false;
        }

        public void Begin()
        {
            Remaining = duration;
            IsRunning = true;
        }

        public void Stop()
        {
            IsRunning = false;
        }

        /// <summary>時間を進める。この呼び出しで0に到達したら true を返す。</summary>
        public bool Tick(float deltaTime)
        {
            if (!IsRunning) return false;

            Remaining -= deltaTime;
            if (Remaining > 0f) return false;

            Remaining = 0f;
            IsRunning = false;
            return true;
        }

        public string ToDisplayString()
        {
            int total = Mathf.CeilToInt(Mathf.Max(0f, Remaining));
            return $"{total / 60:0}:{total % 60:00}";
        }
    }
}
