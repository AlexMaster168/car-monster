using UnityEngine;

namespace CarMonster
{
    /// <summary>
    /// Отладочный HUD на IMGUI: таймер, круг, скорость, шкала нитро с зелёной зоной, HP, всплывающие бонусы.
    /// Нужен, чтобы играть сразу; боевой UI потом делается на UI Toolkit/uGUI по тем же свойствам.
    /// </summary>
    public class RaceDebugHUD : MonoBehaviour
    {
        [SerializeField] private ArcadeCarController player;

        private NitroSystem _nitro;
        private TakedownSystem _takedown;
        private GUIStyle _big;
        private GUIStyle _mid;
        private GUIStyle _center;
        private Texture2D _white;
        private string _popup = "";
        private float _popupUntil;
        private TimeAttackManager _manager;

        private void Start()
        {
            _manager = TimeAttackManager.Instance;
            if (player == null && _manager != null) player = _manager.Player;
            if (player != null)
            {
                _nitro = player.GetComponent<NitroSystem>();
                _takedown = player.GetComponent<TakedownSystem>();
            }
            if (_manager != null) _manager.TimeBonusAdded += OnBonus;
            if (_nitro != null)
            {
                _nitro.BoostStarted += OnBoost;
                _nitro.InjectionBotched += OnBotched;
            }
        }

        private void OnDestroy()
        {
            if (_manager != null) _manager.TimeBonusAdded -= OnBonus;
            if (_nitro != null)
            {
                _nitro.BoostStarted -= OnBoost;
                _nitro.InjectionBotched -= OnBotched;
            }
        }

        private void OnBonus(float seconds, TimeBonusReason reason) => ShowPopup($"+{seconds:0.0}s  {reason.ToString().ToUpperInvariant()}");

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

        private void EnsureStyles()
        {
            if (_big != null) return;
            _white = Texture2D.whiteTexture;
            _big = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold };
            _big.normal.textColor = Color.white;
            _mid = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            _mid.normal.textColor = Color.white;
            _center = new GUIStyle(_big) { alignment = TextAnchor.MiddleCenter, fontSize = 56 };
        }

        private void OnGUI()
        {
            if (player == null) return;
            EnsureStyles();

            if (_manager != null)
            {
                Color timeColor = _manager.TimeLeft < 10f ? new Color(1f, 0.3f, 0.2f) : Color.white;
                _big.normal.textColor = timeColor;
                GUI.Label(new Rect(30, 20, 400, 50), $"{_manager.TimeLeft:00.00}", _big);
                _big.normal.textColor = Color.white;
                GUI.Label(new Rect(30, 70, 400, 30),
                    $"LAP {_manager.CurrentLap}/{_manager.TotalLaps}   TAKEDOWNS {_manager.TakedownCount}", _mid);

                string center = _manager.State switch
                {
                    RaceState.Countdown => Mathf.CeilToInt(_manager.CountdownRemaining).ToString(),
                    RaceState.Finished => $"FINISH! {_manager.ElapsedTime:0.00}s",
                    RaceState.TimeOver => "TIME OVER",
                    _ => null
                };
                if (center != null) GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, 80), center, _center);
            }

            GUI.Label(new Rect(Screen.width - 330, Screen.height - 120, 300, 50),
                $"{player.SpeedKmh:0} km/h", _big);
            GUI.Label(new Rect(Screen.width - 330, Screen.height - 160, 300, 30),
                player.Data.displayName + (player.IsDrifting ? "   DRIFT" : ""), _mid);

            if (_nitro != null) DrawNitro(new Rect(30, Screen.height - 90, 420, 26));
            if (_takedown != null) DrawBar(new Rect(30, Screen.height - 50, 420, 12), _takedown.Health01, new Color(0.3f, 1f, 0.4f));

            if (Time.unscaledTime < _popupUntil)
                GUI.Label(new Rect(0, Screen.height * 0.18f, Screen.width, 60), _popup, _center);
        }

        private void DrawNitro(Rect r)
        {
            float barWidth = (r.width - 8f) / NitroSystem.BarCount;
            float perBar = _nitro.Capacity / NitroSystem.BarCount;
            for (int i = 0; i < NitroSystem.BarCount; i++)
            {
                float fill = Mathf.Clamp01((_nitro.Charge - perBar * i) / perBar);
                DrawBar(new Rect(r.x + i * (barWidth + 4f), r.y, barWidth, r.height), fill, new Color(0.2f, 0.7f, 1f));
            }

            if (!_nitro.IsBoosting) return;
            Rect inj = new Rect(r.x, r.y - 18f, r.width, 10f);
            DrawRect(inj, new Color(0f, 0f, 0f, 0.6f));
            DrawRect(new Rect(inj.x + inj.width * _nitro.GreenZoneStart, inj.y,
                inj.width * (_nitro.GreenZoneEnd - _nitro.GreenZoneStart), inj.height), new Color(0.2f, 1f, 0.3f, 0.9f));
            DrawRect(new Rect(inj.x + inj.width * _nitro.InjectionCursor01 - 2f, inj.y - 3f, 4f, inj.height + 6f),
                _nitro.IsPerfectBoost ? new Color(1f, 0.5f, 0f) : Color.white);
        }

        private void DrawBar(Rect r, float fill01, Color color)
        {
            DrawRect(r, new Color(0f, 0f, 0f, 0.6f));
            DrawRect(new Rect(r.x, r.y, r.width * fill01, r.height), color);
        }

        private void DrawRect(Rect r, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, _white);
            GUI.color = prev;
        }
    }
}
