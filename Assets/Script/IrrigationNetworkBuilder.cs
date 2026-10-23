using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

namespace GardenXR
{
    /// <summary>Type de tuyau proposé dans l'onglet « Irrigation » du catalogue.</summary>
    [Serializable]
    public class IrrigationStyle
    {
        public string name = "Goutte-à-goutte";
        public Color color = new Color(0.2f, 0.55f, 1f);
        [Tooltip("Rayon arrosé de chaque côté du tuyau (m).")]
        public float coverageRadius = 0.6f;
        [Tooltip("Débit en litres par heure et par mètre de tuyau.")]
        public float flowPerMeter = 2f;
        [Tooltip("Courte description affichée dans le panneau d'info du tuyau.")]
        [TextArea] public string description = "";
    }

    /// <summary>
    /// Modèle d'eau simplifié :
    /// - BESOIN d'une plante = besoin de son type (Faible / Moyen / Élevé) × facteur de taille
    ///   (hauteur et largeur du feuillage) : un arbre boit bien plus qu'une petite plante.
    /// - RACINES : plus la plante est grande, plus ses racines s'étendent (≈ largeur du feuillage
    ///   + 15 % de la hauteur) et plus elle peut aller chercher l'eau loin du tuyau.
    /// - DIFFUSION : l'eau se propage dans le sol autour du tuyau en 3 zones de moins en moins fournies :
    ///   zone principale (100 %), zone de diffusion (50 %), zone éloignée (20 %).
    /// </summary>
    public static class WaterModel
    {
        public static float Low = 2f, Medium = 5f, High = 10f;   // L/jour pour une plante de taille « standard »

        // Largeur des zones, en multiples du rayon arrosé du tuyau, et part d'eau reçue
        public static readonly float[] ZoneRadiusFactor = { 1f, 2f, 3f };
        public static readonly float[] ZoneSupply = { 1f, 0.5f, 0.2f };
        public static readonly string[] ZoneName = { "zone principale", "zone de diffusion", "zone éloignée" };

        public static float BaseNeed(WaterNeed n) => n switch
        {
            WaterNeed.Faible => Low,
            WaterNeed.Moyen => Medium,
            WaterNeed.Eleve => High,
            _ => 0f
        };

        /// <summary>Facteur de taille : ~0,6 pour une petite plante, ~5 pour un arbre de 5 m.</summary>
        public static float SizeFactor(Bounds b)
        {
            float height = b.size.y;
            float canopy = Mathf.Max(b.extents.x, b.extents.z);
            return Mathf.Clamp(0.5f * height + 2f * canopy, 0.3f, 8f);
        }

        /// <summary>Rayon des racines au sol (m).</summary>
        public static float RootRadius(Bounds b)
        {
            float canopy = Mathf.Max(b.extents.x, b.extents.z);
            return canopy + 0.15f * b.size.y;
        }

        public static float NeedPerDay(PlacedItem item)
        {
            if (item == null || item.Data == null || !item.Data.IsPlant) return 0f;
            return BaseNeed(item.Data.waterNeed) * SizeFactor(item.GetBounds());
        }

        /// <summary>Part d'eau reçue d'UN tuyau (0, 0.2, 0.5 ou 1) et zone correspondante (-1 = aucune).</summary>
        public static float SupplyFrom(IrrigationPipe pipe, PlacedItem item, out int zone)
        {
            zone = -1;
            if (pipe == null || item == null) return 0f;
            Bounds b = item.GetBounds();
            // Distance entre le tuyau et le bord des racines
            float d = Mathf.Max(0f, pipe.DistanceXZ(b.center) - RootRadius(b));
            for (int i = 0; i < ZoneRadiusFactor.Length; i++)
            {
                if (d <= pipe.coverageRadius * ZoneRadiusFactor[i])
                {
                    zone = i;
                    return ZoneSupply[i];
                }
            }
            return 0f;
        }

        /// <summary>Meilleure part d'eau reçue parmi tous les tuyaux validés.</summary>
        public static float Supply(PlacedItem item, out int zone)
        {
            zone = -1;
            float best = 0f;
            foreach (var pipe in IrrigationPipe.All)
            {
                if (!pipe || !pipe.validated) continue;
                float s = SupplyFrom(pipe, item, out int z);
                if (s > best) { best = s; zone = z; }
            }
            return best;
        }
    }

    /// <summary>Un tuyau tracé (ajouté automatiquement par le builder).</summary>
    public class IrrigationPipe : MonoBehaviour
    {
        public static readonly List<IrrigationPipe> All = new List<IrrigationPipe>();

        public List<Vector3> points = new List<Vector3>();
        public float coverageRadius = 0.6f;
        public float flowPerMeter = 2f;
        public bool validated;
        public string styleName;
        public string description;

        internal LineRenderer line;
        internal GameObject coverage;
        internal Transform label;
        internal readonly List<Renderer> halos = new List<Renderer>();

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        public float Length
        {
            get
            {
                float l = 0f;
                for (int i = 1; i < points.Count; i++) l += FlatDist(points[i], points[i - 1]);
                return l;
            }
        }

        public bool Covers(Vector3 p) => IsNear(p, coverageRadius);

        /// <summary>Distance horizontale entre une position et le tuyau.</summary>
        public float DistanceXZ(Vector3 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < points.Count - 1; i++)
                best = Mathf.Min(best, DistSqXZ(p, points[i], points[i + 1]));
            if (points.Count == 1) best = (new Vector2(p.x - points[0].x, p.z - points[0].z)).sqrMagnitude;
            return Mathf.Sqrt(best);
        }

        /// <summary>Vrai si la position (au sol) est à moins de 'radius' du tuyau.</summary>
        public bool IsNear(Vector3 p, float radius)
        {
            float r2 = radius * radius;
            for (int i = 0; i < points.Count - 1; i++)
                if (DistSqXZ(p, points[i], points[i + 1]) <= r2) return true;
            return false;
        }

        /// <summary>Vrai si la position est arrosée par un tuyau validé.</summary>
        public static bool IsCovered(Vector3 p)
        {
            foreach (var pipe in All)
                if (pipe && pipe.validated && pipe.Covers(p)) return true;
            return false;
        }

        internal static float FlatDist(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        static float DistSqXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            var P = new Vector2(p.x, p.z);
            var A = new Vector2(a.x, a.z);
            var AB = new Vector2(b.x, b.z) - A;
            float t = AB.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(P - A, AB) / AB.sqrMagnitude) : 0f;
            return (A + AB * t - P).sqrMagnitude;
        }
    }

    /// <summary>
    /// Tracé du réseau d'irrigation (séquence secondaire du canevas) :
    /// - Observer / Naviguer jusqu'au point de départ, choisir un type dans l'onglet « Irrigation » ;
    /// - Gâchette MAINTENUE = tracer, relâcher = terminer le segment (snap automatique sur les bordures
    ///   de massif et sur les extrémités des autres tuyaux) ;
    /// - Ligne bleue pointillée, longueur affichée, halo orange sur les jonctions non validées,
    ///   clic sonore à chaque segment, vibration légère pendant le tracé ;
    /// - A (main droite) = valider le réseau → aperçu des zones arrosées + estimation des besoins en eau ;
    /// - B = quitter l'outil, Y = annuler le dernier tuyau.
    /// Un seul exemplaire dans la scène (objet vide "Irrigation").
    /// </summary>
    public class IrrigationNetworkBuilder : MonoBehaviour
    {
        public static IrrigationNetworkBuilder Instance { get; private set; }

        [Header("Rayon")]
        [SerializeField] Transform rayOrigin;
        [SerializeField] float maxDistance = 30f;
        [SerializeField] LayerMask groundMask = ~0;
        [SerializeField] HapticImpulsePlayer haptics;

        [Header("Tracé")]
        [SerializeField] float pointSpacing = 0.25f;
        [Tooltip("Snap sur le bord d'un massif (PlantingZone) à moins de cette distance (m).")]
        [SerializeField] float borderSnapDistance = 0.25f;
        [Tooltip("Snap sur l'extrémité d'un autre tuyau à moins de cette distance (m).")]
        [SerializeField] float junctionSnapDistance = 0.3f;
        [SerializeField] Transform pipesRoot;

        [Header("Besoins en eau (litres / jour / plante)")]
        [SerializeField] float waterLow = 2f;
        [SerializeField] float waterMedium = 5f;
        [SerializeField] float waterHigh = 10f;

        [Header("Styles")]
        [SerializeField] List<IrrigationStyle> styles = new List<IrrigationStyle>
        {
            new IrrigationStyle { name = "Goutte-à-goutte", color = new Color(0.2f, 0.55f, 1f),  coverageRadius = 0.5f, flowPerMeter = 2f,
                description = "Goutteurs ponctuels au pied des plantes. Arrosage précis et économe, idéal pour les rangs et les massifs." },
            new IrrigationStyle { name = "Microporeux",     color = new Color(0.1f, 0.35f, 0.8f), coverageRadius = 0.8f, flowPerMeter = 4f,
                description = "Tuyau qui suinte sur toute sa longueur. Bande humide plus large, pour les massifs denses et les haies." },
        };

        public IReadOnlyList<IrrigationStyle> Styles => styles;
        public bool IsActive => _style != null;

        static readonly Color PendingHalo = new Color(1f, 0.55f, 0.1f, 0.85f);
        static readonly Color ValidHalo = new Color(0.25f, 0.6f, 1f, 0.6f);

        IrrigationStyle _style;
        bool _waitRelease, _drawing, _trigWas, _bWas, _aWas;
        readonly List<Vector3> _pts = new List<Vector3>();
        GameObject _cursor;
        Renderer _cursorRenderer;
        LineRenderer _preview;
        Texture2D _dashTex;
        float _hapticTimer;
        Transform _head;
        Canvas _summary;
        TMP_Text _summaryText;
        float _summaryTimer;

        void Awake()
        {
            Instance = this;
            ApplyWaterSettings();
        }

        void OnValidate() => ApplyWaterSettings();

        void ApplyWaterSettings()
        {
            WaterModel.Low = waterLow;
            WaterModel.Medium = waterMedium;
            WaterModel.High = waterHigh;
        }

        void Start()
        {
            if (Camera.main) _head = Camera.main.transform;
            _dashTex = CreateDashTexture();
            BuildSummaryPanel();
            SetCoverageVisible(false);
        }

        // ---------- API ----------

        public void Begin(IrrigationStyle style)
        {
            var placer = FindFirstObjectByType<PrefabPlacer>();
            if (placer) placer.CancelPlacement();
            if (PathTool.Instance) PathTool.Instance.Stop();
            if (ZoneTool.Instance) ZoneTool.Instance.Stop();

            Stop();
            _style = style;
            _waitRelease = true;
            _cursor = CreateDisc("IrrigationCursor", 0.12f, PendingHalo, out _cursorRenderer);
            SetCoverageVisible(true);
            RefreshSummary();
        }

        public void Stop()
        {
            CancelStroke();
            if (_cursor) Destroy(_cursor);
            _cursor = null;
            _style = null;
            SetCoverageVisible(false);
            if (_summary) _summary.enabled = false;
        }

        /// <summary>Signature du document de conception : ajoute un segment droit.</summary>
        public IrrigationPipe AddSegment(Vector3 start, Vector3 end, float flowRate)
        {
            var s = _style ?? (styles.Count > 0 ? styles[0] : new IrrigationStyle());
            var style = new IrrigationStyle { name = s.name, color = s.color, coverageRadius = s.coverageRadius, flowPerMeter = flowRate, description = s.description };
            return CreatePipe(new List<Vector3> { start, end }, style);
        }

        /// <summary>Valide tous les tuyaux en attente : halos bleus, zones arrosées, estimation.</summary>
        public void ValidateNetwork()
        {
            int n = 0;
            foreach (var pipe in IrrigationPipe.All)
            {
                if (!pipe || pipe.validated) continue;
                pipe.validated = true;
                foreach (var h in pipe.halos) if (h) h.sharedMaterial.color = ValidHalo;
                n++;
            }

            if (n == 0) { GardenSounds.PlayAtHead(GardenSound.Error); return; }
            GardenSounds.PlayAtHead(GardenSound.Place);
            if (haptics) haptics.SendHapticImpulse(0.5f, 0.12f);
            SetCoverageVisible(true);
            RefreshSummary();
        }

        // ---------- Boucle ----------

        void Update()
        {
            UpdateHoverPanel(); // panneau d'info d'un tuyau visé (outil actif ou non)
            if (!IsActive) return;

            bool trig = Pressed("triggerPressed");
            bool b = Pressed("secondaryButton");
            bool a = Pressed("primaryButton");

            if (b && !_bWas) { _bWas = true; Stop(); GardenSounds.PlayAtHead(GardenSound.MenuClose); return; }
            _bWas = b;

            if (a && !_aWas && !_drawing) ValidateNetwork();
            _aWas = a;

            bool hit = RayGround(out Vector3 raw);
            bool snapped = false;
            Vector3 p = hit ? Snap(raw, out snapped) : raw;
            if (_cursor)
            {
                _cursor.SetActive(hit);
                if (hit)
                {
                    _cursor.transform.position = p + Vector3.up * 0.075f;
                    _cursorRenderer.sharedMaterial.color = snapped ? new Color(0.3f, 1f, 0.4f, 0.9f) : PendingHalo;
                }
            }

            if (_waitRelease)
            {
                if (!trig) _waitRelease = false;
                _trigWas = trig;
                return;
            }

            if (trig && !_trigWas && hit)
            {
                _drawing = true;
                _pts.Clear();
                _pts.Add(p);
                UpdatePreview(p);
            }
            else if (trig && _drawing)
            {
                if (hit && IrrigationPipe.FlatDist(p, _pts[_pts.Count - 1]) >= pointSpacing) _pts.Add(p);
                UpdatePreview(hit ? p : _pts[_pts.Count - 1]);

                _hapticTimer -= Time.deltaTime;
                if (haptics && _hapticTimer <= 0f)
                {
                    haptics.SendHapticImpulse(0.1f, 0.1f);
                    _hapticTimer = 0.1f;
                }
            }
            else if (!trig && _trigWas && _drawing)
            {
                if (hit && IrrigationPipe.FlatDist(p, _pts[_pts.Count - 1]) > 0.05f) _pts.Add(p); // snap de fin
                FinishStroke();
            }
            _trigWas = trig;
        }

        void LateUpdate()
        {
            if (!IsActive || _head == null) return;

            foreach (var pipe in IrrigationPipe.All)
                if (pipe && pipe.label)
                    pipe.label.rotation = Quaternion.LookRotation(pipe.label.position - _head.position);
        }

        void FinishStroke()
        {
            _drawing = false;
            if (_pts.Count < 2 || TotalLength(_pts) < 0.2f)
            {
                CancelStroke();
                GardenSounds.PlayAtHead(GardenSound.Error);
                return;
            }

            var pipe = CreatePipe(new List<Vector3>(_pts), _style);
            CancelStroke();
            GardenSounds.Play(GardenSound.Click, pipe.points[pipe.points.Count - 1]); // clic à chaque segment posé
            if (GardenEditHistory.Instance) GardenEditHistory.Instance.RecordPlace(pipe.gameObject);
            RefreshSummary();
        }

        void CancelStroke()
        {
            _drawing = false;
            _pts.Clear();
            if (_preview) Destroy(_preview.gameObject);
            _preview = null;
        }

        // ---------- Snap ----------

        Vector3 Snap(Vector3 p, out bool snapped)
        {
            snapped = false;

            // 1. Extrémités des tuyaux existants (jonctions)
            float best = junctionSnapDistance;
            Vector3 result = p;
            foreach (var pipe in IrrigationPipe.All)
            {
                if (!pipe || pipe.points.Count == 0) continue;
                foreach (var end in new[] { pipe.points[0], pipe.points[pipe.points.Count - 1] })
                {
                    float d = IrrigationPipe.FlatDist(p, end);
                    if (d < best) { best = d; result = new Vector3(end.x, p.y, end.z); snapped = true; }
                }
            }
            if (snapped) return result;

            // 2. Bordures de massif
            best = borderSnapDistance;
            foreach (var zone in PlantingZone.All)
            {
                Vector3 q = zone.ClosestPointOnBorder(p);
                float d = IrrigationPipe.FlatDist(p, q);
                if (d < best) { best = d; result = new Vector3(q.x, p.y, q.z); snapped = true; }
            }
            return result;
        }

        // ---------- Création des tuyaux ----------

        IrrigationPipe CreatePipe(List<Vector3> pts, IrrigationStyle style)
        {
            var go = new GameObject($"Tuyau_{style.name}_{IrrigationPipe.All.Count + 1}");
            if (pipesRoot) go.transform.SetParent(pipesRoot, false);
            var pipe = go.AddComponent<IrrigationPipe>();
            pipe.points = pts;
            pipe.coverageRadius = style.coverageRadius;
            pipe.flowPerMeter = style.flowPerMeter;
            pipe.styleName = style.name;
            pipe.description = style.description;

            // Ligne bleue pointillée
            pipe.line = CreateLine(go.transform, style.color, pts);

            // Zones arrosées : principale (100 %), diffusion (50 %), éloignée (20 %),
            // bandes semi-transparentes de plus en plus larges et pâles (visibles quand l'outil est actif)
            pipe.coverage = new GameObject("Coverage");
            pipe.coverage.transform.SetParent(go.transform, false);
            float[] alphas = { 0.22f, 0.12f, 0.06f };
            for (int z = 0; z < WaterModel.ZoneRadiusFactor.Length; z++)
            {
                var band = new GameObject($"Zone_{z}");
                band.transform.SetParent(pipe.coverage.transform, false);
                var mesh = new Mesh { name = "Coverage" };
                PathTool.FillRibbon(mesh, pts, style.coverageRadius * 2f * WaterModel.ZoneRadiusFactor[z], 0.004f - z * 0.001f);
                band.AddComponent<MeshFilter>().sharedMesh = mesh;
                var cr = band.AddComponent<MeshRenderer>();
                Color cc = style.color; cc.a = alphas[Mathf.Min(z, alphas.Length - 1)];
                cr.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = cc };
                cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            pipe.coverage.SetActive(IsActive);

            // Halos orange sur les jonctions (extrémités) tant que le réseau n'est pas validé
            foreach (var end in new[] { pts[0], pts[pts.Count - 1] })
            {
                var halo = CreateDisc("Jonction", 0.16f, PendingHalo, out var hr);
                halo.transform.SetParent(go.transform, true);
                halo.transform.position = end + Vector3.up * 0.072f;
                halo.SetActive(true);
                pipe.halos.Add(hr);
            }

            // Longueur au milieu du tuyau
            pipe.label = CreateLengthLabel(go.transform, pts, pipe.Length);
            pipe.label.gameObject.SetActive(IsActive);
            return pipe;
        }

        LineRenderer CreateLine(Transform parent, Color color, List<Vector3> pts)
        {
            var lr = new GameObject("Line").AddComponent<LineRenderer>();
            lr.transform.SetParent(parent, false);
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.04f;
            lr.textureMode = LineTextureMode.Tile;
            lr.alignment = LineAlignment.TransformZ;
            lr.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // ruban à plat, vu du dessus
            var mat = new Material(Shader.Find("Sprites/Default")) { mainTexture = _dashTex, color = color };
            mat.mainTextureScale = new Vector2(1f / 0.08f, 1f); // un tiret toutes les 8 cm
            lr.sharedMaterial = mat;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SetLinePoints(lr, pts);
            return lr;
        }

        static void SetLinePoints(LineRenderer lr, List<Vector3> pts)
        {
            lr.positionCount = pts.Count;
            for (int i = 0; i < pts.Count; i++) lr.SetPosition(i, pts[i] + Vector3.up * 0.07f);
        }

        void UpdatePreview(Vector3 current)
        {
            if (_preview == null) _preview = CreateLine(transform, _style.color, _pts);
            var tmp = new List<Vector3>(_pts) { current };
            SetLinePoints(_preview, tmp);
        }

        Transform CreateLengthLabel(Transform parent, List<Vector3> pts, float length)
        {
            Vector3 mid = PointAtHalfLength(pts);
            var go = new GameObject("Longueur", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.position = mid + Vector3.up * 0.25f;
            go.transform.localScale = Vector3.one * 0.0015f;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = new Vector2(220f, 70f);

            var t = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            t.transform.SetParent(go.transform, false);
            var rt = (RectTransform)t.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            t.text = $"{length:0.0} m";
            t.fontSize = 48;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.color = new Color(0.75f, 0.9f, 1f);
            t.outlineWidth = 0.25f;
            t.outlineColor = new Color32(0, 0, 0, 220);
            t.raycastTarget = false;
            return go.transform;
        }

        static Vector3 PointAtHalfLength(List<Vector3> pts)
        {
            float half = TotalLength(pts) * 0.5f, acc = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                float d = IrrigationPipe.FlatDist(pts[i], pts[i - 1]);
                if (acc + d >= half) return Vector3.Lerp(pts[i - 1], pts[i], d > 0f ? (half - acc) / d : 0f);
                acc += d;
            }
            return pts[pts.Count - 1];
        }

        static float TotalLength(List<Vector3> pts)
        {
            float l = 0f;
            for (int i = 1; i < pts.Count; i++) l += IrrigationPipe.FlatDist(pts[i], pts[i - 1]);
            return l;
        }

        void SetCoverageVisible(bool on)
        {
            foreach (var pipe in IrrigationPipe.All)
            {
                if (!pipe) continue;
                if (pipe.coverage) pipe.coverage.SetActive(on);
                if (pipe.label) pipe.label.gameObject.SetActive(on);
            }
        }

        // ---------- Récapitulatif (estimation des besoins en eau) ----------

        void BuildSummaryPanel()
        {
            const float w = 540f, h = 300f;
            var root = new GameObject("IrrigationSummary", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            root.transform.localScale = Vector3.one * (0.42f / w);
            _summary = root.AddComponent<Canvas>();
            _summary.renderMode = RenderMode.WorldSpace;
            ((RectTransform)root.transform).sizeDelta = new Vector2(w, h);

            var bg = new GameObject("Background", typeof(RectTransform)).AddComponent<Image>();
            bg.transform.SetParent(root.transform, false);
            var brt = (RectTransform)bg.transform;
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
            brt.offsetMin = brt.offsetMax = Vector2.zero;
            bg.color = new Color(0.04f, 0.1f, 0.18f, 0.88f);
            bg.raycastTarget = false;

            _summaryText = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            _summaryText.transform.SetParent(root.transform, false);
            var trt = (RectTransform)_summaryText.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(18f, 12f); trt.offsetMax = new Vector2(-18f, -12f);
            _summaryText.fontSize = 28;
            _summaryText.color = Color.white;
            _summaryText.alignment = TextAlignmentOptions.TopLeft;
            _summaryText.raycastTarget = false;

            _summary.enabled = false;
        }

        /// <summary>Force la mise à jour du panneau à la prochaine image.</summary>
        void RefreshSummary() => _summaryTimer = 0f;

        IrrigationPipe _hoveredPipe;

        /// <summary>
        /// Le panneau d'info s'affiche uniquement quand le rayon de la main droite vise un tuyau,
        /// juste au-dessus de l'endroit visé, tourné vers l'utilisateur.
        /// </summary>
        void UpdateHoverPanel()
        {
            if (_summary == null) return;

            IrrigationPipe found = null;
            Vector3 hitPoint = default;
            if (!_drawing && IrrigationPipe.All.Count > 0 && RayGround(out hitPoint))
            {
                foreach (var pipe in IrrigationPipe.All)
                    if (pipe && pipe.IsNear(hitPoint, 0.25f)) { found = pipe; break; }
            }

            if (found == null)
            {
                _summary.enabled = false;
                _hoveredPipe = null;
                return;
            }

            // Position : au-dessus du point visé, à hauteur de poitrine
            var t = _summary.transform;
            Vector3 target = hitPoint + Vector3.up * 0.5f;
            if (_head) target.y = Mathf.Max(target.y, _head.position.y - 0.45f);
            bool newPipe = found != _hoveredPipe;
            t.position = newPipe ? target : Vector3.Lerp(t.position, target, Time.deltaTime * 8f);
            if (_head) t.rotation = Quaternion.LookRotation(t.position - _head.position);

            _summaryTimer -= Time.deltaTime;
            if (newPipe || _summaryTimer <= 0f)
            {
                _summaryTimer = 0.5f;
                _summaryText.text = BuildPipeText(found);
            }
            _hoveredPipe = found;
            _summary.enabled = true;
        }

        string BuildPipeText(IrrigationPipe pipe)
        {
            float length = pipe.Length;
            float pipeFlow = length * pipe.flowPerMeter;

            // Plantes alimentées par CE tuyau, par zone (selon leur position et l'étendue de leurs racines)
            var perZone = new int[WaterModel.ZoneSupply.Length];
            float need = 0f, volume = 0f; // volume à envoyer pour satisfaire les plantes moins bien placées
            foreach (var item in PlacedItem.All)
            {
                if (!item || item.Data == null || !item.Data.IsPlant) continue;
                float supply = WaterModel.SupplyFrom(pipe, item, out int zone);
                if (zone < 0) continue;
                perZone[zone]++;
                float n = WaterModel.NeedPerDay(item);
                need += n;
                volume += n / supply;
            }
            int plants = 0;
            foreach (int c in perZone) plants += c;

            // Réseau entier
            float networkLength = 0f;
            foreach (var p in IrrigationPipe.All) if (p && p.validated) networkLength += p.Length;
            int uncovered = 0;
            foreach (var item in PlacedItem.All)
                if (item && item.Data != null && item.Data.IsPlant && WaterModel.Supply(item, out _) <= 0f)
                    uncovered++;

            string text = $"<b>{pipe.styleName}</b>  " + (pipe.validated
                ? "<color=#7FC8FF>validé</color>"
                : "<color=#FFA040>en attente (A pour valider)</color>") + "\n";
            if (!string.IsNullOrEmpty(pipe.description))
                text += $"<size=68%><color=#9FC4E8>{pipe.description}</color></size>\n";
            text += $"Longueur {length:0.0} m · bande arrosée {pipe.coverageRadius * 2f:0.0} m · {pipe.flowPerMeter:0} L/h/m\n";
            text += $"Plantes alimentées : {plants} · besoin {need:0.#} L/jour\n";
            if (plants > 0)
                text += $"<size=75%>100 % : {perZone[0]}  ·  50 % : {perZone[1]}  ·  20 % : {perZone[2]}</size>\n";
            if (pipeFlow > 0f && volume > 0f)
                text += $"Débit {pipeFlow:0} L/h → arrosage ≈ {Mathf.CeilToInt(volume / pipeFlow * 60f)} min/jour\n";
            text += $"<size=75%>Réseau validé : {networkLength:0.0} m";
            if (uncovered > 0) text += $" · <color=#FFA040>{uncovered} plante(s) non couverte(s)</color>";
            text += "</size>";
            if (IsActive)
                text += "\n<size=65%><color=#9FC4E8>Gâchette : tracer · A : valider · Y : annuler · B : quitter</color></size>";
            return text;
        }

        // ---------- Utilitaires ----------

        bool RayGround(out Vector3 point)
        {
            point = default;
            if (rayOrigin == null) return false;
            if (!Physics.Raycast(rayOrigin.position, rayOrigin.forward, out RaycastHit h, maxDistance, groundMask,
                                 QueryTriggerInteraction.Ignore)) return false;
            point = h.point;
            return true;
        }

        static bool Pressed(string control)
        {
            var c = XRController.rightHand?.TryGetChildControl<ButtonControl>(control);
            return c != null && c.isPressed;
        }

        static GameObject CreateDisc(string name, float diameter, Color color, out Renderer renderer)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            c.name = name;
            Destroy(c.GetComponent<Collider>());
            c.layer = LayerMask.NameToLayer("Ignore Raycast");
            c.transform.localScale = new Vector3(diameter, 0.004f, diameter);
            renderer = c.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = color };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            c.SetActive(false);
            return c;
        }

        static Texture2D CreateDashTexture()
        {
            var tex = new Texture2D(4, 1, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
                name = "IrrigationDash"
            };
            tex.SetPixels(new[] { Color.white, Color.white, Color.white, new Color(1f, 1f, 1f, 0f) });
            tex.Apply();
            return tex;
        }
    }
}
