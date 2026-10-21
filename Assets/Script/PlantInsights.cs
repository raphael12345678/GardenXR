using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GardenXR
{
    /// <summary>
    /// - Analyse l'ensoleillement de chaque plante posée sur la journée simulée (7 h → 19 h),
    ///   en tenant compte de l'ombre portée des autres objets posés.
    /// - Affiche une icône d'alerte rouge au-dessus des plantes dont le besoin en lumière n'est pas satisfait
    ///   (ALC « alerte de besoins non satisfaits »).
    /// - Affiche un panneau d'info au-dessus de l'objet visé par le rayon (ACV Observer).
    /// Un seul exemplaire dans la scène (objet vide "PlantInsights").
    /// </summary>
    public class PlantInsights : MonoBehaviour
    {
        public static PlantInsights Instance { get; private set; }

        [SerializeField] SunlightSimulator sun;

        [Header("Analyse")]
        [SerializeField] float refreshInterval = 0.5f;
        [SerializeField] float dayStartHour = 7f;
        [SerializeField] float dayEndHour = 19f;
        [Tooltip("Plein soleil : part minimale d'heures ensoleillées.")]
        [Range(0f, 1f)] [SerializeField] float fullSunMinRatio = 0.6f;
        [Tooltip("Mi-ombre : part minimale d'heures ensoleillées.")]
        [Range(0f, 1f)] [SerializeField] float partShadeMinRatio = 0.3f;
        [Tooltip("Ombre : part maximale d'heures ensoleillées.")]
        [Range(0f, 1f)] [SerializeField] float shadeMaxRatio = 0.5f;

        [Header("Affichage")]
        [SerializeField] float infoPanelWidth = 0.4f;
        [SerializeField] float alertSize = 0.18f;
        [SerializeField] bool alertSound = true;

        public class Result
        {
            public int sunHours, totalHours;
            public string issue; // null = besoins satisfaits
            public float Ratio => totalHours > 0 ? (float)sunHours / totalHours : 0f;
        }

        readonly Dictionary<PlacedItem, Result> _results = new Dictionary<PlacedItem, Result>();
        readonly Dictionary<PlacedItem, GameObject> _alerts = new Dictionary<PlacedItem, GameObject>();
        readonly RaycastHit[] _hits = new RaycastHit[32];
        readonly List<PlacedItem> _tmp = new List<PlacedItem>();

        PlacedItem _hovered;
        float _timer;
        Transform _head;
        Canvas _panel;
        TMP_Text _panelText;

        // ---------- Cycle de vie ----------

        void Awake() => Instance = this;

        void Start()
        {
            if (sun == null) sun = SunlightSimulator.Instance ? SunlightSimulator.Instance : FindFirstObjectByType<SunlightSimulator>();
            if (Camera.main) _head = Camera.main.transform;
            BuildInfoPanel();
        }

        void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                _timer = refreshInterval;
                AnalyzeAll();
            }
        }

        void LateUpdate()
        {
            if (_head == null) return;

            // Icônes d'alerte : au-dessus de la plante, tournées vers l'utilisateur
            foreach (var kv in _alerts)
            {
                if (kv.Key == null || kv.Value == null) continue;
                Bounds b = kv.Key.GetBounds();
                var t = kv.Value.transform;
                t.position = new Vector3(b.center.x, b.max.y + alertSize * 0.8f, b.center.z);
                t.rotation = Quaternion.LookRotation(t.position - _head.position);
            }

            UpdateInfoPanel();
        }

        public void SetHovered(PlacedItem item, bool on)
        {
            if (on) _hovered = item;
            else if (_hovered == item) _hovered = null;
        }

        // ---------- Analyse ----------

        void AnalyzeAll()
        {
            if (sun == null) return;

            foreach (var item in PlacedItem.All)
            {
                if (item == null || item.Data == null || !item.Data.IsPlant)
                {
                    SetAlert(item, false);
                    continue;
                }

                var r = Analyze(item);
                _results[item] = r;
                SetAlert(item, r.issue != null);
            }

            // Nettoyage : objets supprimés ou désactivés
            _tmp.Clear();
            foreach (var key in _alerts.Keys)
                if (key == null || !PlacedItem.All.Contains(key)) _tmp.Add(key);
            foreach (var key in _tmp)
            {
                if (_alerts[key]) Destroy(_alerts[key]);
                _alerts.Remove(key);
                _results.Remove(key);
            }
        }

        public Result Analyze(PlacedItem item)
        {
            var r = new Result();
            Bounds b = item.GetBounds();
            Vector3 origin = b.center + Vector3.up * (b.extents.y * 0.3f);

            for (float h = dayStartHour; h <= dayEndHour + 0.01f; h += 1f)
            {
                Vector3 dir = sun.DirectionToSun(h);
                if (dir.y < 0.05f) continue; // soleil couché ou rasant
                r.totalHours++;
                if (!IsShaded(item, origin, dir)) r.sunHours++;
            }

            float ratio = r.Ratio;
            switch (item.Data.sunNeed)
            {
                case SunNeed.PleinSoleil: if (ratio < fullSunMinRatio) r.issue = "Manque de soleil"; break;
                case SunNeed.MiOmbre:     if (ratio < partShadeMinRatio) r.issue = "Manque de soleil"; break;
                case SunNeed.Ombre:       if (ratio > shadeMaxRatio) r.issue = "Trop de soleil"; break;
            }
            return r;
        }

        /// <summary>Vrai si un autre objet posé se trouve entre la plante et le soleil.</summary>
        bool IsShaded(PlacedItem item, Vector3 origin, Vector3 dirToSun)
        {
            // Rayon lancé DEPUIS le soleil VERS la plante : un rayon ne détecte pas un collider
            // dans lequel il démarre. Partir de loin évite de rater une grande plante très proche
            // (ou une plante tenue en l'air à l'intérieur de la boîte d'une autre).
            const float dist = 60f;
            Vector3 from = origin + dirToSun * dist;
            int n = Physics.RaycastNonAlloc(from, -dirToSun, _hits, dist - 0.01f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var other = _hits[i].collider.GetComponentInParent<PlacedItem>();
                if (other != null && other != item) return true;
            }
            return false;
        }

        // ---------- Alertes ----------

        void SetAlert(PlacedItem item, bool on)
        {
            if (item == null) return;
            bool has = _alerts.TryGetValue(item, out var go) && go != null;

            if (on && !has)
            {
                _alerts[item] = CreateAlertIcon();
                if (alertSound) GardenSounds.Play(GardenSound.Click, item.transform.position);
            }
            else if (!on && has)
            {
                Destroy(go);
                _alerts.Remove(item);
            }
        }

        GameObject CreateAlertIcon()
        {
            const float px = 100f;
            var root = new GameObject("ShadeAlert", typeof(RectTransform));
            root.transform.localScale = Vector3.one * (alertSize / px);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)root.transform).sizeDelta = new Vector2(px, px);

            var bg = NewUI("Circle", root.transform);
            Stretch(bg);
            var img = bg.gameObject.AddComponent<Image>();
            img.color = new Color(0.85f, 0.1f, 0.1f, 0.95f);
            img.raycastTarget = false;

            var txt = NewUI("Mark", root.transform);
            Stretch(txt);
            var t = txt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = "!";
            t.fontSize = 80;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
            t.raycastTarget = false;
            return root;
        }

        // ---------- Panneau d'info au survol ----------

        void BuildInfoPanel()
        {
            const float w = 460f, h = 240f;
            var root = new GameObject("PlantInfoPanel", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            root.transform.localScale = Vector3.one * (infoPanelWidth / w);
            _panel = root.AddComponent<Canvas>();
            _panel.renderMode = RenderMode.WorldSpace;
            ((RectTransform)root.transform).sizeDelta = new Vector2(w, h);

            var bg = NewUI("Background", root.transform);
            Stretch(bg);
            var img = bg.gameObject.AddComponent<Image>();
            img.color = new Color(0.06f, 0.1f, 0.06f, 0.88f);
            img.raycastTarget = false;

            var txt = NewUI("Text", root.transform);
            Stretch(txt);
            txt.offsetMin = new Vector2(18f, 12f);
            txt.offsetMax = new Vector2(-18f, -12f);
            _panelText = txt.gameObject.AddComponent<TextMeshProUGUI>();
            _panelText.fontSize = 30;
            _panelText.color = Color.white;
            _panelText.alignment = TextAlignmentOptions.TopLeft;
            _panelText.raycastTarget = false;

            _panel.enabled = false;
        }

        void UpdateInfoPanel()
        {
            if (_panel == null) return;
            if (_hovered == null || !_hovered.isActiveAndEnabled || _hovered.Data == null)
            {
                _panel.enabled = false;
                return;
            }

            var data = _hovered.Data;
            Bounds b = _hovered.GetBounds();
            var t = _panel.transform;
            t.position = new Vector3(b.center.x, b.max.y + 0.3f, b.center.z);
            t.rotation = Quaternion.LookRotation(t.position - _head.position);

            string text = $"<b>{data.displayName}</b>\n<size=75%><color=#B8D8B0>{data.groupName}</color></size>\n";

            if (!data.IsPlant)
            {
                text += "Décoration (pas de besoins)";
            }
            else
            {
                if (!_results.TryGetValue(_hovered, out var r) && sun != null) r = Analyze(_hovered);
                text += $"Lumière : {SunLabel(data.sunNeed)}\n";
                if (r != null)
                {
                    text += $"Soleil reçu : {r.sunHours} h / {r.totalHours} h ({Mathf.RoundToInt(r.Ratio * 100f)} %)\n";
                }
                text += $"Eau : {WaterLabel(data.waterNeed)}\n";
                text += r != null && r.issue != null
                    ? $"<color=#FF6060><b>! {r.issue}</b></color>"
                    : "<color=#80FF80><b>Besoins satisfaits</b></color>";
            }

            _panelText.text = text;
            _panel.enabled = true;
        }

        static string SunLabel(SunNeed n) => n switch
        {
            SunNeed.PleinSoleil => "Plein soleil",
            SunNeed.MiOmbre => "Mi-ombre",
            SunNeed.Ombre => "Ombre",
            _ => "-"
        };

        static string WaterLabel(WaterNeed n) => n switch
        {
            WaterNeed.Faible => "Faible",
            WaterNeed.Moyen => "Moyen",
            WaterNeed.Eleve => "Élevé",
            _ => "-"
        };

        // ---------- Utilitaires UI ----------

        static RectTransform NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
