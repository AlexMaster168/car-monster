using UnityEngine;

namespace CarMonster
{
    /// <summary>
    /// Гараж (выбор машины и локации, покупка за кредиты), пауза и экран результатов. IMGUI-прототип:
    /// вся логика в TimeAttackManager/PlayerProfile, так что боевой UI потом просто зовёт те же методы.
    /// </summary>
    public class RaceSetupMenu : MonoBehaviour
    {
        [SerializeField] private TimeAttackManager manager;

        private int _carIndex;
        private bool _paused;
        private string _toast = "";
        private float _toastUntil;
        private GUIStyle _title, _big, _mid, _small, _button, _center;

        private GameCatalogSO Catalog => manager.Catalog;

        private void Start()
        {
            if (manager == null) manager = TimeAttackManager.Instance;
            if (manager != null && manager.Player != null) _carIndex = Mathf.Max(0, Catalog.IndexOfCar(manager.Player.Data));
        }

        private void OnDisable()
        {
            if (_paused) SetPaused(false);
        }

        private void Update()
        {
            if (manager == null) return;
            if (MenuInput.Fullscreen) DisplaySettings.ToggleFullscreen();

            switch (manager.State)
            {
                case RaceState.Setup:
                    if (MenuInput.Left) SelectCar(_carIndex - 1);
                    if (MenuInput.Right) SelectCar(_carIndex + 1);
                    if (MenuInput.Up) SwitchLocation(-1);
                    if (MenuInput.Down) SwitchLocation(1);
                    if (MenuInput.Confirm) RaceOrBuy();
                    break;

                case RaceState.Countdown:
                case RaceState.Racing:
                    if (MenuInput.Back) SetPaused(!_paused);
                    if (_paused && MenuInput.Restart) Restart();
                    break;

                case RaceState.Finished:
                case RaceState.TimeOver:
                    if (MenuInput.Restart) Restart();
                    if (MenuInput.Confirm || MenuInput.Back) manager.OpenGarage();
                    break;
            }
        }

        private void SelectCar(int index)
        {
            int count = Catalog.cars.Length;
            if (count == 0) return;
            _carIndex = (index % count + count) % count;
            manager.SetPreviewCar(Catalog.cars[_carIndex]);
        }

        private void SwitchLocation(int delta)
        {
            LocationSO[] locations = Catalog.locations;
            if (locations.Length < 2) return;
            int current = Mathf.Max(0, Catalog.IndexOfLocation(manager.Location));
            int next = ((current + delta) % locations.Length + locations.Length) % locations.Length;
            manager.OpenGarage(locations[next]);
        }

        private void RaceOrBuy()
        {
            CarDataSO car = manager.Player != null ? manager.Player.Data : null;
            if (car == null) return;
            if (PlayerProfile.IsOwned(car))
            {
                manager.BeginRace();
                return;
            }
            if (PlayerProfile.TryBuy(car)) Toast($"КУПЛЕНО: {car.displayName}!");
            else Toast($"Не хватает {car.price - PlayerProfile.Credits:N0} CR");
        }

        private void Restart()
        {
            SetPaused(false);
            manager.RestartRace();
        }

        private void SetPaused(bool value)
        {
            _paused = value;
            TimeScaleController.SetPaused(value);
            AudioListener.pause = value;
        }

        private void Toast(string text)
        {
            _toast = text;
            _toastUntil = Time.unscaledTime + 2f;
        }

        // ---------- GUI ----------

        private void EnsureStyles()
        {
            if (_title != null) return;
            _title = GuiKit.Label(54);
            _big = GuiKit.Label(36);
            _mid = GuiKit.Label(24);
            _small = GuiKit.Label(18, FontStyle.Normal);
            _button = GuiKit.Button(26);
            _center = GuiKit.Label(56, FontStyle.Bold, TextAnchor.MiddleCenter);
        }

        private void OnGUI()
        {
            if (manager == null) return;
            EnsureStyles();
            GuiKit.BeginScaled();
            float w = GuiKit.Width;
            float h = GuiKit.Height;

            switch (manager.State)
            {
                case RaceState.Setup:
                    DrawGarage(w, h);
                    break;
                case RaceState.Countdown:
                case RaceState.Racing:
                    if (_paused) DrawPause(w, h);
                    break;
                case RaceState.Finished:
                case RaceState.TimeOver:
                    DrawResults(w, h);
                    break;
            }

            if (Time.unscaledTime < _toastUntil)
            {
                GuiKit.Rect(new Rect(w * 0.5f - 300f, h * 0.12f, 600f, 70f), new Color(0f, 0f, 0f, 0.75f));
                GUI.Label(new Rect(w * 0.5f - 300f, h * 0.12f, 600f, 70f), _toast, GuiKit.Label(30, FontStyle.Bold, TextAnchor.MiddleCenter));
            }
        }

        private void DrawGarage(float w, float h)
        {
            CarDataSO car = manager.Player != null ? manager.Player.Data : null;
            var panel = new Rect(40f, 40f, 620f, h - 80f);
            GuiKit.Rect(panel, new Color(0.02f, 0.03f, 0.06f, 0.82f));

            float x = panel.x + 30f;
            float y = panel.y + 20f;
            GUI.Label(new Rect(x, y, 560f, 70f), "CAR MONSTER", _title);
            y += 70f;
            GUI.Label(new Rect(x, y, 560f, 34f), $"<color=#7CFFB2>{PlayerProfile.Credits:N0} CR</color>", _mid);
            y += 50f;

            // Локация.
            LocationSO loc = manager.Location;
            GUI.Label(new Rect(x, y, 560f, 30f), "<color=#9AA4B8>ЛОКАЦИЯ  ↑ ↓</color>", _small);
            y += 26f;
            if (GUI.Button(new Rect(x, y, 50f, 46f), "◄", _button)) SwitchLocation(-1);
            GUI.Label(new Rect(x + 62f, y + 2f, 436f, 46f), loc != null ? loc.displayName.ToUpperInvariant() : "—", _big);
            if (GUI.Button(new Rect(x + 510f, y, 50f, 46f), "►", _button)) SwitchLocation(1);
            y += 52f;
            if (loc != null) GUI.Label(new Rect(x, y, 560f, 50f), loc.description, _small);
            y += 60f;

            // Машина.
            GUI.Label(new Rect(x, y, 560f, 30f), $"<color=#9AA4B8>МАШИНА  ← →   {_carIndex + 1}/{Catalog.cars.Length}</color>", _small);
            y += 26f;
            if (GUI.Button(new Rect(x, y, 50f, 46f), "◄", _button)) SelectCar(_carIndex - 1);
            GUI.Label(new Rect(x + 62f, y + 2f, 436f, 46f), car != null ? car.displayName : "—", _big);
            if (GUI.Button(new Rect(x + 510f, y, 50f, 46f), "►", _button)) SelectCar(_carIndex + 1);
            y += 54f;
            if (car == null) return;

            GUI.Label(new Rect(x, y, 560f, 28f), $"<color=#9AA4B8>{car.inspiredBy}</color>   ·   {car.carClass}   ·   {TierName(car.tier)}", _small);
            y += 30f;
            if (car.hasRamPlow)
            {
                GuiKit.Rect(new Rect(x, y, 560f, 34f), new Color(0.85f, 0.65f, 0.1f, 0.85f));
                GUI.Label(new Rect(x + 10f, y + 3f, 540f, 30f),
                    $"<color=#000000>БРОНЯ + КОВШ: таран ×{car.plowDamageMultiplier:0.0}, лоб −{car.plowFrontalArmor * 100f:0}% урона</color>", _small);
                y += 42f;
            }
            GUI.Label(new Rect(x, y, 560f, 60f), car.description, _small);
            y += 64f;

            Stat(x, ref y, "СКОРОСТЬ", car.statSpeed, $"{car.maxSpeedKmh:0} км/ч");
            Stat(x, ref y, "РАЗГОН", car.statAcceleration, $"{car.acceleration:0} м/с²");
            Stat(x, ref y, "УПРАВЛЕНИЕ", car.statHandling, $"{car.steerRateHighSpeed:0}°/с");
            Stat(x, ref y, "ПРОЧНОСТЬ", car.statToughness, $"{car.durability:0} HP · {car.mass:0} кг");
            y += 10f;

            bool owned = PlayerProfile.IsOwned(car);
            string action = owned ? "ГОНКА  ►  (Enter)" : $"КУПИТЬ  {car.price:N0} CR  (Enter)";
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = owned ? new Color(0.3f, 1f, 0.5f) : (PlayerProfile.Credits >= car.price ? new Color(1f, 0.8f, 0.2f) : new Color(1f, 0.35f, 0.3f));
            if (GUI.Button(new Rect(x, y, 560f, 64f), action, _button)) RaceOrBuy();
            GUI.backgroundColor = prev;
            y += 76f;

            if (GUI.Button(new Rect(x, y, 274f, 44f), DisplaySettings.IsFullscreen ? "Оконный (F11)" : "Полный экран (F11)", GuiKit.Button(20)))
                DisplaySettings.ToggleFullscreen();
            if (GUI.Button(new Rect(x + 286f, y, 274f, 44f), "Выход", GuiKit.Button(20))) Application.Quit();
        }

        private void Stat(float x, ref float y, string label, int value, string detail)
        {
            GUI.Label(new Rect(x, y, 170f, 28f), label, _small);
            GuiKit.Bar(new Rect(x + 170f, y + 7f, 230f, 14f), value / 5f, new Color(0.2f, 0.75f, 1f));
            GUI.Label(new Rect(x + 412f, y, 160f, 28f), detail, _small);
            y += 32f;
        }

        private static string TierName(CarTier tier) => tier switch
        {
            CarTier.Street => "Street",
            CarTier.Race => "Race",
            CarTier.Hyper => "Hyper",
            _ => "<color=#FFC93C>Armored Special</color>"
        };

        private void DrawPause(float w, float h)
        {
            GuiKit.Rect(new Rect(0f, 0f, w, h), new Color(0f, 0f, 0f, 0.55f));
            GUI.Label(new Rect(0f, h * 0.3f, w, 80f), "ПАУЗА", _center);
            float bx = w * 0.5f - 200f;
            float by = h * 0.45f;
            if (GUI.Button(new Rect(bx, by, 400f, 56f), "Продолжить (Esc)", _button)) SetPaused(false);
            if (GUI.Button(new Rect(bx, by + 70f, 400f, 56f), "Рестарт (R)", _button)) Restart();
            if (GUI.Button(new Rect(bx, by + 140f, 400f, 56f), "В гараж", _button))
            {
                SetPaused(false);
                manager.OpenGarage();
            }
            if (GUI.Button(new Rect(bx, by + 210f, 400f, 56f), DisplaySettings.IsFullscreen ? "Оконный (F11)" : "Полный экран (F11)", _button))
                DisplaySettings.ToggleFullscreen();
        }

        private void DrawResults(float w, float h)
        {
            RaceResult r = manager.LastResult;
            var panel = new Rect(w * 0.5f - 360f, h * 0.22f, 720f, 480f);
            GuiKit.Rect(panel, new Color(0.02f, 0.03f, 0.06f, 0.88f));
            GUI.Label(new Rect(panel.x, panel.y + 20f, panel.width, 70f),
                r.finished ? "<color=#7CFFB2>ФИНИШ!</color>" : "<color=#FF6B5A>ВРЕМЯ ВЫШЛО</color>", _center);

            float x = panel.x + 60f;
            float y = panel.y + 120f;
            Row(x, ref y, "Время в заезде", $"{r.elapsed:0.00} с");
            Row(x, ref y, "Чекпоинты", r.checkpoints.ToString());
            Row(x, ref y, "Тейкдауны", r.takedowns.ToString());
            if (r.finished) Row(x, ref y, "Остаток времени", $"{r.timeLeft:0.0} с");
            Row(x, ref y, "Заработано", $"<color=#7CFFB2>+{r.credits:N0} CR</color>");
            Row(x, ref y, "Всего", $"{PlayerProfile.Credits:N0} CR");

            if (GUI.Button(new Rect(x, panel.yMax - 90f, 290f, 60f), "Рестарт (R)", _button)) Restart();
            if (GUI.Button(new Rect(x + 310f, panel.yMax - 90f, 290f, 60f), "Гараж (Enter)", _button)) manager.OpenGarage();
        }

        private void Row(float x, ref float y, string label, string value)
        {
            GUI.Label(new Rect(x, y, 300f, 36f), label, _mid);
            GUI.Label(new Rect(x + 320f, y, 300f, 36f), value, _mid);
            y += 40f;
        }

#if UNITY_EDITOR
        public void EditorSetup(TimeAttackManager raceManager) => manager = raceManager;
#endif
    }
}
