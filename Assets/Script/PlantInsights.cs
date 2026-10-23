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
        [Tooltip("Plage de recherche du lever/coucher du soleil (le vrai lever/coucher dépend de la saison).")]
        [SerializeField] float dayStartHour = 4f;
        [SerializeField] float dayEndHour = 22f;
        [Tooltip("Pas de simulation de la course du soleil, en heures.")]
        [SerializeField] float timeStep = 0.5f;
        [Tooltip("En dessous de cette hauteur (degrés), le soleil est trop rasant pour compter.")]
        [SerializeField] float minSunElevation = 5f;
        [Tooltip("Réduction horizontale du volume des autres objets (couronne arrondie plutôt que boîte).")]
        [Range(0.5f, 1f)] [SerializeField] float occluderShrink = 0.85f;
        [Tooltip("Plein soleil : part minimale d'heures ensoleillées.")]
        [Range(0f, 1f)] [SerializeField] float fullSunMinRatio = 0.6f;
        [Tooltip("Mi-ombre : part minimale d'heures ensoleillées.")]
        [Range(0f, 1f)] [SerializeField] float partShadeMinRatio = 0.3f;
        [Tooltip("Ombre : part maximale d'heures ensoleillées.")]
        [Range(0f, 1f)] [SerializeField] float shadeMaxRatio = 0.5f;

        [Header("Affichage")]
        [SerializeField] float infoPanelWidth = 0.4f;
        [Tooltip("Grands objets : distance minimale entre toi et le panneau d'info (m).")]
        [SerializeField] float minPanelDistance = 1.3f;
        [SerializeField] float alertSize = 0.18f;
        [SerializeField] bool alertSound = true;

        public class Result
        {
            public float sunHours;    // heures de soleil reçues (pondérées par la part de la plante éclairée)
            public float dayHours;    // durée du jour utile (soleil au-dessus de minSunElevation)
            public float ratio;       // part de l'énergie solaire reçue (heures pondérées par la hauteur du soleil)
            public float sunrise = -1f, sunset = -1f;
            public string issue;      // null = besoins satisfaits
            public float Ratio => ratio;
        }

        readonly Dictionary<PlacedItem, Result> _results = new Dictionary<PlacedItem, Result>();
        readonly Dictionary<PlacedItem, GameObject> _alerts = new Dictionary<PlacedItem, GameObject>();
        readonly List<PlacedItem> _tmp = new List<PlacedItem>();
        readonly List<Bounds> _occluders = new List<Bounds>();
        readonly List<Vector3> _samples = new List<Vector3>();

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

        /// <summary>
        /// Simule la course du soleil sur la journée (pas de 30 min, du lever au coucher réels de la saison).
        /// À chaque pas, plusieurs points du feuillage de la plante (selon sa taille) sont testés contre
        /// le volume des autres objets : on obtient une part éclairée (ombre partielle possible).
        /// Chaque pas est pondéré par la hauteur du soleil : un soleil rasant compte moins qu'un soleil de midi.
        /// </summary>
        public Result Analyze(PlacedItem item)
        {
            var r = new Result();
            Bounds b = item.GetBounds();
            BuildSamples(b);
            CollectOccluders(item, b);

            float minSin = Mathf.Sin(minSunElevation * Mathf.Deg2Rad);
            float litWeighted = 0f, totalWeighted = 0f;

            for (float h = dayStartHour; h <= dayEndHour + 0.001f; h += timeStep)
            {
                Vector3 dir = sun.DirectionToSun(h);
                if (dir.y < minSin) continue; // soleil couché ou trop rasant

                if (r.sunrise < 0f) r.sunrise = h;
                r.sunset = h;
                r.dayHours += timeStep;

                int lit = 0;
                foreach (var p in _samples)
                    if (!IsShaded(p, dir)) lit++;
                float fraction = (float)lit / _samples.Count;

                float weight = dir.y; // sinus de la hauteur du soleil ≈ énergie reçue
                litWeighted += fraction * weight;
                totalWeighted += weight;
                r.sunHours += fraction * timeStep;
            }

            r.ratio = totalWeighted > 0f ? litWeighted / totalWeighted : 0f;

            switch (item.Data.sunNeed)
            {
                case SunNeed.PleinSoleil: if (r.ratio < fullSunMinRatio) r.issue = "Manque de soleil"; break;
                case SunNeed.MiOmbre:     if (r.ratio < partShadeMinRatio) r.issue = "Manque de soleil"; break;
                case SunNeed.Ombre:       if (r.ratio > shadeMaxRatio) r.issue = "Trop de soleil"; break;
            }
            return r;
        }

        /// <summary>
        /// Points testés : centre et 4 points du haut du feuillage, écartés selon la taille de la plante.
        /// Une petite plante a des points rapprochés, une grande des points étalés.
        /// </summary>
        void BuildSamples(Bounds b)
        {
            _samples.Clear();
            float y = b.min.y + b.size.y * 0.75f;
            float ex = b.extents.x * 0.6f, ez = b.extents.z * 0.6f;
            Vector3 c = new Vector3(b.center.x, y, b.center.z);
            _samples.Add(c);
            _samples.Add(c + new Vector3(ex, 0f, 0f));
            _samples.Add(c - new Vector3(ex, 0f, 0f));
            _samples.Add(c + new Vector3(0f, 0f, ez));
            _samples.Add(c - new Vector3(0f, 0f, ez));
        }

        /// <summary>
        /// Garde seulement les objets qui peuvent réellement faire de l'ombre à cette plante :
        /// plus hauts que sa base, et assez proches pour que leur ombre la touche
        /// (longueur d'ombre maximale ≈ hauteur / tan(hauteur minimale du soleil)).
        /// </summary>
        void CollectOccluders(PlacedItem item, Bounds b)
        {
            _occluders.Clear();
            float maxShadowFactor = 1f / Mathf.Tan(Mathf.Max(minSunElevation, 1f) * Mathf.Deg2Rad);

            foreach (var other in PlacedItem.All)
            {
                if (other == null || other == item || !other.isActiveAndEnabled) continue;
                Bounds ob = other.GetBounds();

                float heightAbove = ob.max.y - b.min.y;
                if (heightAbove <= 0.05f) continue; // plus bas que la plante : pas d'ombre

                Vector2 d = new Vector2(ob.center.x - b.center.x, ob.center.z - b.center.z);
                float reach = heightAbove * maxShadowFactor
                              + Mathf.Max(ob.extents.x, ob.extents.z) + Mathf.Max(b.extents.x, b.extents.z);
                if (d.magnitude > reach) continue;

                // Couronne plutôt arrondie : on réduit un peu la boîte horizontalement
                ob.extents = new Vector3(ob.extents.x * occluderShrink, ob.extents.y, ob.extents.z * occluderShrink);
                _occluders.Add(ob);
            }
        }

        /// <summary>
        /// Vrai si un objet se trouve entre ce point et le soleil.
        /// Le rayon part de loin côté soleil vers le point : il détecte aussi une plante collée
        /// ou glissée dans le feuillage d'une autre. On teste le volume visuel (couronne comprise),
        /// pas les colliders (les arbres n'ont qu'un collider fin sur le tronc).
        /// </summary>
        bool IsShaded(Vector3 point, Vector3 dirToSun)
        {
            const float dist = 80f;
            var ray = new Ray(point + dirToSun * dist, -dirToSun);
            foreach (var ob in _occluders)
                if (ob.IntersectRay(ray, out float d) && d < dist - 0.01f) return true;
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
            const float w = 480f, h = 310f;
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
            MakeAlwaysOnTop(_panel);

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
            t.position = InfoPanelPosition(b);
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
                    text += $"Soleil reçu : {r.sunHours:0.#} h / {r.dayHours:0.#} h ({Mathf.RoundToInt(r.Ratio * 100f)} %)\n";
                    if (r.sunrise >= 0f)
                        text += $"<size=75%><color=#B8D8B0>Soleil utile {FormatHour(r.sunrise)} – {FormatHour(r.sunset)}</color></size>\n";
                }
                // Eau : type × taille de la plante, et part reçue selon la distance au tuyau et ses racines
                float need = WaterModel.NeedPerDay(_hovered);
                text += $"Eau : {WaterLabel(data.waterNeed)} · besoin {need:0.#} L/jour\n";
                if (IrrigationPipe.All.Count > 0)
                {
                    float supply = WaterModel.Supply(_hovered, out int zone);
                    float roots = WaterModel.RootRadius(b);
                    text += zone >= 0
                        ? $"<size=80%><color=#7FC8FF>Arrosée à {Mathf.RoundToInt(supply * 100f)} % ({WaterModel.ZoneName[zone]})</color> · racines {roots:0.0} m</size>\n"
                        : $"<size=80%><color=#FFA040>Non arrosée</color> · racines {roots:0.0} m</size>\n";
                }
                text += r != null && r.issue != null
                    ? $"<color=#FF6060><b>! {r.issue}</b></color>"
                    : "<color=#80FF80><b>Besoins satisfaits</b></color>";
            }

            _panelText.text = text;
            _panel.enabled = true;
        }

        /// <summary>
        /// Au-dessus de l'objet, comme d'habitude. Si l'objet est plus haut que les yeux (arbre),
        /// le panneau descend à hauteur du regard et se place devant l'objet, côté utilisateur.
        /// </summary>
        /// <summary>
        /// Place le panneau entre toi et l'objet, quelle que soit sa taille (petite plante, gros buisson, arbre) :
        /// - devant l'objet, de ton côté (jamais caché derrière le feuillage) ;
        /// - à hauteur lisible : juste au-dessus des objets bas, à hauteur de poitrine pour les objets hauts ;
        /// - jamais plus près de toi que 'minPanelDistance' (ni plus loin que l'objet lui-même).
        /// Le panneau est en plus dessiné par-dessus la géométrie, donc toujours visible.
        /// </summary>
        Vector3 InfoPanelPosition(Bounds b)
        {
            Vector3 head = _head.position;
            Vector3 toHead = head - b.center; toHead.y = 0f;
            float dist = toHead.magnitude;
            toHead = dist > 0.001f ? toHead / dist : Vector3.forward;

            // Bord de l'objet tourné vers toi
            float radius = Mathf.Max(b.extents.x, b.extents.z);
            float edgeDist = Mathf.Max(0f, dist - radius);

            // On avance le panneau un peu devant ce bord, sans dépasser la distance minimale
            float forward = Mathf.Clamp(edgeDist - minPanelDistance, 0f, 0.35f);
            Vector3 p = b.center + toHead * (Mathf.Min(radius, dist) + forward);

            // Hauteur : au-dessus de l'objet s'il est bas, sinon à hauteur de poitrine
            float above = b.max.y + 0.25f;
            p.y = Mathf.Clamp(above, head.y - 0.9f, head.y - 0.05f);
            return p;
        }

        /// <summary>Le panneau est dessiné par-dessus tout le reste (feuillage, troncs, autres panneaux).</summary>
        static void MakeAlwaysOnTop(Canvas canvas)
        {
            canvas.sortingOrder = 1000;
            const float always = (float)UnityEngine.Rendering.CompareFunction.Always;
            foreach (var g in canvas.GetComponentsInChildren<Graphic>(true))
            {
                if (g is TMP_Text t)
                {
                    var m = t.fontMaterial; // instance propre à ce texte
                    if (m && m.HasProperty("unity_GUIZTestMode")) m.SetFloat("unity_GUIZTestMode", always);
                }
                else
                {
                    var m = new Material(Shader.Find("UI/Default"));
                    m.SetFloat("unity_GUIZTestMode", always);
                    g.material = m;
                }
            }
        }

        static string FormatHour(float h)
        {
            int hh = Mathf.FloorToInt(h);
            int mm = Mathf.RoundToInt((h - hh) * 60f);
            if (mm == 60) { hh++; mm = 0; }
            return $"{hh:00}:{mm:00}";
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
