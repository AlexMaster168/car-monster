using UnityEngine;
#if ENABLE_INPUT_SYSTEM && INPUT_SYSTEM_PACKAGE
using UnityEngine.InputSystem;
#endif

namespace CarMonster
{
    /// <summary>
    /// Ввод игрока. Клавиатура: WASD/стрелки, Space — дрифт, Shift — нитро.
    /// Геймпад: RT/LT — газ/тормоз, левый стик — руль, X (West) или RB — дрифт, A (South) — нитро.
    /// Работает и с новым Input System, и со старым Input Manager.
    /// </summary>
    public class PlayerCarInput : CarInputSource
    {
        [SerializeField, Range(0f, 0.5f)] private float stickDeadZone = 0.12f;
        [SerializeField, Tooltip("Скорость, с которой клавиатурный руль доходит до упора (ед./сек).")]
        private float keyboardSteerSpeed = 6f;

        private float _keyboardSteer;

        private void Update()
        {
            float throttle = 0f;
            float steerAnalog = 0f;
            float steerDigital = 0f;
            bool drift = false;
            bool nitro = false;

#if ENABLE_INPUT_SYSTEM && INPUT_SYSTEM_PACKAGE
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) throttle += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) throttle -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) steerDigital += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) steerDigital -= 1f;
                drift |= kb.spaceKey.isPressed;
                nitro |= kb.leftShiftKey.wasPressedThisFrame || kb.rightShiftKey.wasPressedThisFrame;
            }

            Gamepad gp = Gamepad.current;
            if (gp != null)
            {
                throttle += gp.rightTrigger.ReadValue() - gp.leftTrigger.ReadValue();
                float stick = gp.leftStick.x.ReadValue();
                if (Mathf.Abs(stick) > stickDeadZone)
                    steerAnalog = Mathf.Sign(stick) * Mathf.InverseLerp(stickDeadZone, 1f, Mathf.Abs(stick));
                drift |= gp.buttonWest.isPressed || gp.rightShoulder.isPressed;
                nitro |= gp.buttonSouth.wasPressedThisFrame;
            }
#else
            throttle = Input.GetAxisRaw("Vertical");
            steerDigital = Input.GetAxisRaw("Horizontal");
            drift = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.JoystickButton2);
            nitro = Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift) ||
                    Input.GetKeyDown(KeyCode.JoystickButton0);
#endif
            // Клавиатурный руль плавно доезжает до упора — так на 350 км/ч машина не дёргается.
            _keyboardSteer = Mathf.MoveTowards(_keyboardSteer, steerDigital, keyboardSteerSpeed * Time.unscaledDeltaTime);

            Throttle = Mathf.Clamp(throttle, -1f, 1f);
            Steer = Mathf.Abs(steerAnalog) > Mathf.Abs(_keyboardSteer) ? steerAnalog : _keyboardSteer;
            Drift = drift;
            if (nitro) PressNitro();
        }

        public override void ResetInput()
        {
            base.ResetInput();
            _keyboardSteer = 0f;
        }
    }
}
