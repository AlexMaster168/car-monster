using UnityEngine;

namespace CarMonster
{
    /// <summary>
    /// Выбор игрока, переживающий загрузку сцены: какая машина, какие боты, стартовать ли сразу.
    /// </summary>
    public static class RaceSession
    {
        /// <summary>Код шасси игрока. null — последняя выбранная в гараже.</summary>
        public static string PlayerCarCode;
        /// <summary>Коды ботов. null — случайный набор из каталога.</summary>
        public static string[] BotCarCodes;
        /// <summary>true — пропустить гараж и сразу начать отсчёт (рестарт, тесты).</summary>
        public static bool AutoStart;
        /// <summary>Отключить экономику (тесты): не начислять кредиты.</summary>
        public static bool DisableRewards;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            PlayerCarCode = null;
            BotCarCodes = null;
            AutoStart = false;
            DisableRewards = false;
        }
    }

    /// <summary>Итог заезда для экрана результатов и начисления кредитов.</summary>
    public struct RaceResult
    {
        public bool finished;
        public int checkpoints;
        public int takedowns;
        public float timeLeft;
        public float elapsed;
        public int credits;
    }

    public static class RaceRewards
    {
        public static int Calculate(in RaceResult r, GameBalanceSO.EconomyTuning e)
        {
            int credits = r.checkpoints * e.creditsPerCheckpoint + r.takedowns * e.creditsPerTakedown;
            if (r.finished) credits += e.finishBonus + Mathf.RoundToInt(r.timeLeft) * e.creditsPerSecondLeft;
            else credits += e.consolationCredits;
            return Mathf.Max(0, credits);
        }
    }

    /// <summary>Кредиты и купленные машины (PlayerPrefs).</summary>
    public static class PlayerProfile
    {
        private const string CreditsKey = "cm_credits";
        private const string InitKey = "cm_profile_init";
        private const string OwnedPrefix = "cm_owned_";
        private const string LastCarKey = "cm_last_car";

        public static void EnsureInitialized(GameBalanceSO balance)
        {
            if (PlayerPrefs.GetInt(InitKey, 0) == 1) return;
            PlayerPrefs.SetInt(InitKey, 1);
            PlayerPrefs.SetInt(CreditsKey, balance.economy.startingCredits);
            PlayerPrefs.Save();
        }

        public static int Credits => PlayerPrefs.GetInt(CreditsKey, 0);

        public static void AddCredits(int amount)
        {
            if (amount == 0) return;
            PlayerPrefs.SetInt(CreditsKey, Mathf.Max(0, Credits + amount));
            PlayerPrefs.Save();
        }

        public static bool IsOwned(CarDataSO car) =>
            car != null && (car.price <= 0 || PlayerPrefs.GetInt(OwnedPrefix + car.chassisCode, 0) == 1);

        public static bool TryBuy(CarDataSO car)
        {
            if (car == null || IsOwned(car) || Credits < car.price) return false;
            PlayerPrefs.SetInt(CreditsKey, Credits - car.price);
            PlayerPrefs.SetInt(OwnedPrefix + car.chassisCode, 1);
            PlayerPrefs.Save();
            return true;
        }

        public static string LastCarCode
        {
            get => PlayerPrefs.GetString(LastCarKey, "");
            set
            {
                PlayerPrefs.SetString(LastCarKey, value ?? "");
                PlayerPrefs.Save();
            }
        }

        public static void ResetProgress()
        {
            PlayerPrefs.DeleteKey(InitKey);
            PlayerPrefs.DeleteKey(CreditsKey);
            PlayerPrefs.DeleteKey(LastCarKey);
            PlayerPrefs.Save();
        }

        public static void ResetOwnership(GameCatalogSO catalog)
        {
            foreach (CarDataSO car in catalog.cars)
                if (car != null) PlayerPrefs.DeleteKey(OwnedPrefix + car.chassisCode);
            PlayerPrefs.Save();
        }
    }
}
