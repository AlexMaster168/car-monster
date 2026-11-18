using UnityEngine;

namespace CarMonster
{
    /// <summary>Каталог игры: все машины гаража и все локации.</summary>
    [CreateAssetMenu(fileName = "GameCatalog", menuName = "Car Monster/Game Catalog", order = 3)]
    public class GameCatalogSO : ScriptableObject
    {
        public CarDataSO[] cars = new CarDataSO[0];
        public LocationSO[] locations = new LocationSO[0];

        public CarDataSO FindCar(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            for (int i = 0; i < cars.Length; i++)
                if (cars[i] != null && cars[i].chassisCode == code) return cars[i];
            return null;
        }

        public int IndexOfCar(CarDataSO car)
        {
            for (int i = 0; i < cars.Length; i++)
                if (cars[i] == car) return i;
            return -1;
        }

        public int IndexOfLocation(LocationSO location)
        {
            for (int i = 0; i < locations.Length; i++)
                if (locations[i] == location) return i;
            return -1;
        }
    }
}
