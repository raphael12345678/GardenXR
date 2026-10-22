using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace GardenXR
{
    /// <summary>
    /// Zone rectangulaire au sol où seuls certains groupes du catalogue peuvent être posés
    /// (ex. « Massif de fleurs » : Fleurs uniquement ; « Allée » : Autre uniquement).
    /// Le ghost passe au rouge et la pose est refusée pour un groupe non autorisé.
    /// Pas de collider : la zone est définie par la position, la rotation Y et la taille.
    /// </summary>
    public class PlantingZone : MonoBehaviour
    {
        /// <summary>Toutes les zones actives de la scène.</summary>
        public static readonly List<PlantingZone> All = new List<PlantingZone>();

        /// <summary>Peut-on poser librement en dehors de toute zone ?</summary>
        public static bool AllowOutsideZones = true;

        [Header("Règle")]
        [SerializeField] string zoneName = "Massif";
        [Tooltip("Noms des groupes du catalogue autorisés ici (ex. Plantes, Fleurs). Vide = tout est autorisé.")]
        [SerializeField] List<string> allowedGroups = new List<string> { "Plantes" };

        [Header("Taille (mètres)")]
        [SerializeField] Vector2 size = new Vector2(2f, 2f);

        [Header("Apparence")]
        [SerializeField] Color color = new Color(0.45f, 0.3f, 0.15f, 0.45f); // terre
        [SerializeField] bool showLabel = true;
        [SerializeField] float labelHeight = 0.6f;

        Transform _label;
        Transform _head;

        public string ZoneName => zoneName;
        public IReadOnlyList<string> AllowedGroups => allowedGroups;

        // ---------- Règles ----------

        public bool Contains(Vector3 worldPos)
        {
            Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, transform.eulerAngles.y, 0f)) * (worldPos - transform.position);
            return Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.z) <= size.y * 0.5f;
        }

        /// <summary>Point le plus proche sur le bord de la zone (utilisé pour le snap des tuyaux d'irrigation).</summary>
        public Vector3 ClosestPointOnBorder(Vector3 worldPos)
        {
            var rot = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            Vector3 l = Quaternion.Inverse(rot) * (worldPos - transform.position);
            float hx = size.x * 0.5f, hz = size.y * 0.5f;
            float cx = Mathf.Clamp(l.x, -hx, hx), cz = Mathf.Clamp(l.z, -hz, hz);

            if (Mathf.Abs(l.x) <= hx && Mathf.Abs(l.z) <= hz)
            {
                // À l'intérieur : on pousse vers le bord le plus proche
                if (hx - Mathf.Abs(l.x) < hz - Mathf.Abs(l.z)) cx = (l.x >= 0f ? 1f : -1f) * hx;
                else cz = (l.z >= 0f ? 1f : -1f) * hz;
            }
            return transform.position + rot * new Vector3(cx, l.y, cz);
        }

        public bool Allows(GardenItemData item)
        {
            if (allowedGroups == null || allowedGroups.Count == 0) return true;
            if (item == null) return false;
            foreach (var g in allowedGroups)
                if (string.Equals(g.Trim(), item.groupName, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Vrai si cet élément peut être posé à cette position, compte tenu de toutes les zones.</summary>
        public static bool IsAllowed(GardenItemData item, Vector3 worldPos)
        {
            // Pas de plante sur une allée (la décoration reste autorisée)
            if (item != null && item.IsPlant && GardenPath.IsOnPath(worldPos)) return false;

            bool insideAny = false;
            foreach (var z in All)
            {
                if (!z.Contains(worldPos)) continue;
                insideAny = true;
                if (z.Allows(item)) return true;
            }
            return !insideAny && AllowOutsideZones;
        }

        // ---------- Cycle de vie ----------

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        void Start()
        {
            if (Camera.main) _head = Camera.main.transform;
            BuildVisual();
        }

        void LateUpdate()
        {
            if (_label && _head)
                _label.rotation = Quaternion.LookRotation(_label.position - _head.position);
        }

        // ---------- Visuel ----------

        void BuildVisual()
        {
            var shader = Shader.Find("Sprites/Default"); // transparent, non éclairé, compatible URP
            var mat = new Material(shader) { color = color };

            // Surface colorée
            var fill = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fill.name = "ZoneFill";
            Destroy(fill.GetComponent<Collider>()); // ne doit pas bloquer les rayons
            fill.transform.SetParent(transform, false);
            fill.transform.localPosition = new Vector3(0f, 0.01f, 0f);
            fill.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            fill.transform.localScale = new Vector3(size.x, size.y, 1f);
            var fr = fill.GetComponent<MeshRenderer>();
            fr.sharedMaterial = mat;
            fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Bordure
            var border = new GameObject("ZoneBorder").AddComponent<LineRenderer>();
            border.transform.SetParent(transform, false);
            border.useWorldSpace = false;
            border.loop = true;
            border.widthMultiplier = 0.03f;
            border.sharedMaterial = mat;
            Color edge = color; edge.a = 1f;
            border.startColor = border.endColor = edge;
            border.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            float hx = size.x * 0.5f, hz = size.y * 0.5f, y = 0.02f;
            border.positionCount = 4;
            border.SetPositions(new[]
            {
                new Vector3(-hx, y, -hz), new Vector3(-hx, y, hz),
                new Vector3(hx, y, hz),   new Vector3(hx, y, -hz)
            });

            // Étiquette
            if (showLabel)
            {
                var go = new GameObject("ZoneLabel", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, labelHeight, 0f);
                go.transform.localScale = Vector3.one * 0.0025f;
                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                ((RectTransform)go.transform).sizeDelta = new Vector2(400f, 100f);

                var t = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
                t.transform.SetParent(go.transform, false);
                var rt = (RectTransform)t.transform;
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                string rule = allowedGroups == null || allowedGroups.Count == 0
                    ? "Tout est autorisé"
                    : string.Join(", ", allowedGroups) + " uniquement";
                t.text = $"<b>{zoneName}</b>\n<size=70%>{rule}</size>";
                t.fontSize = 40;
                t.alignment = TextAlignmentOptions.Center;
                t.color = Color.white;
                t.outlineWidth = 0.2f;
                t.outlineColor = new Color32(0, 0, 0, 200);
                t.raycastTarget = false;
                _label = go.transform;
            }
        }

        // Aperçu dans la vue Scene
        void OnDrawGizmos()
        {
            Gizmos.color = color;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, Quaternion.Euler(0f, transform.eulerAngles.y, 0f), Vector3.one);
            Gizmos.DrawCube(new Vector3(0f, 0.01f, 0f), new Vector3(size.x, 0.02f, size.y));
            Color edge = color; edge.a = 1f;
            Gizmos.color = edge;
            Gizmos.DrawWireCube(new Vector3(0f, 0.01f, 0f), new Vector3(size.x, 0.02f, size.y));
        }
    }
}
