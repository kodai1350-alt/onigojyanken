using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MagicHand
{
    /// <summary>
    /// コントローラーの接続台数に応じて、1P→2Pの優先順で Gamepad/Keyboard を割り当てる。
    /// 0台: 両者Keyboard / 1台: 1P=Gamepad・2P=Keyboard / 2台以上: 1P・2PともGamepad（3台目以降は無視）。
    /// 試合中の抜き差しにも追従するため、onDeviceChange を常時監視する
    /// （準備ルーム開始時だけの判定だと、試合中に挿したコントローラーが反映されない）。
    /// </summary>
    public sealed class ControllerPriorityAssigner : IDisposable
    {
        private const string GamepadScheme = "Gamepad";
        private const string KeyboardScheme = "Keyboard";

        private readonly PlayerInput player1Input;
        private readonly PlayerInput player2Input;
        private readonly MonoBehaviour coroutineHost;
        private Coroutine reassignRoutine;

        public ControllerPriorityAssigner(PlayerInput player1Input, PlayerInput player2Input, MonoBehaviour coroutineHost)
        {
            this.player1Input = player1Input;
            this.player2Input = player2Input;
            this.coroutineHost = coroutineHost;

            InputSystem.onDeviceChange += OnDeviceChange;
            RequestReassign();
        }

        public void Dispose()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (!(device is Gamepad)) return;
            if (change != InputDeviceChange.Added && change != InputDeviceChange.Removed) return;

            RequestReassign();
        }

        private void RequestReassign()
        {
            if (coroutineHost == null) return;

            if (reassignRoutine != null) coroutineHost.StopCoroutine(reassignRoutine);
            reassignRoutine = coroutineHost.StartCoroutine(ReassignRoutine());
        }

        /// <summary>
        /// 1P→2Pの順に割り当てるが、間に1フレーム空ける。
        ///
        /// 同じフレーム内で2人ぶん連続して SwitchCurrentControlScheme を呼ぶと、
        /// 一度も入力を受けたことのない PlayerInput 同士では Input System 側の
        /// InputUser 登録が競合し、どちらか片方が "Invalid user" 例外で失敗することを実測で確認した
        /// （毎回1P/2Pのどちらかが失敗し、どちらが失敗するかは実行のたびに変わる＝競合状態）。
        /// 1フレーム空けるだけで安定して両方成功するようになる
        /// </summary>
        private IEnumerator ReassignRoutine()
        {
            var gamepads = Gamepad.all;
            Keyboard keyboard = Keyboard.current;

            InputDevice player1Device = gamepads.Count >= 1 ? (InputDevice)gamepads[0] : keyboard;
            InputDevice player2Device = gamepads.Count >= 2 ? (InputDevice)gamepads[1] : keyboard;

            Assign(player1Input, gamepads.Count >= 1 ? GamepadScheme : KeyboardScheme, player1Device);
            yield return null;
            Assign(player2Input, gamepads.Count >= 2 ? GamepadScheme : KeyboardScheme, player2Device);

            reassignRoutine = null;
        }

        private static void Assign(PlayerInput input, string scheme, InputDevice device)
        {
            if (input == null || device == null) return;
            if (input.currentControlScheme == scheme && input.devices.Count == 1 && input.devices[0] == device) return;

            try
            {
                input.SwitchCurrentControlScheme(scheme, device);
            }
            catch (InvalidOperationException e)
            {
                // 1フレーム空けても、ごく稀にInput System側の初期化が間に合わないことがある。
                // 次のonDeviceChange（や次の抜き差し）でやり直されるので、
                // ここでは握りつぶさずログだけ残して先へ進む
                Debug.LogWarning($"[MagicHand] コントローラー割り当てに失敗しました（{input.name} → {scheme}）: {e.Message}");
            }
        }
    }
}
