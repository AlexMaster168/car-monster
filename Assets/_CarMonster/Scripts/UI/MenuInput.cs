using UnityEngine;
#if ENABLE_INPUT_SYSTEM && INPUT_SYSTEM_PACKAGE
using UnityEngine.InputSystem;
#endif

namespace CarMonster
{
    /// <summary>Нажатия для меню (клавиатура + геймпад), работает с обоими бэкендами ввода.</summary>
    public static class MenuInput
    {
#if ENABLE_INPUT_SYSTEM && INPUT_SYSTEM_PACKAGE
        private static bool KeyDown(Key key) => Keyboard.current != null && Keyboard.current[key].wasPressedThisFrame;
        private static bool Pad(System.Func<Gamepad, UnityEngine.InputSystem.Controls.ButtonControl> button) =>
            Gamepad.current != null && button(Gamepad.current).wasPressedThisFrame;

        public static bool Left => KeyDown(Key.LeftArrow) || KeyDown(Key.A) || Pad(g => g.dpad.left);
        public static bool Right => KeyDown(Key.RightArrow) || KeyDown(Key.D) || Pad(g => g.dpad.right);
        public static bool Up => KeyDown(Key.UpArrow) || Pad(g => g.dpad.up);
        public static bool Down => KeyDown(Key.DownArrow) || Pad(g => g.dpad.down);
        public static bool Confirm => KeyDown(Key.Enter) || KeyDown(Key.NumpadEnter) || Pad(g => g.buttonSouth);
        public static bool Back => KeyDown(Key.Escape) || Pad(g => g.startButton);
        public static bool Restart => KeyDown(Key.R) || Pad(g => g.selectButton);
        public static bool Fullscreen => KeyDown(Key.F11);
#else
        public static bool Left => Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A);
        public static bool Right => Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D);
        public static bool Up => Input.GetKeyDown(KeyCode.UpArrow);
        public static bool Down => Input.GetKeyDown(KeyCode.DownArrow);
        public static bool Confirm => Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.JoystickButton0);
        public static bool Back => Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton7);
        public static bool Restart => Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.JoystickButton6);
        public static bool Fullscreen => Input.GetKeyDown(KeyCode.F11);
#endif
    }

    /// <summary>Переключение полноэкранного режима (кнопка в меню, F11; Alt+Enter Unity обрабатывает сам).</summary>
    public static class DisplaySettings
    {
        public static bool IsFullscreen => Screen.fullScreenMode != FullScreenMode.Windowed;

        public static void ToggleFullscreen()
        {
            if (IsFullscreen)
            {
                Resolution desktop = Screen.currentResolution;
                int w = Mathf.Min(1600, desktop.width - 100);
                int h = Mathf.Min(900, desktop.height - 100);
                Screen.SetResolution(w, h, FullScreenMode.Windowed);
            }
            else
            {
                Resolution desktop = Screen.currentResolution;
                Screen.SetResolution(desktop.width, desktop.height, FullScreenMode.FullScreenWindow);
            }
        }
    }

    /// <summary>Общие помощники IMGUI: масштаб под 1080p, полоски, прямоугольники.</summary>
    public static class GuiKit
    {
        public const float RefHeight = 1080f;
        private static Texture2D _white;

        public static float Scale => Mathf.Max(0.5f, Screen.height / RefHeight);
        public static float Width => Screen.width / Scale;
        public static float Height => Screen.height / Scale;

        public static void BeginScaled() => GUI.matrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1f));

        public static void Rect(Rect r, Color color)
        {
            if (_white == null) _white = Texture2D.whiteTexture;
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, _white);
            GUI.color = prev;
        }

        public static void Bar(Rect r, float fill01, Color color)
        {
            Rect(r, new Color(0f, 0f, 0f, 0.6f));
            Rect(new Rect(r.x, r.y, r.width * Mathf.Clamp01(fill01), r.height), color);
        }

        public static GUIStyle Label(int size, FontStyle style = FontStyle.Bold, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = style, alignment = anchor, richText = true };
            s.normal.textColor = Color.white;
            return s;
        }

        public static GUIStyle Button(int size)
        {
            var s = new GUIStyle(GUI.skin.button) { fontSize = size, fontStyle = FontStyle.Bold };
            return s;
        }
    }
}
