using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

namespace GardenXR
{
    /// <summary>Type de zone proposé dans l'onglet « Zones » du catalogue (un par groupe du catalogue).</summary>
    [Serializable]
    public class ZoneStyle
    {
        public string name = "Massif de plantes";
        [Tooltip("Groupes du catalogue autorisés dans cette zone.")]
        public List<string> allowedGroups = new List<string> { "Plantes" };
        public Color color = new Color(0.45f, 0.3f, 0.15f, 0.45f);
        [Tooltip("Pelouse : herbe texturée, tout est autorisé dessus.")]
        public bool lawn;
        [Tooltip("0 = tracer d'un coin à l'autre. > 0 = carré de cette taille (m) posé d'un clic.")]
        public float presetSize;
    }

    /// <summary>
    /// Création de zones de plantation en VR :
    /// choisir un type dans l'onglet « Zones » du catalogue, viser le sol,
    /// MAINTENIR la gâchette d'un coin à l'autre, relâcher pour créer la zone.
    /// Le rectangle est aligné sur ton regard. A en visant une zone = la supprimer.
    /// Y annule la dernière zone, B quitte l'outil.
    /// Un seul exemplaire dans la scène (objet vide "ZoneTool").
    /// </summary>
    public class ZoneTool : MonoBehaviour
    {
        public static ZoneTool Instance { get; private set; }

        [Header("Rayon")]
        [SerializeField] Transform rayOrigin;
        [SerializeField] float maxDistance = 30f;
        [SerializeField] LayerMask groundMask = ~0;
        [SerializeField] HapticImpulsePlayer haptics;

        [Header("Zones")]
        [Tooltip("Si rempli, un type de zone est créé automatiquement pour chaque groupe du catalogue.")]
        [SerializeField] GardenCatalog catalog;
        [SerializeField] float gridSize = 0.25f;
        [SerializeField] float minSize = 0.5f;
        [SerializeField] Transform zonesRoot;
        [Tooltip("Types de zones supplémentaires (ou tous, si aucun catalogue n'est assigné).")]
        [SerializeField] List<ZoneStyle> extraStyles = new List<ZoneStyle>
        {
            new ZoneStyle { name = "Potager / mixte", allowedGroups = new List<string> { "Plantes", "Fleurs" },
                            color = new Color(0.5f, 0.35f, 0.2f, 0.45f) },
        };

        public IReadOnlyList<ZoneStyle> Styles => _styles;
        public bool IsActive => _style != null;

        readonly List<ZoneStyle> _styles = new List<ZoneStyle>();
        ZoneStyle _style;
        bool _waitRelease, _drawing, _trigWas, _bWas, _aWas;
        Vector3 _start;
        float _yaw;
        GameObject _preview, _cursor;
        TMP_Text _sizeLabel;
        Transform _head;
        int _count;

        void Awake()
        {
            Instance = this;
            BuildStyles();
        }

        void Start()
        {
            if (Camera.main) _head = Camera.main.transform;
        }

        void BuildStyles()
        {
            _styles.Clear();
            if (catalog != null)
            {
                foreach (var g in catalog.groups)
                    _styles.Add(new ZoneStyle
                    {
                        name = ZoneNameFor(g.name),
                        allowedGroups = new List<string> { g.name },
                        color = ColorFor(g.name)
                    });
            }
            // Pelouses : tracée librement, ou carrés prêts à poser (petite / moyenne / grande)
            Color grass = new Color(0.3f, 0.55f, 0.2f, 1f);
            _styles.Add(new ZoneStyle { name = "Pelouse (tracer)", lawn = true, color = grass, allowedGroups = new List<string>() });
            _styles.Add(new ZoneStyle { name = "Pelouse petite 2 m", lawn = true, presetSize = 2f, color = grass, allowedGroups = new List<string>() });
            _styles.Add(new ZoneStyle { name = "Pelouse moyenne 5 m", lawn = true, presetSize = 5f, color = grass, allowedGroups = new List<string>() });
            _styles.Add(new ZoneStyle { name = "Pelouse grande 10 m", lawn = true, presetSize = 10f, color = grass, allowedGroups = new List<string>() });

            _styles.AddRange(extraStyles);
        }

        static string ZoneNameFor(string group) => group.ToLowerInvariant() switch
        {
            "plantes" => "Massif de plantes",
            "arbres" => "Verger",
            "fleurs" => "Parterre de fleurs",
            "buissons" => "Haie / bosquet",
            "cactus" => "Jardin sec",
            "rochers" => "Rocaille",
            "autre" => "Terrasse / déco",
            _ => "Zone " + group
        };

        static Color ColorFor(string group) => group.ToLowerInvariant() switch
        {
            "plantes" => new Color(0.35f, 0.6f, 0.25f, 0.45f),
            "arbres" => new Color(0.15f, 0.4f, 0.15f, 0.45f),
            "fleurs" => new Color(0.9f, 0.45f, 0.65f, 0.45f),
            "buissons" => new Color(0.25f, 0.5f, 0.3f, 0.45f),
            "cactus" => new Color(0.85f, 0.72f, 0.4f, 0.45f),
            "rochers" => new Color(0.5f, 0.48f, 0.45f, 0.45f),
            "autre" => new Color(0.6f, 0.6f, 0.6f, 0.45f),
            _ => new Color(0.45f, 0.3f, 0.15f, 0.45f)
        };

        // ---------- API ----------

        public void Begin(ZoneStyle style)
        {
            var placer = FindFirstObjectByType<PrefabPlacer>();
            if (placer) placer.CancelPlacement();
            if (PathTool.Instance) PathTool.Instance.Stop();
            if (IrrigationNetworkBuilder.Instance) IrrigationNetworkBuilder.Instance.Stop();

            Stop();
            _style = style;
            _waitRelease = true;
            if (style.presetSize > 0f)
            {
                // Aperçu du carré à poser, qui suit le rayon
                var size = new Vector2(style.presetSize, style.presetSize);
                _cursor = CreateQuad("ZoneCursor", style, size);
                _cursor.transform.localScale = new Vector3(size.x, size.y, 1f);
                _sizeLabel = CreateLabel(_cursor.transform);
                _sizeLabel.text = $"{size.x:0} × {size.y:0} m";
            }
            else
            {
                _cursor = CreateQuad("ZoneCursor", style, Vector2.one);
                _cursor.transform.localScale = new Vector3(0.15f, 0.15f, 1f);
            }
        }

        public void Stop()
        {
            CancelDraw();
            if (_cursor) Destroy(_cursor);
            _cursor = null;
            _style = null;
        }

        // ---------- Boucle ----------

        void Update()
        {
            if (!IsActive) return;

            bool trig = Pressed("triggerPressed");
            bool b = Pressed("secondaryButton");
            bool a = Pressed("primaryButton");

            if (b && !_bWas) { _bWas = true; Stop(); GardenSounds.PlayAtHead(GardenSound.MenuClose); return; }
            _bWas = b;

            bool hit = RayGround(out Vector3 p);
            if (hit) p = SnapGrid(p);

            if (a && !_aWas && hit && !_drawing) DeleteZoneAt(p);
            _aWas = a;

            float yaw = _head ? Mathf.Round(_head.eulerAngles.y / 15f) * 15f : 0f;
            if (_cursor)
            {
                _cursor.SetActive(hit && !_drawing);
                if (hit) _cursor.transform.position = p + Vector3.up * 0.03f;
                if (_style.presetSize > 0f) _cursor.transform.rotation = Quaternion.Euler(90f, yaw, 0f);
            }
            if (_sizeLabel && _head)
                _sizeLabel.transform.rotation = Quaternion.LookRotation(_sizeLabel.transform.position - _head.position);

            if (_waitRelease)
            {
                if (!trig) _waitRelease = false;
                _trigWas = trig;
                return;
            }

            // Carré prêt à poser : un clic suffit
            if (_style.presetSize > 0f)
            {
                if (trig && !_trigWas && hit)
                    CreateZone(p, new Vector2(_style.presetSize, _style.presetSize), Quaternion.Euler(0f, yaw, 0f));
                _trigWas = trig;
                return;
            }

            if (trig && !_trigWas && hit)
            {
                // Premier coin ; le rectangle est aligné sur la direction du regard
                _drawing = true;
                _start = p;
                _yaw = _head ? Mathf.Round(_head.eulerAngles.y / 15f) * 15f : 0f;
                _preview = CreateQuad("ZonePreview", _style, Vector2.one);
                _sizeLabel = CreateLabel(_preview.transform);
            }
            else if (trig && _drawing && hit)
            {
                UpdatePreview(p);
            }
            else if (!trig && _trigWas && _drawing)
            {
                Finish(hit ? p : _start);
            }
            _trigWas = trig;

            if (_sizeLabel && _head)
                _sizeLabel.transform.rotation = Quaternion.LookRotation(_sizeLabel.transform.position - _head.position);
        }

        void GetRect(Vector3 end, out Vector3 center, out Vector2 size, out Quaternion rot)
        {
            rot = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 local = Quaternion.Inverse(rot) * (end - _start);
            size = new Vector2(Mathf.Abs(local.x), Mathf.Abs(local.z));
            center = _start + rot * new Vector3(local.x * 0.5f, 0f, local.z * 0.5f);
            center.y = Mathf.Max(_start.y, end.y);
        }

        void UpdatePreview(Vector3 end)
        {
            GetRect(end, out Vector3 c, out Vector2 s, out Quaternion rot);
            var t = _preview.transform;
            t.position = c + Vector3.up * 0.03f;
            t.rotation = rot * Quaternion.Euler(90f, 0f, 0f);
            t.localScale = new Vector3(Mathf.Max(s.x, 0.05f), Mathf.Max(s.y, 0.05f), 1f);

            var mr = _preview.GetComponent<MeshRenderer>();
            if (_style.lawn && mr) mr.sharedMaterial.mainTextureScale = s; // herbe à l'échelle réelle
            bool ok = s.x >= minSize && s.y >= minSize;
            _sizeLabel.text = $"{s.x:0.00} × {s.y:0.00} m";
            _sizeLabel.color = ok ? Color.white : new Color(1f, 0.55f, 0.2f);
        }

        void Finish(Vector3 end)
        {
            GetRect(end, out Vector3 c, out Vector2 s, out Quaternion rot);
            CancelDraw();

            if (s.x < minSize || s.y < minSize)
            {
                GardenSounds.PlayAtHead(GardenSound.Error);
                return;
            }

            CreateZone(c, s, rot);
        }

        void CreateZone(Vector3 c, Vector2 s, Quaternion rot)
        {
            var go = new GameObject($"Zone_{_style.name}_{++_count}");
            if (zonesRoot) go.transform.SetParent(zonesRoot, false);
            go.transform.SetPositionAndRotation(c, rot);
            var zone = go.AddComponent<PlantingZone>();
            string label = _style.lawn ? "Pelouse" : _style.name;
            zone.Setup(label, new List<string>(_style.allowedGroups), s, _style.color, _style.lawn);

            GardenSounds.Play(GardenSound.Place, c);
            if (haptics) haptics.SendHapticImpulse(0.4f, 0.08f);
            if (GardenEditHistory.Instance) GardenEditHistory.Instance.RecordPlace(go); // Y annule la zone
        }

        void DeleteZoneAt(Vector3 p)
        {
            for (int i = PlantingZone.All.Count - 1; i >= 0; i--)
            {
                var z = PlantingZone.All[i];
                if (z == null || !z.Contains(p)) continue;
                GardenSounds.Play(GardenSound.Delete, z.transform.position);
                Destroy(z.gameObject);
                return;
            }
            GardenSounds.PlayAtHead(GardenSound.Error);
        }

        void CancelDraw()
        {
            _drawing = false;
            if (_preview) Destroy(_preview);
            _preview = null;
            if (_style == null || _style.presetSize <= 0f) _sizeLabel = null;
        }

        // ---------- Utilitaires ----------

        Vector3 SnapGrid(Vector3 p)
        {
            if (gridSize <= 0f) return p;
            p.x = Mathf.Round(p.x / gridSize) * gridSize;
            p.z = Mathf.Round(p.z / gridSize) * gridSize;
            return p;
        }

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

        static GameObject CreateQuad(string name, ZoneStyle style, Vector2 tiling)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Destroy(q.GetComponent<Collider>());
            q.layer = LayerMask.NameToLayer("Ignore Raycast");
            q.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var mr = q.GetComponent<MeshRenderer>();
            Color c = style.color; c.a = Mathf.Max(c.a, 0.55f);
            mr.sharedMaterial = style.lawn
                ? PlantingZone.LawnMaterial(tiling, 0.75f)
                : new Material(Shader.Find("Sprites/Default")) { color = c };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return q;
        }

        TMP_Text CreateLabel(Transform follow)
        {
            var go = new GameObject("ZoneSize", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * 0.002f;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = new Vector2(400f, 80f);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = 52;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.outlineWidth = 0.25f;
            t.outlineColor = new Color32(0, 0, 0, 220);
            t.raycastTarget = false;
            var f = go.AddComponent<FollowAbove>();
            f.target = follow;
            return t;
        }

        /// <summary>Garde l'étiquette de dimensions au-dessus de l'aperçu, et la détruit avec lui.</summary>
        class FollowAbove : MonoBehaviour
        {
            public Transform target;
            void LateUpdate()
            {
                if (target == null) { Destroy(gameObject); return; }
                transform.position = target.position + Vector3.up * 0.4f;
            }
        }
    }
}
