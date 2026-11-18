using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CarMonster.Tests
{
    /// <summary>Скриптовый ввод для тестов.</summary>
    public class ScriptedInput : CarInputSource
    {
        public void Set(float throttle, float steer, bool drift)
        {
            Throttle = throttle;
            Steer = steer;
            Drift = drift;
        }

        public void TapNitro() => PressNitro();
    }

    /// <summary>
    /// Интеграционные тесты на демо-сцене: реальная физика, ИИ, нитро, тейкдаун и респавн.
    /// </summary>
    public class GameplayPlayModeTests
    {
        private const string SceneName = "Demo_TimeAttack";

        private TimeAttackManager _manager;
        private ArcadeCarController _player;
        private ScriptedInput _input;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            _manager = TimeAttackManager.Instance;
            Assert.IsNotNull(_manager, "TimeAttackManager не найден в демо-сцене");
            _player = _manager.Player;
            _input = _player.gameObject.AddComponent<ScriptedInput>();
            _player.SetInputSource(_input);

            yield return WaitSeconds(_manager.Balance.timeAttack.countdownDuration + 0.3f);
            Assert.AreEqual(RaceState.Racing, _manager.State);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Player_Accelerates_And_Bots_Follow_Track()
        {
            Vector3 start = _player.Body.position;
            _input.Set(1f, 0f, false);
            yield return WaitSeconds(5f);

            float travelled = Vector3.Distance(start, _player.Body.position);
            Debug.Log($"[Test] speed {_player.SpeedKmh:0} km/h, travelled {travelled:0} m, grounded {_player.IsGrounded}");
            Assert.Greater(_player.SpeedKmh, 120f, "Машина должна разогнаться за 5 секунд");
            Assert.Greater(travelled, 150f);
            Assert.Greater(Vector3.Dot(_player.transform.up, Vector3.up), 0.9f, "Машина не должна перевернуться");

            foreach (RacerAI ai in Object.FindObjectsByType<RacerAI>(FindObjectsSortMode.None))
            {
                Debug.Log($"[Test] {ai.name} progress {ai.TotalDistance:0} m");
                Assert.Greater(ai.TotalDistance, 100f, $"{ai.name} должен ехать по трассе");
            }
            Assert.Less(_manager.TimeLeft, _manager.Balance.timeAttack.startTime, "Таймер должен идти");
        }

        [UnityTest]
        public IEnumerator Nitro_PerfectInjection_In_GreenZone()
        {
            var nitro = _player.GetComponent<NitroSystem>();
            nitro.ResetCharge(nitro.Capacity);
            Assert.AreEqual(NitroSystem.BarCount, nitro.FilledBars);

            _input.Set(1f, 0f, false);
            _input.TapNitro();
            yield return null;
            yield return null;
            Assert.IsTrue(nitro.IsBoosting);
            Assert.IsFalse(nitro.IsPerfectBoost);
            Assert.AreEqual(2, nitro.FilledBars);

            float timeout = 5f;
            while (!nitro.InGreenZone && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
            Assert.IsTrue(nitro.InGreenZone, "Курсор впрыска должен дойти до зелёной зоны");
            _input.TapNitro();
            yield return null;
            yield return null;

            Assert.IsTrue(nitro.IsPerfectBoost, "Нажатие в зелёной зоне = идеальный впрыск");
            Assert.AreEqual(1, nitro.PerfectChain);
            Assert.AreEqual(1, nitro.FilledBars);
            Debug.Log($"[Test] perfect boost speed {_player.SpeedKmh:0} km/h");
        }

        [UnityTest]
        public IEnumerator Nitro_EarlyPress_Botches_Injection()
        {
            var nitro = _player.GetComponent<NitroSystem>();
            nitro.ResetCharge(nitro.Capacity);
            _input.Set(1f, 0f, false);
            _input.TapNitro();
            yield return null;
            yield return null;
            _input.TapNitro(); // сразу — слишком рано
            yield return null;
            yield return null;
            Assert.IsFalse(nitro.InGreenZone, "После раннего нажатия зелёная зона закрыта");
            Assert.IsFalse(nitro.IsPerfectBoost);
            Assert.AreEqual(2, nitro.FilledBars, "Раннее нажатие не тратит заряд");
        }

        [UnityTest]
        public IEnumerator Ram_With_Nitro_Knocks_Out_Bot_Gives_Time_And_Respawns()
        {
            RacerAI bot = GameObject.Find("AI_HRC-V10").GetComponent<RacerAI>();
            float bonusFromTakedown = 0f;
            void OnBonus(float seconds, TimeBonusReason reason)
            {
                if (reason == TimeBonusReason.Takedown) bonusFromTakedown += seconds;
            }
            _manager.TimeBonusAdded += OnBonus;
            ArcadeCarController victim = bot.GetComponent<ArcadeCarController>();
            TakedownSystem victimTd = bot.GetComponent<TakedownSystem>();

            // Остальных ботов убираем далеко в сторону, чтобы мишень не сбил кто-то другой.
            Transform p = _player.transform;
            int parked = 0;
            foreach (RacerAI other in Object.FindObjectsByType<RacerAI>(FindObjectsSortMode.None))
            {
                if (other == bot) continue;
                var car = other.GetComponent<ArcadeCarController>();
                car.SetControlEnabled(false);
                car.Teleport(new Vector3(500f + 20f * parked++, 1f, 0f), Quaternion.identity);
            }

            // Ставим бота неподвижной мишенью в 60 м прямо по курсу игрока, бортом к нему.
            victim.SetControlEnabled(false);
            victim.Teleport(p.position + p.forward * 60f + Vector3.up * 0.3f, p.rotation * Quaternion.Euler(0f, 90f, 0f));

            bool takedown = false;
            bool slowMo = false;
            void OnTakedown(TakedownSystem a, TakedownSystem v)
            {
                if (v != victimTd) return;
                takedown = true;
                Debug.Log($"[Test] takedown by {(a != null ? a.name : "null")}");
            }
            TakedownSystem.AnyTakedown += OnTakedown;

            float timeBefore = _manager.TimeLeft;
            var nitro = _player.GetComponent<NitroSystem>();
            nitro.ResetCharge(nitro.Capacity);
            _input.Set(1f, 0f, false);
            yield return WaitSeconds(0.6f);
            _input.TapNitro();

            float timeout = 8f;
            while (!takedown && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }
            for (int i = 0; i < 5; i++)
            {
                slowMo |= TimeScaleController.IsSlowMo;
                yield return null;
            }
            TakedownSystem.AnyTakedown -= OnTakedown;

            Assert.IsTrue(takedown, "Таран с нитро в борт должен выбить машину");
            Assert.IsTrue(victimTd.IsWrecked);
            Assert.IsTrue(slowMo, "Тейкдаун включает слоу-мо");
            Assert.AreEqual(1, _manager.TakedownCount);
            int detached = 0;
            foreach (DetachablePart part in Object.FindObjectsByType<DetachablePart>(FindObjectsSortMode.None))
                if (part.IsDetached) detached++;
            Assert.Greater(detached, 0, "Детали кузова должны отлететь");
            Debug.Log($"[Test] takedown OK, detached parts {detached}, time {timeBefore:0.0} -> {_manager.TimeLeft:0.0}");

            _manager.TimeBonusAdded -= OnBonus;
            Assert.AreEqual(_manager.Balance.combat.takedownTimeBonus, bonusFromTakedown, 0.001f, "За тейкдаун +5 сек");

            yield return WaitSeconds(_manager.Balance.timeAttack.respawnDelay + 0.5f);
            Assert.IsFalse(victimTd.IsWrecked, "Через 3 секунды машина должна вернуться на трассу");
            Assert.AreEqual(victimTd.MaxHealth, victimTd.Health);
            Assert.IsFalse(TimeScaleController.IsSlowMo);
            Assert.AreEqual(1f, Time.timeScale, 0.001f);
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }
    }
}
