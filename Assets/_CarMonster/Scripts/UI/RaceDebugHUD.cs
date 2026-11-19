using UnityEngine;

namespace CarMonster
{
    /// <summary>
    /// HUD заезда на IMGUI: таймер, круг, скорость/передача, шкала нитро с зелёной зоной, HP, всплывающие бонусы.
    /// Прототип — боевой UI делается на UI Toolkit/uGUI по тем же свойствам.
    /// </summary>
    public class RaceDebugHUD : MonoBehaviour
    {
        private TimeAttackManager _manager;
        private ArcadeCarController _player;
        private NitroSystem _nitro;
        private TakedownSystem _takedown;
        private EngineAudio _engine;
        private GUIStyle _big, _mid, _center;
        private string _popup = "";
        private float _popupUntil;

        private void Start()
        {
            _manager = TimeAttackManager.Instance;
            if (_manager == null) return;
            _manager.TimeBonusAdded += OnBonus;
            _manager.PlayerChanged += Bind;
            TakedownSystem.AnyTakedown += OnAnyTakedown;
            TakedownSystem.AnyReassembled += OnAnyReassembled;
            Bind(_manager.Player);
        }

        private void OnAnyTakedown(TakedownSystem attacker, TakedownSystem victim)
        {
            if (victim == null || _takedown == null) return;
            if (victim == _takedown)
                ShowPopup(attacker != null ? $"WRECKED BY {attacker.Car.Data.displayName.ToUpperInvariant()}" : "CRASHED!");
            else if (attacker != _takedown)
                ShowPopup($"{victim.Car.Data.displayName.ToUpperInvariant()} WRECKED");
        }

        private void OnAnyReassembled(TakedownSystem car)
        {
            if (car == _takedown) ShowPopup("BACK IN THE RACE!");
        }

        private void OnDestroy()
        {
            if (_manager != null)
            {
                _manager.TimeBonusAdded -= OnBonus;
                _manager.PlayerChanged -= Bind;
            }
            TakedownSystem.AnyTakedown -= OnAnyTakedown;
            TakedownSystem.AnyReassembled -= OnAnyReassembled;
            Bind(null);
        }

        private void Bind(ArcadeCarController player)
        {
            if (_nitro != null)
            {
                _nitro.BoostStarted -= OnBoost;
                _nitro.InjectionBotched -= OnBotched;
            }
            _player = player;
            _nitro = player != null ? player.GetComponent<NitroSystem>() : null;
            _takedown = player != null ? player.GetComponent<TakedownSystem>() : null;
            _engine = player != null ? player.GetComponent<EngineAudio>() : null;
            if (_nitro != null)
            {
                _nitro.BoostStarted += OnBoost;
                _nitro.InjectionBotched += OnBotched;
            }
        }

        private void OnBonus(float seconds, TimeBonusReason reason) =>
            ShowPopup($"+{seconds:0.0}s  {reason.ToString().ToUpperInvariant()}");

        private void OnBoost(bool perfect)
        {
            if (perfect) ShowPopup($"PERFECT INJECTION x{_nitro.PerfectChain}");
        }

        private void OnBotched() => ShowPopup("TOO EARLY!");

        private void ShowPopup(string text)
        {
            _popup = text;
            _popupUntil = Time.unscaledTime + 1.4f;
        }

        private void OnGUI()
        {
            if (_manager == null || _player == null || _manager.State == RaceState.Setup) return;
            if (_big == null)
            {
                _big = GuiKit.Label(44);
                _mid = GuiKit.Label(22);
                _center = GuiKit.Label(64, FontStyle.Bold, TextAnchor.MiddleCenter);
            }

            GuiKit.BeginScaled();
            float w = GuiKit.Width;
            float h = GuiKit.Height;

            string timeColor = _manager.TimeLeft < 10f ? "#FF5A46" : "#FFFFFF";
            GUI.Label(new Rect(36, 24, 400, 56), $"<color={timeColor}>{_manager.TimeLeft:00.00}</color>", _big);
            GUI.Label(new Rect(36, 80, 600, 30),
                $"LAP {_manager.CurrentLap}/{_manager.TotalLaps}   TAKEDOWNS {_manager.TakedownCount}", _mid);

            if (_manager.State == RaceState.Countdown)
                GUI.Label(new Rect(0, h * 0.3f, w, 90), Mathf.CeilToInt(_manager.CountdownRemaining).ToString(), _center);

            GUI.Label(new Rect(w - 360, h - 130, 330, 56), $"{_player.SpeedKmh:0} km/h", _big);
            string gear = _engine != null ? $"   G{_engine.Gear}  {_engine.Rpm:0} rpm" : "";
            GUI.Label(new Rect(w - 520, h - 172, 490, 30),
                $"{_player.Data.displayName}{gear}{(_player.IsDrifting ? "   DRIFT" : "")}", _mid);

            if (_nitro != null) DrawNitro(new Rect(36, h - 96, 440, 28));
            if (_takedown != null) GuiKit.Bar(new Rect(36, h - 54, 440, 12), _takedown.Health01, new Color(0.3f, 1f, 0.4f));

            if (_takedown != null && (_takedown.IsWrecked || _takedown.IsReassembling))
            {
                GuiKit.Rect(new Rect(0, h * 0.42f, w, 110), new Color(0f, 0f, 0f, 0.45f));
                string text = _takedown.IsReassembling
                    ? "<color=#7CFFB2>SELF-REPAIR…</color>"
                    : "<color=#FF5A46>WRECKED</color>";
                GUI.Label(new Rect(0, h * 0.42f, w, 80), text, _center);
                if (!_takedown.IsReassembling)
                    GUI.Label(new Rect(0, h * 0.42f + 72, w, 34), "машина соберётся сама и поедет дальше",
                        GuiKit.Label(22, FontStyle.Normal, TextAnchor.MiddleCenter));
            }

            if (Time.unscaledTime < _popupUntil)
                GUI.Label(new Rect(0, h * 0.18f, w, 70), _popup, _center);
        }

        private void DrawNitro(Rect r)
        {
            float barWidth = (r.width - 8f) / NitroSystem.BarCount;
            float perBar = _nitro.Capacity / NitroSystem.BarCount;
            for (int i = 0; i < NitroSystem.BarCount; i++)
            {
                float fill = Mathf.Clamp01((_nitro.Charge - perBar * i) / perBar);
                GuiKit.Bar(new Rect(r.x + i * (barWidth + 4f), r.y, barWidth, r.height), fill, new Color(0.2f, 0.7f, 1f));
            }

            if (!_nitro.IsBoosting) return;
            var inj = new Rect(r.x, r.y - 20f, r.width, 11f);
            GuiKit.Rect(inj, new Color(0f, 0f, 0f, 0.6f));
            GuiKit.Rect(new Rect(inj.x + inj.width * _nitro.GreenZoneStart, inj.y,
                inj.width * (_nitro.GreenZoneEnd - _nitro.GreenZoneStart), inj.height), new Color(0.2f, 1f, 0.3f, 0.9f));
            GuiKit.Rect(new Rect(inj.x + inj.width * _nitro.InjectionCursor01 - 2f, inj.y - 3f, 4f, inj.height + 6f),
                _nitro.IsPerfectBoost ? new Color(1f, 0.5f, 0f) : Color.white);
        }
    }
}
