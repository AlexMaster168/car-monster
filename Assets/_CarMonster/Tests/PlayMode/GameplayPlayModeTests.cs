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
    /// Интеграционные тесты на реальных сценах локаций: физика, ИИ, нитро, тейкдауны, ковш, звук, музыка, гараж.
    /// </summary>
    public class GameplayPlayModeTests
    {
        private const string MainScene = "Loc_NeonCity";
        private static readonly string[] DefaultBots = { "PLAID-EV", "SF90-V8", "HRC-V10", "CYB-TRK", "Z34-RZ" };

        private TimeAttackManager _manager;
        private ArcadeCarController _player;
        private ScriptedInput _input;

        private IEnumerator LoadRace(string playerCode, string scene = MainScene, bool autoStart = true)
        {
            RaceSession.AutoStart = autoStart;
            RaceSession.PlayerCarCode = playerCode;
            RaceSession.BotCarCodes = DefaultBots;
            RaceSession.DisableRewards = true;
            SceneManager.LoadScene(scene);
            yield return null;
            yield return null;

            _manager = TimeAttackManager.Instance;
            Assert.IsNotNull(_manager, $"TimeAttackManager не найден в сцене {scene}");
            _player = _manager.Player;
            Assert.IsNotNull(_player, "Машина игрока не заспавнилась");
            if (!autoStart) yield break;

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
            AudioListener.pause = false;
            RaceSession.AutoStart = false;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Player_Accelerates_Bots_Follow_And_Engine_Shifts()
        {
            yield return LoadRace("R35-GT");
            Assert.AreEqual(6, _manager.Racers.Count, "Игрок + 5 ботов");

            Vector3 start = _player.Body.position;
            _input.Set(1f, 0f, false);
            yield return WaitSeconds(5f);

            float travelled = Vector3.Distance(start, _player.Body.position);
            var engine = _player.GetComponent<EngineAudio>();
            Debug.Log($"[Test] speed {_player.SpeedKmh:0} km/h, travelled {travelled:0} m, gear {engine.Gear}, rpm {engine.Rpm:0}");
            Assert.Greater(_player.SpeedKmh, 120f);
            Assert.Greater(travelled, 150f);
            Assert.Greater(Vector3.Dot(_player.transform.up, Vector3.up), 0.9f, "Машина не должна перевернуться");
            Assert.GreaterOrEqual(engine.Gear, 3, "Коробка должна переключаться вверх");
            Assert.That(engine.Rpm, Is.InRange(_player.Data.engineSound.idleRpm, _player.Data.engineSound.redlineRpm + 1f));

            foreach (ArcadeCarController racer in _manager.Racers)
            {
                if (racer == _player) continue;
                var ai = racer.GetComponent<RacerAI>();
                Debug.Log($"[Test] {racer.name} progress {ai.TotalDistance:0} m");
                Assert.Greater(ai.TotalDistance, 100f, $"{racer.name} должен ехать по трассе");
            }
        }

        [UnityTest]
        public IEnumerator Nitro_PerfectInjection_In_GreenZone()
        {
            yield return LoadRace("R35-GT");
            var nitro = _player.GetComponent<NitroSystem>();
            nitro.ResetCharge(nitro.Capacity);

            _input.Set(1f, 0f, false);
            _input.TapNitro();
            yield return null;
            yield return null;
            Assert.IsTrue(nitro.IsBoosting);
            Assert.AreEqual(2, nitro.FilledBars);

            float timeout = 5f;
            while (!nitro.InGreenZone && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
            _input.TapNitro();
            yield return null;
            yield return null;
            Assert.IsTrue(nitro.IsPerfectBoost, "Нажатие в зелёной зоне = идеальный впрыск");
            Assert.AreEqual(1, nitro.FilledBars);

            nitro.ResetCharge(nitro.Capacity);
            _input.TapNitro();
            yield return null;
            yield return null;
            _input.TapNitro(); // сразу — слишком рано
            yield return null;
            yield return null;
            Assert.IsFalse(nitro.InGreenZone, "Раннее нажатие срывает впрыск");
            Assert.AreEqual(2, nitro.FilledBars, "Раннее нажатие не тратит заряд");
        }

        [UnityTest]
        public IEnumerator Ram_With_Nitro_Knocks_Out_Bot_Gives_Time_And_Respawns()
        {
            yield return LoadRace("R35-GT");
            ArcadeCarController victim = ParkBotsAndPlaceTarget("HRC-V10", 60f);
            var victimTd = victim.GetComponent<TakedownSystem>();

            float bonus = 0f;
            void OnBonus(float s, TimeBonusReason r)
            {
                if (r == TimeBonusReason.Takedown) bonus += s;
            }
            _manager.TimeBonusAdded += OnBonus;

            var nitro = _player.GetComponent<NitroSystem>();
            nitro.ResetCharge(nitro.Capacity);
            _input.Set(1f, 0f, false);
            yield return WaitSeconds(0.6f);
            _input.TapNitro();

            bool slowMo = false;
            float timeout = 8f;
            while (!victimTd.IsWrecked && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }
            for (int i = 0; i < 5; i++)
            {
                slowMo |= TimeScaleController.IsSlowMo;
                yield return null;
            }
            _manager.TimeBonusAdded -= OnBonus;

            Assert.IsTrue(victimTd.IsWrecked, "Таран с нитро в борт должен выбить машину");
            Assert.AreEqual(_player.GetComponent<TakedownSystem>(), victimTd.LastAttacker);
            Assert.IsTrue(slowMo, "Тейкдаун включает слоу-мо");
            Assert.AreEqual(1, _manager.TakedownCount);
            Assert.AreEqual(_manager.Balance.combat.takedownTimeBonus, bonus, 0.001f);

            yield return WaitSeconds(_manager.Balance.timeAttack.respawnDelay + 0.5f);
            Assert.IsFalse(victimTd.IsWrecked, "Через 3 секунды машина возвращается");
            Assert.AreEqual(1f, Time.timeScale, 0.001f);
        }

        [UnityTest]
        public IEnumerator Armored_Plow_Ram_Wrecks_Without_Nitro()
        {
            yield return LoadRace("KNT-XV");
            Assert.IsTrue(_player.Data.hasRamPlow);
            ArcadeCarController victim = ParkBotsAndPlaceTarget("SF90-V8", 45f);
            var victimTd = victim.GetComponent<TakedownSystem>();

            _input.Set(1f, 0f, false); // без нитро
            float timeout = 10f;
            while (!victimTd.IsWrecked && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }
            Debug.Log($"[Test] plow ram: wrecked {victimTd.IsWrecked}, player speed {_player.SpeedKmh:0}");
            Assert.IsTrue(victimTd.IsWrecked, "Ковш бронемашины выбивает соперника даже без нитро");
        }

        [UnityTest]
        public IEnumerator Armored_Plow_Absorbs_Frontal_Hits()
        {
            yield return LoadRace("BLW-CUL");
            var td = _player.GetComponent<TakedownSystem>();
            Transform t = _player.transform;

            float before = td.Health;
            td.ReceiveHit(null, 100f, t.position + t.forward * 3f, -t.forward, true); // удар спереди, в ковш
            float frontal = before - td.Health;

            before = td.Health;
            td.ReceiveHit(null, 100f, t.position + t.right * 2f, -t.right, true); // в борт
            float side = before - td.Health;

            Debug.Log($"[Test] frontal {frontal:0.0} vs side {side:0.0}");
            Assert.Less(frontal, side * 0.5f, "Ковш должен поглощать фронтальные удары");
        }

        [UnityTest]
        public IEnumerator Garage_Preview_Swaps_Car_And_Starts_Race()
        {
            yield return LoadRace("R35-GT", MainScene, false);
            Assert.AreEqual(RaceState.Setup, _manager.State);
            Assert.AreEqual(1, _manager.Racers.Count, "В гараже только машина игрока");

            _manager.SetPreviewCar(_manager.Catalog.FindCar("G66-ARM"));
            yield return null;
            yield return null;
            Assert.AreEqual("G66-ARM", _manager.Player.Data.chassisCode);
            int wheels = 0;
            foreach (Transform child in _manager.Player.transform)
                if (child.name.StartsWith("WheelAnchor_")) wheels++;
            Assert.AreEqual(6, wheels, "Mammoth 6x6 — шесть колёс");
            Assert.IsNotNull(_manager.Player.transform.Find("RamPlow"), "У бронемашины есть ковш");

            _manager.BeginRace();
            yield return null;
            Assert.AreEqual(RaceState.Countdown, _manager.State);
            Assert.AreEqual(6, _manager.Racers.Count);
        }

        [UnityTest]
        public IEnumerator All_Locations_Load_With_Music()
        {
            string[] scenes = { "Loc_NeonCity", "Loc_DesertCanyon", "Loc_ArcticPort" };
            foreach (string scene in scenes)
            {
                yield return LoadRace("R35-GT", scene, false);
                var music = Object.FindFirstObjectByType<MusicPlayer>();
                Assert.IsNotNull(music, $"{scene}: нет MusicPlayer");

                float timeout = 20f;
                while (!music.IsReady && timeout > 0f)
                {
                    timeout -= Time.unscaledDeltaTime;
                    yield return null;
                }
                Assert.IsTrue(music.IsReady, $"{scene}: трек не сгенерировался");
                Debug.Log($"[Test] {scene}: {music.Location.musicStyle.genre} track {music.CurrentClip.length:0.0}s");
                Assert.Greater(music.CurrentClip.length, 50f);
            }
        }

        [Test]
        public void Rewards_Are_Calculated_From_Result()
        {
            var e = new GameBalanceSO.EconomyTuning();
            var finished = new RaceResult { finished = true, checkpoints = 15, takedowns = 2, timeLeft = 12.4f };
            Assert.AreEqual(15 * e.creditsPerCheckpoint + 2 * e.creditsPerTakedown + e.finishBonus + 12 * e.creditsPerSecondLeft,
                RaceRewards.Calculate(finished, e));

            var failed = new RaceResult { finished = false, checkpoints = 3, takedowns = 0 };
            Assert.AreEqual(3 * e.creditsPerCheckpoint + e.consolationCredits, RaceRewards.Calculate(failed, e));
        }

        // ---------- helpers ----------

        /// <summary>Убирает ботов с трассы, а один ставит неподвижной мишенью бортом к игроку.</summary>
        private ArcadeCarController ParkBotsAndPlaceTarget(string targetCode, float distance)
        {
            Transform p = _player.transform;
            ArcadeCarController target = null;
            int parked = 0;
            foreach (ArcadeCarController racer in _manager.Racers)
            {
                if (racer == _player) continue;
                racer.SetControlEnabled(false);
                if (racer.Data.chassisCode == targetCode && target == null)
                {
                    target = racer;
                    continue;
                }
                racer.Teleport(new Vector3(600f + 20f * parked++, 1f, 0f), Quaternion.identity);
            }
            Assert.IsNotNull(target, $"Бот {targetCode} не найден");
            target.Teleport(p.position + p.forward * distance + Vector3.up * 0.3f, p.rotation * Quaternion.Euler(0f, 90f, 0f));
            return target;
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
