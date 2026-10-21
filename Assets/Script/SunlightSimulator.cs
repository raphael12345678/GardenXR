using System;
using UnityEngine;

namespace GardenXR
{
    /// <summary>
    /// Simulation d'ensoleillement : place la Directional Light selon l'heure, le jour de l'année
    /// et la latitude (trajectoire solaire réelle). Les ombres portées sont recalculées en temps réel par Unity.
    /// Un seul exemplaire dans la scène (objet vide "Sun").
    /// </summary>
    public class SunlightSimulator : MonoBehaviour
    {
        public static SunlightSimulator Instance { get; private set; }

        [Header("Lumière")]
        [Tooltip("La Directional Light de la scène (trouvée automatiquement si vide).")]
        [SerializeField] Light sunLight;
        [SerializeField] float maxIntensity = 1.3f;
        [SerializeField] Color lowSunColor = new Color(1f, 0.55f, 0.3f);
        [SerializeField] Color highSunColor = new Color(1f, 0.96f, 0.9f);

        [Header("Lieu")]
        [Tooltip("Latitude en degrés (Paris = 48.85).")]
        [SerializeField] float latitude = 48.85f;
        [Tooltip("Rotation du nord de la scène (0 = le nord est vers +Z).")]
        [SerializeField] float northOffset = 0f;

        [Header("Date et heure")]
        [Range(0f, 24f)] [SerializeField] float hour = 14f;
        [Range(1, 365)] [SerializeField] int dayOfYear = 172; // 21 juin

        [Header("Animation (optionnel)")]
        [SerializeField] bool animateDay = false;
        [SerializeField] float hoursPerSecond = 1f;
        [SerializeField] float animStartHour = 6f;
        [SerializeField] float animEndHour = 21f;

        public event Action Changed;

        public float Hour => hour;
        public int DayOfYear => dayOfYear;
        public int Month => DayToMonth(dayOfYear);
        public float ElevationDeg { get; private set; }
        public float AzimuthDeg { get; private set; }

        static readonly int[] MonthStartDay = { 1, 32, 60, 91, 121, 152, 182, 213, 244, 274, 305, 335 };

        void Awake()
        {
            Instance = this;
            if (sunLight == null)
            {
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { sunLight = l; break; }
            }
            if (sunLight == null) { Debug.LogWarning("[Sun] Aucune Directional Light trouvée."); return; }

            if (sunLight.shadows == LightShadows.None) sunLight.shadows = LightShadows.Soft;
            RenderSettings.sun = sunLight; // le ciel procédural suit le soleil
            Apply();
        }

        void Update()
        {
            if (!animateDay) return;
            hour += hoursPerSecond * Time.deltaTime;
            if (hour > animEndHour) hour = animStartHour;
            Apply();
        }

        void OnValidate()
        {
            if (sunLight) Apply(); // aperçu dans l'éditeur
        }

        // ---------- API ----------

        public void SetHour(float h)
        {
            hour = Mathf.Clamp(h, 0f, 24f);
            Apply();
        }

        public void SetDayOfYear(int d)
        {
            dayOfYear = Mathf.Clamp(d, 1, 365);
            Apply();
        }

        /// <summary>Mois 1-12 : on prend le 21 du mois (solstices / équinoxes).</summary>
        public void SetMonth(int month)
        {
            month = Mathf.Clamp(month, 1, 12);
            SetDayOfYear(MonthStartDay[month - 1] + 20);
        }

        public void SetAnimate(bool on) => animateDay = on;

        // ---------- Calcul ----------

        /// <summary>Direction (normalisée) vers le soleil à une heure donnée, pour le jour simulé.</summary>
        public Vector3 DirectionToSun(float atHour) => ComputeSun(atHour, dayOfYear, out _, out _);

        Vector3 ComputeSun(float atHour, int day, out float elevationRad, out float azimuthNorthRad)
        {
            float lat = latitude * Mathf.Deg2Rad;
            float decl = 23.44f * Mathf.Deg2Rad * Mathf.Sin(2f * Mathf.PI * (284 + day) / 365f);
            float hAngle = (atHour - 12f) * 15f * Mathf.Deg2Rad;

            float sinEl = Mathf.Sin(lat) * Mathf.Sin(decl) + Mathf.Cos(lat) * Mathf.Cos(decl) * Mathf.Cos(hAngle);
            elevationRad = Mathf.Asin(Mathf.Clamp(sinEl, -1f, 1f));

            // Azimut mesuré depuis le sud (positif vers l'ouest), converti depuis le nord dans le sens horaire
            float azSouth = Mathf.Atan2(Mathf.Sin(hAngle),
                                        Mathf.Cos(hAngle) * Mathf.Sin(lat) - Mathf.Tan(decl) * Mathf.Cos(lat));
            azimuthNorthRad = azSouth + Mathf.PI + northOffset * Mathf.Deg2Rad;

            // Direction vers le soleil (nord = +Z, est = +X)
            return new Vector3(Mathf.Sin(azimuthNorthRad) * Mathf.Cos(elevationRad),
                               Mathf.Sin(elevationRad),
                               Mathf.Cos(azimuthNorthRad) * Mathf.Cos(elevationRad));
        }

        void Apply()
        {
            if (sunLight == null) return;

            Vector3 toSun = ComputeSun(hour, dayOfYear, out float el, out float azNorth);
            ElevationDeg = el * Mathf.Rad2Deg;
            AzimuthDeg = Mathf.Repeat(azNorth * Mathf.Rad2Deg, 360f);
            sunLight.transform.rotation = Quaternion.LookRotation(-toSun);

            // Intensité et couleur selon la hauteur du soleil
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(ElevationDeg / 30f));
            sunLight.intensity = maxIntensity * t;
            sunLight.color = Color.Lerp(lowSunColor, highSunColor, Mathf.Clamp01(ElevationDeg / 25f));
            RenderSettings.ambientIntensity = Mathf.Lerp(0.3f, 1f, t);

            Changed?.Invoke();
        }

        public static int DayToMonth(int day)
        {
            for (int m = 11; m >= 0; m--)
                if (day >= MonthStartDay[m]) return m + 1;
            return 1;
        }
    }
}
