using UnityEditor;
using UnityEngine;

namespace CarMonster.EditorTools
{
    /// <summary>Три локации: палитра, форма трассы и процедурный трек под атмосферу.</summary>
    public static class LocationPresets
    {
        public static readonly string[] All = { "NeonCity", "DesertCanyon", "ArcticPort" };

        public static string ScenePath(string id) => $"{CarRoster.Root}/Scenes/Loc_{id}.unity";
        public static string SceneName(string id) => $"Loc_{id}";

        public static LocationSO[] CreateOrUpdateAll()
        {
            CarRoster.EnsureFolder(CarRoster.LocationsFolder);
            var result = new LocationSO[All.Length];
            for (int i = 0; i < All.Length; i++)
            {
                string id = All[i];
                string path = $"{CarRoster.LocationsFolder}/Location_{id}.asset";
                var loc = AssetDatabase.LoadAssetAtPath<LocationSO>(path);
                bool isNew = loc == null;
                if (isNew) loc = ScriptableObject.CreateInstance<LocationSO>();

                AudioClip[] keepClips = loc.musicClips; // свои треки пользователя не трогаем
                Apply(loc, id);
                loc.musicClips = keepClips ?? new AudioClip[0];
                loc.sceneName = SceneName(id);

                if (isNew) AssetDatabase.CreateAsset(loc, path);
                EditorUtility.SetDirty(loc);
                result[i] = loc;
            }
            return result;
        }

        private static void Apply(LocationSO l, string id)
        {
            switch (id)
            {
                case "NeonCity":
                    l.displayName = "Neon City";
                    l.description = "Ночной мегаполис: стадион-трасса между неоновыми башнями и S-связка на обратной прямой.";
                    l.decor = DecorStyle.CityTowers;
                    l.straightLength = 520f;
                    l.cornerRadius = 140f;
                    l.sBendAmplitude = 38f;
                    l.sBendWaves = 1f;
                    l.groundColor = new Color(0.1f, 0.1f, 0.16f);
                    l.roadColor = new Color(0.09f, 0.09f, 0.12f);
                    l.decorColor = new Color(0.16f, 0.15f, 0.26f);
                    l.glowColor = new Color(1f, 0.2f, 0.85f);
                    l.fogColor = new Color(0.32f, 0.16f, 0.42f);
                    l.fogStart = 180f;
                    l.fogEnd = 750f;
                    l.sunEuler = new Vector3(12f, -60f, 0f);
                    l.sunColor = new Color(1f, 0.55f, 0.65f);
                    l.sunIntensity = 1.1f;
                    l.skyTint = new Color(0.55f, 0.25f, 0.75f);
                    l.groundHorizon = new Color(0.2f, 0.1f, 0.25f);
                    l.atmosphere = 2.2f;
                    l.skyExposure = 0.9f;
                    l.solidSky = true;
                    l.solidSkyColor = new Color(0.08f, 0.04f, 0.14f);
                    l.fogColor = new Color(0.16f, 0.08f, 0.24f);
                    l.musicStyle = new MusicStyle
                    {
                        genre = MusicGenre.Synthwave, bpm = 116f, rootMidi = 45, scale = ScaleType.Minor,
                        progression = new[] { 0, 5, 2, 6 }, bars = 32, seed = 11, swing = 0f
                    };
                    break;

                case "DesertCanyon":
                    l.displayName = "Desert Canyon";
                    l.description = "Раскалённый каньон: длинные прямые на максималку и волнистая обратная прямая между столовых гор.";
                    l.decor = DecorStyle.DesertMesas;
                    l.straightLength = 640f;
                    l.cornerRadius = 170f;
                    l.sBendAmplitude = 55f;
                    l.sBendWaves = 1.5f;
                    l.groundColor = new Color(0.78f, 0.6f, 0.38f);
                    l.roadColor = new Color(0.2f, 0.18f, 0.17f);
                    l.decorColor = new Color(0.62f, 0.32f, 0.17f);
                    l.glowColor = new Color(1f, 0.55f, 0.1f);
                    l.fogColor = new Color(0.93f, 0.78f, 0.58f);
                    l.fogStart = 300f;
                    l.fogEnd = 1300f;
                    l.sunEuler = new Vector3(55f, 30f, 0f);
                    l.sunColor = new Color(1f, 0.92f, 0.78f);
                    l.sunIntensity = 1.6f;
                    l.skyTint = new Color(0.55f, 0.5f, 0.42f);
                    l.groundHorizon = new Color(0.7f, 0.55f, 0.4f);
                    l.atmosphere = 1.4f;
                    l.skyExposure = 1.4f;
                    l.solidSky = false;
                    l.musicStyle = new MusicStyle
                    {
                        genre = MusicGenre.Phonk, bpm = 130f, rootMidi = 42, scale = ScaleType.Phrygian,
                        progression = new[] { 0, 0, 5, 6 }, bars = 32, seed = 23, swing = 0.12f
                    };
                    break;

                default: // ArcticPort
                    l.displayName = "Arctic Port";
                    l.description = "Ледяной порт: короткая техничная трасса среди контейнерных стен, две S-связки подряд.";
                    l.decor = DecorStyle.PortContainers;
                    l.straightLength = 430f;
                    l.cornerRadius = 115f;
                    l.sBendAmplitude = 32f;
                    l.sBendWaves = 2f;
                    l.groundColor = new Color(0.86f, 0.9f, 0.95f);
                    l.roadColor = new Color(0.16f, 0.17f, 0.2f);
                    l.decorColor = new Color(0.75f, 0.2f, 0.15f);
                    l.glowColor = new Color(0.4f, 0.85f, 1f);
                    l.fogColor = new Color(0.74f, 0.82f, 0.9f);
                    l.fogStart = 120f;
                    l.fogEnd = 650f;
                    l.sunEuler = new Vector3(18f, 140f, 0f);
                    l.sunColor = new Color(0.8f, 0.88f, 1f);
                    l.sunIntensity = 1.2f;
                    l.skyTint = new Color(0.45f, 0.55f, 0.7f);
                    l.groundHorizon = new Color(0.6f, 0.66f, 0.72f);
                    l.atmosphere = 0.8f;
                    l.skyExposure = 1.2f;
                    l.solidSky = false;
                    l.musicStyle = new MusicStyle
                    {
                        genre = MusicGenre.Techno, bpm = 128f, rootMidi = 38, scale = ScaleType.Minor,
                        progression = new[] { 0, 0, 5, 3 }, bars = 32, seed = 37, swing = 0f
                    };
                    break;
            }
        }
    }
}
