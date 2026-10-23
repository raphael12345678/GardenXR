using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

namespace GardenXR
{
    public enum PathPattern { Auto, Gravier, Dallage, TerreBattue, Uni }

    /// <summary>Un type d'allée proposé dans l'onglet « Allées » du catalogue.</summary>
    [Serializable]
    public class PathStyle
    {
        public string name = "Gravier";
        [Tooltip("Texture générée. Auto = déduite du nom (gravier / dallage / terre).")]
        public PathPattern pattern = PathPattern.Auto;
        public Color color = new Color(0.78f, 0.74f, 0.66f);
        [Tooltip("Optionnel : matériau texturé (gravier, dalles...). Sinon couleur unie.")]
        public Material material;
        [Tooltip("Largeur de l'allée en mètres.")]
        public float width = 0.8f;
    }

    /// <summary>
    /// Allée tracée au sol. Mémorise son tracé pour interdire d'y planter.
    /// Ajoutée automatiquement par le PathTool.
    /// </summary>
    public class GardenPath : MonoBehaviour
    {
        public static readonly List<GardenPath> All = new List<GardenPath>();

        public List<Vector3> points = new List<Vector3>();
        public float width = 0.8f;
        public string styleName;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        /// <summary>Vrai si la position (au sol) est sur l'allée.</summary>
        public bool Contains(Vector3 p, float margin = 0f)
        {
            float r = width * 0.5f + margin;
            float r2 = r * r;
            for (int i = 0; i < points.Count - 1; i++)
                if (DistSqXZ(p, points[i], points[i + 1]) <= r2) return true;
            return false;
        }

        public static bool IsOnPath(Vector3 p, float margin = 0f)
        {
            foreach (var g in All)
                if (g && g.Contains(p, margin)) return true;
            return false;
        }

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
    /// Outil de tracé d'allées : choisir un style dans l'onglet « Allées » du catalogue,
    /// puis viser le sol et MAINTENIR la gâchette en déplaçant la main. Relâcher termine l'allée.
    /// On peut enchaîner plusieurs allées ; B (main droite) quitte l'outil. Y annule la dernière allée.
    /// Vibration légère continue pendant le tracé et clic sonore à chaque segment (cf. canevas, section 3).
    /// Un seul exemplaire dans la scène (objet vide "PathTool").
    /// </summary>
    public class PathTool : MonoBehaviour
    {
        public static PathTool Instance { get; private set; }

        [Header("Rayon")]
        [Tooltip("Right Controller du XR Origin.")]
        [SerializeField] Transform rayOrigin;
        [SerializeField] float maxDistance = 30f;
        [Tooltip("Mettre le layer Ground.")]
        [SerializeField] LayerMask groundMask = ~0;
        [SerializeField] HapticImpulsePlayer haptics;

        [Header("Tracé")]
        [Tooltip("Distance entre deux points du tracé (m).")]
        [SerializeField] float pointSpacing = 0.3f;
        [Tooltip("Arrondit les virages.")]
        [SerializeField] bool smooth = true;
        [SerializeField] Transform pathsRoot;

        [Header("Styles")]
        [SerializeField] List<PathStyle> styles = new List<PathStyle>
        {
            new PathStyle { name = "Gravier",      color = new Color(0.80f, 0.76f, 0.68f), width = 0.8f },
            new PathStyle { name = "Dallage",      color = new Color(0.58f, 0.57f, 0.55f), width = 1.0f },
            new PathStyle { name = "Terre battue", color = new Color(0.55f, 0.40f, 0.26f), width = 0.6f },
        };

        public IReadOnlyList<PathStyle> Styles => styles;
        public bool IsActive => _style != null;

        PathStyle _style;
        bool _waitRelease, _drawing, _trigWas, _bWas;
        readonly List<Vector3> _pts = new List<Vector3>();
        GameObject _cursor, _preview;
        Mesh _previewMesh;
        Material _material;
        float _hapticTimer;
        int _count;

        void Awake() => Instance = this;

        // ---------- API ----------

        public void Begin(PathStyle style)
        {
            var placer = FindFirstObjectByType<PrefabPlacer>();
            if (placer) placer.CancelPlacement();
            if (IrrigationNetworkBuilder.Instance) IrrigationNetworkBuilder.Instance.Stop();
            if (ZoneTool.Instance) ZoneTool.Instance.Stop();

            Stop();
            _style = style;
            _material = CreateMaterial(style, 1f);
            _waitRelease = true; // le clic qui a choisi le style ne commence pas un tracé
            _cursor = CreateCursor(style);
        }

        public void Stop()
        {
            CancelStroke();
            if (_cursor) Destroy(_cursor);
            _cursor = null;
            _style = null;
        }

        // ---------- Boucle ----------

        void Update()
        {
            if (!IsActive) return;

            bool trig = ButtonPressed("triggerPressed");
            bool b = ButtonPressed("secondaryButton");

            if (b && !_bWas)
            {
                _bWas = true;
                Stop();
                GardenSounds.PlayAtHead(GardenSound.MenuClose);
                return;
            }
            _bWas = b;

            bool hit = RayGround(out Vector3 p);
            if (_cursor)
            {
                _cursor.SetActive(hit);
                if (hit) _cursor.transform.position = p + Vector3.up * 0.02f;
            }

            if (_waitRelease)
            {
                if (!trig) _waitRelease = false;
                _trigWas = trig;
                return;
            }

            if (trig && !_trigWas && hit)
            {
                // Début d'une allée
                _drawing = true;
                _pts.Clear();
                AddPoint(p);
            }
            else if (trig && _drawing)
            {
                if (hit && FlatDistance(p, _pts[_pts.Count - 1]) >= pointSpacing) AddPoint(p);

                // Vibration légère continue : contact avec le sol virtuel
                _hapticTimer -= Time.deltaTime;
                if (haptics && _hapticTimer <= 0f)
                {
                    haptics.SendHapticImpulse(0.12f, 0.1f);
                    _hapticTimer = 0.1f;
                }
            }
            else if (!trig && _trigWas && _drawing)
            {
                Finish();
            }
            _trigWas = trig;
        }

        void AddPoint(Vector3 p)
        {
            _pts.Add(p);
            if (_pts.Count > 1) GardenSounds.Play(GardenSound.Click, p);
            UpdatePreview();
        }

        void Finish()
        {
            _drawing = false;
            if (_pts.Count < 2)
            {
                CancelStroke();
                GardenSounds.Play(GardenSound.Error, rayOrigin ? rayOrigin.position : transform.position);
                return;
            }

            var pts = smooth ? CleanPath(_pts, _style.width) : new List<Vector3>(_pts);

            var go = new GameObject($"Allee_{_style.name}_{++_count}");
            if (pathsRoot) go.transform.SetParent(pathsRoot, false);
            // Chaque nouvelle allée est posée un poil plus haut que la précédente :
            // là où deux allées se croisent, la plus récente passe dessus (pas de scintillement).
            float lift = 0.015f + 0.002f * (_count % 20);
            var mesh = new Mesh { name = "Allee" };
            FillPathMesh(mesh, pts, _style.width, lift);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = true;

            var path = go.AddComponent<GardenPath>();
            path.points = pts;
            path.width = _style.width;
            path.styleName = _style.name;

            CancelStroke();
            GardenSounds.Play(GardenSound.Place, pts[pts.Count / 2]);
            if (haptics) haptics.SendHapticImpulse(0.4f, 0.08f);
            if (GardenEditHistory.Instance) GardenEditHistory.Instance.RecordPlace(go); // Y annule l'allée
        }

        void CancelStroke()
        {
            _drawing = false;
            _pts.Clear();
            if (_preview) Destroy(_preview);
            _preview = null;
        }

        // ---------- Aperçu ----------

        void UpdatePreview()
        {
            if (_pts.Count < 2) return;
            if (_preview == null)
            {
                _preview = new GameObject("AlleePreview");
                _previewMesh = new Mesh { name = "AlleePreview" };
                _preview.AddComponent<MeshFilter>().sharedMesh = _previewMesh;
                var mr = _preview.AddComponent<MeshRenderer>();
                mr.sharedMaterial = CreateMaterial(_style, 0.6f, transparent: true);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // L'aperçu montre déjà le tracé nettoyé (lignes droites + virages arrondis)
            var pts = smooth ? CleanPath(_pts, _style.width) : _pts;
            FillPathMesh(_previewMesh, pts, _style.width, 0.06f); // au-dessus de tout
        }

        // ---------- Géométrie ----------

        /// <summary>
        /// Nettoie le tracé à main levée :
        /// 1. ré-échantillonnage régulier ;
        /// 2. simplification (Ramer-Douglas-Peucker) : le tremblement de la main disparaît,
        ///    les portions presque droites deviennent des lignes droites ;
        /// 3. chaque angle est remplacé par un arc de cercle (rayon ≥ 0,75 × largeur) :
        ///    virages réguliers, sans pli du ruban côté intérieur ;
        /// 4. ré-échantillonnage fin pour des courbes lisses.
        /// </summary>
        public static List<Vector3> CleanPath(List<Vector3> raw, float width)
        {
            if (raw.Count < 2) return new List<Vector3>(raw);
            var pts = Resample(raw, 0.1f);
            float tolerance = Mathf.Clamp(width * 0.18f, 0.06f, 0.25f);
            pts = Simplify(pts, tolerance);
            pts = Fillet(pts, Mathf.Max(width * 0.75f, 0.45f));
            return Resample(pts, 0.08f);
        }

        static List<Vector3> Resample(List<Vector3> src, float step)
        {
            var o = new List<Vector3> { src[0] };
            float carry = 0f;
            for (int i = 1; i < src.Count; i++)
            {
                Vector3 a = src[i - 1], b = src[i];
                float seg = FlatDistance(a, b);
                if (seg < 1e-5f) continue;
                float t = step - carry;
                while (t <= seg)
                {
                    o.Add(Vector3.Lerp(a, b, t / seg));
                    t += step;
                }
                carry = seg - (t - step);
            }
            if (FlatDistance(o[o.Count - 1], src[src.Count - 1]) > step * 0.25f) o.Add(src[src.Count - 1]);
            else o[o.Count - 1] = src[src.Count - 1];
            return o;
        }

        static List<Vector3> Simplify(List<Vector3> pts, float tol)
        {
            if (pts.Count < 3) return new List<Vector3>(pts);
            var keep = new bool[pts.Count];
            keep[0] = keep[pts.Count - 1] = true;
            var stack = new Stack<(int, int)>();
            stack.Push((0, pts.Count - 1));
            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                float maxD = 0f; int idx = -1;
                for (int i = a + 1; i < b; i++)
                {
                    float d = DistToSegmentXZ(pts[i], pts[a], pts[b]);
                    if (d > maxD) { maxD = d; idx = i; }
                }
                if (idx >= 0 && maxD > tol)
                {
                    keep[idx] = true;
                    stack.Push((a, idx));
                    stack.Push((idx, b));
                }
            }
            var o = new List<Vector3>();
            for (int i = 0; i < pts.Count; i++) if (keep[i]) o.Add(pts[i]);
            return o;
        }

        static List<Vector3> Fillet(List<Vector3> pts, float radius)
        {
            if (pts.Count < 3) return new List<Vector3>(pts);
            var o = new List<Vector3> { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Vector3 P = pts[i];
                Vector3 toA = Flat(pts[i - 1] - P), toB = Flat(pts[i + 1] - P);
                float la = toA.magnitude, lb = toB.magnitude;
                if (la < 1e-4f || lb < 1e-4f) continue;
                Vector3 u = toA / la, v = toB / lb;
                float theta = Vector3.Angle(u, v) * Mathf.Deg2Rad; // angle intérieur
                if (theta > Mathf.PI * 0.97f) { o.Add(P); continue; } // presque droit

                float half = theta * 0.5f;
                float t = radius / Mathf.Tan(half);
                float tMax = Mathf.Min(la, lb) * 0.5f;
                if (t > tMax) t = tMax;
                float r = t * Mathf.Tan(half);

                Vector3 t1 = P + u * t, t2 = P + v * t;
                Vector3 bis = (u + v).normalized;
                Vector3 c = P + bis * (r / Mathf.Sin(half));

                Vector3 a1 = Flat(t1 - c), a2 = Flat(t2 - c);
                float sweep = Vector3.SignedAngle(a1, a2, Vector3.up);
                int steps = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(sweep) / 8f));
                for (int k = 0; k <= steps; k++)
                {
                    Vector3 d = Quaternion.AngleAxis(sweep * k / steps, Vector3.up) * a1;
                    Vector3 q = c + d;
                    q.y = P.y;
                    o.Add(q);
                }
            }
            o.Add(pts[pts.Count - 1]);
            return o;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        static float DistToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            var P = new Vector2(p.x, p.z); var A = new Vector2(a.x, a.z); var AB = new Vector2(b.x, b.z) - A;
            float t = AB.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(P - A, AB) / AB.sqrMagnitude) : 0f;
            return (A + AB * t - P).magnitude;
        }

        /// <summary>
        /// Ruban de l'allée + embouts arrondis aux deux extrémités.
        /// UV : u de 0 à 1 sur la largeur (bordures de la texture sur les bords), v = distance / largeur.
        /// </summary>
        static void FillPathMesh(Mesh mesh, List<Vector3> pts, float width, float lift)
        {
            int n = pts.Count;
            if (n < 2) { mesh.Clear(); return; }
            const int capSeg = 10;
            float half = width * 0.5f;

            var verts = new List<Vector3>(n * 2 + capSeg * 2 + 4);
            var uvs = new List<Vector2>(verts.Capacity);
            var tris = new List<int>((n - 1) * 6 + capSeg * 6);
            var dists = new float[n];
            var sides = new Vector3[n];

            for (int i = 0; i < n; i++)
            {
                Vector3 prev = pts[Mathf.Max(0, i - 1)], next = pts[Mathf.Min(n - 1, i + 1)];
                Vector3 dir = Flat(next - prev);
                if (dir.sqrMagnitude < 1e-8f) dir = Vector3.forward;
                dir.Normalize();
                sides[i] = new Vector3(-dir.z, 0f, dir.x);
                if (i > 0) dists[i] = dists[i - 1] + FlatDistance(pts[i], pts[i - 1]);

                Vector3 p = pts[i] + Vector3.up * lift;
                verts.Add(p + sides[i] * half); uvs.Add(new Vector2(0f, dists[i] / width));
                verts.Add(p - sides[i] * half); uvs.Add(new Vector2(1f, dists[i] / width));
            }
            for (int i = 0; i < n - 1; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }

            AddCap(verts, uvs, tris, pts[0], sides[0], -1f, dists[0], half, width, lift, capSeg);
            AddCap(verts, uvs, tris, pts[n - 1], sides[n - 1], 1f, dists[n - 1], half, width, lift, capSeg);

            var normals = new Vector3[verts.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;

            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.normals = normals;
            mesh.RecalculateBounds();
        }

        /// <summary>Demi-disque à une extrémité. dirSign = -1 au début (vers l'arrière), +1 à la fin.</summary>
        static void AddCap(List<Vector3> verts, List<Vector2> uvs, List<int> tris, Vector3 center, Vector3 side,
                           float dirSign, float dist, float half, float width, float lift, int seg)
        {
            Vector3 fwd = new Vector3(side.z, 0f, -side.x) * -dirSign; // direction sortante
            int ci = verts.Count;
            verts.Add(center + Vector3.up * lift);
            uvs.Add(new Vector2(0.5f, dist / width));
            for (int k = 0; k <= seg; k++)
            {
                float a = Mathf.PI * k / seg;
                Vector3 off = side * Mathf.Cos(a) + fwd * Mathf.Sin(a);
                verts.Add(center + off * half + Vector3.up * lift);
                float uSide = Vector3.Dot(off, side);   // +1 = côté u=0
                float along = Vector3.Dot(off, fwd) * dirSign;
                uvs.Add(new Vector2(0.5f - uSide * 0.5f, (dist + along * half) / width));
            }
            for (int k = 0; k < seg; k++)
            {
                if (dirSign > 0f) { tris.Add(ci); tris.Add(ci + 1 + k); tris.Add(ci + 2 + k); }
                else { tris.Add(ci); tris.Add(ci + 2 + k); tris.Add(ci + 1 + k); }
            }
        }

        /// <summary>
        /// Ruban plat qui suit le tracé, légèrement au-dessus du sol, UV répétées sur la longueur.
        /// Les normales sont forcées vers le haut : dans un virage serré ou un aller-retour,
        /// le ruban se replie sur lui-même ; avec des normales calculées, les parties repliées
        /// apparaissaient noires. Vers le haut, tout reçoit la même lumière.
        /// </summary>
        public static void FillRibbon(Mesh mesh, List<Vector3> pts, float width, float lift)
        {
            int n = pts.Count;
            var verts = new Vector3[n * 2];
            var normals = new Vector3[n * 2];
            var uvs = new Vector2[n * 2];
            var tris = new int[(n - 1) * 6];
            float half = width * 0.5f, dist = 0f;

            for (int i = 0; i < n; i++)
            {
                Vector3 prev = pts[Mathf.Max(0, i - 1)];
                Vector3 next = pts[Mathf.Min(n - 1, i + 1)];
                Vector3 dir = next - prev; dir.y = 0f;
                if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
                dir.Normalize();
                Vector3 side = new Vector3(-dir.z, 0f, dir.x) * half;

                if (i > 0) dist += FlatDistance(pts[i], pts[i - 1]);
                Vector3 p = pts[i] + Vector3.up * lift; // au-dessus du sol et des zones
                verts[i * 2] = p + side;
                verts[i * 2 + 1] = p - side;
                normals[i * 2] = normals[i * 2 + 1] = Vector3.up;
                uvs[i * 2] = new Vector2(0f, dist / width);
                uvs[i * 2 + 1] = new Vector2(1f, dist / width);
            }

            for (int i = 0; i < n - 1; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3, t = i * 6;
                tris[t] = a; tris[t + 1] = c; tris[t + 2] = b;
                tris[t + 3] = b; tris[t + 4] = c; tris[t + 5] = d;
            }

            mesh.Clear();
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.normals = normals;
            mesh.RecalculateBounds();
        }

        /// <summary>Arrondit les angles (algorithme de Chaikin), en gardant le premier et le dernier point.</summary>
        static List<Vector3> Chaikin(List<Vector3> src, int iterations)
        {
            var pts = new List<Vector3>(src);
            for (int it = 0; it < iterations && pts.Count > 2; it++)
            {
                var o = new List<Vector3> { pts[0] };
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    o.Add(Vector3.Lerp(pts[i], pts[i + 1], 0.25f));
                    o.Add(Vector3.Lerp(pts[i], pts[i + 1], 0.75f));
                }
                o.Add(pts[pts.Count - 1]);
                pts = o;
            }
            return pts;
        }

        static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

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

        static bool ButtonPressed(string control)
        {
            var c = XRController.rightHand?.TryGetChildControl<ButtonControl>(control);
            return c != null && c.isPressed;
        }

        static Material CreateMaterial(PathStyle style, float alpha, bool transparent = false)
        {
            if (style.material && !transparent) return style.material;

            var pattern = PathTextures.Resolve(style);
            Texture2D tex = PathTextures.Get(pattern);

            if (transparent)
            {
                Color pc = tex ? Color.white : style.color; pc.a = alpha;
                return new Material(Shader.Find("Sprites/Default")) { color = pc, mainTexture = tex };
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            Color col = tex ? Color.white : style.color;
            var m = new Material(shader) { color = col };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            if (tex)
            {
                m.mainTexture = tex;
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            }
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", pattern == PathPattern.Dallage ? 0.28f : 0.08f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f); // visible des deux côtés
            return m;
        }

        GameObject CreateCursor(PathStyle style)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            c.name = "AlleeCursor";
            Destroy(c.GetComponent<Collider>());
            c.layer = LayerMask.NameToLayer("Ignore Raycast");
            c.transform.localScale = new Vector3(style.width, 0.005f, style.width);
            var mr = c.GetComponent<MeshRenderer>();
            mr.sharedMaterial = CreateMaterial(style, 0.6f, transparent: true);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            c.SetActive(false);
            return c;
        }
    }
}
