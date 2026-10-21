using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace GardenXR
{
    /// <summary>
    /// Ajouté automatiquement à chaque objet posé.
    /// Le rend saisissable (Grip, de près ou à distance), le garde droit et le recolle au sol
    /// quand on le lâche, et enregistre le déplacement pour pouvoir l'annuler.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlacedItem : MonoBehaviour
    {
        /// <summary>Objet actuellement tenu en main (null sinon).</summary>
        public static PlacedItem Held { get; private set; }

        /// <summary>Tous les objets posés et actifs (utilisé par l'analyse d'ensoleillement).</summary>
        public static readonly System.Collections.Generic.List<PlacedItem> All = new System.Collections.Generic.List<PlacedItem>();

        public GardenItemData Data { get; private set; }
        public XRGrabInteractable Grab { get; private set; }

        // ---------- Highlight au survol (ALC « highlight d'objet manipulable ») ----------
        public static float HoverScale = 1.06f;                                // léger scale-up
        public static Color HoverTint = new Color(1.35f, 1.45f, 1.2f, 1f);     // éclaircissement vert doux
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");   // URP Lit
        static readonly int ColorId = Shader.PropertyToID("_Color");           // shaders Built-in

        LayerMask _groundMask;
        float _gridSize;
        Vector3 _grabStartPos;
        Quaternion _grabStartRot;
        bool _deleting;

        Renderer[] _renderers;
        MaterialPropertyBlock _mpb;
        Vector3 _baseScale;
        int _hoverCount;      // plusieurs mains peuvent survoler en même temps
        bool _highlighted;

        public void Init(GardenItemData data, LayerMask groundMask, float gridSize)
        {
            Data = data;
            _groundMask = groundMask;
            _gridSize = gridSize;

            EnsureCollider(); // doit exister AVANT l'ajout du XRGrabInteractable

            var rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            Grab = gameObject.AddComponent<XRGrabInteractable>();
            Grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            Grab.throwOnDetach = false;
            Grab.useDynamicAttach = true; // l'objet ne "saute" pas dans la main
            Grab.selectEntered.AddListener(OnGrab);
            Grab.selectExited.AddListener(OnRelease);
            Grab.hoverEntered.AddListener(OnHoverEnter);
            Grab.hoverExited.AddListener(OnHoverExit);

            _renderers = GetComponentsInChildren<Renderer>();
            foreach (var r in _renderers)
            {
                // Toutes les plantes projettent et reçoivent des ombres (simulation solaire)
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
            }
            _mpb = new MaterialPropertyBlock();
            _baseScale = transform.localScale;
        }

        void OnEnable()
        {
            _deleting = false;
            if (!All.Contains(this)) All.Add(this);
        }

        /// <summary>Boîte englobante de l'objet (tous ses renderers).</summary>
        public Bounds GetBounds()
        {
            if (_renderers == null || _renderers.Length == 0) return new Bounds(transform.position, Vector3.one * 0.3f);
            Bounds b = _renderers[0].bounds;
            foreach (var r in _renderers) if (r) b.Encapsulate(r.bounds);
            return b;
        }

        void OnDisable()
        {
            All.Remove(this);
            if (PlantInsights.Instance) PlantInsights.Instance.SetHovered(this, false);
            // Supprimé ou désactivé : on retire proprement le highlight
            _hoverCount = 0;
            SetHighlight(false);
            if (_mpb != null) transform.localScale = _baseScale;
        }

        void Update()
        {
            if (_mpb == null) return;
            // Scale-up uniquement au survol, pas pendant la saisie (évite de modifier la taille finale)
            bool grow = _highlighted && !(Grab && Grab.isSelected);
            Vector3 target = grow ? _baseScale * HoverScale : _baseScale;
            transform.localScale = Vector3.Lerp(transform.localScale, target, Time.deltaTime * 18f);
        }

        void OnHoverEnter(HoverEnterEventArgs _)
        {
            _hoverCount++;
            SetHighlight(true);
            if (PlantInsights.Instance) PlantInsights.Instance.SetHovered(this, true);
        }

        void OnHoverExit(HoverExitEventArgs _)
        {
            _hoverCount = Mathf.Max(0, _hoverCount - 1);
            if (_hoverCount == 0)
            {
                SetHighlight(false);
                if (PlantInsights.Instance) PlantInsights.Instance.SetHovered(this, false);
            }
        }

        void SetHighlight(bool on)
        {
            if (_highlighted == on || _renderers == null) return;
            _highlighted = on;

            foreach (var r in _renderers)
            {
                if (!r) continue;
                if (on)
                {
                    r.GetPropertyBlock(_mpb);
                    _mpb.SetColor(BaseColorId, HoverTint);
                    _mpb.SetColor(ColorId, HoverTint);
                    r.SetPropertyBlock(_mpb);
                }
                else
                {
                    r.SetPropertyBlock(null); // retour aux couleurs du matériau
                }
            }
        }

        public void MarkDeleting() => _deleting = true;

        void OnGrab(SelectEnterEventArgs _)
        {
            Held = this;
            _grabStartPos = transform.position;
            _grabStartRot = transform.rotation;
            GardenSounds.Play(GardenSound.Grab, transform.position);
        }

        void OnRelease(SelectExitEventArgs _)
        {
            if (Held == this) Held = null;
            if (_deleting) return; // supprimé pendant qu'il était tenu

            if (!SnapToGround())
            {
                // Lâché hors du terrain : on le pose au point valide le plus proche du lâcher,
                // en remontant vers sa position d'avant (qui est toujours valide en dernier recours).
                PlaceNearestValid(transform.position, _grabStartPos);
                GardenSounds.Play(GardenSound.Error, transform.position);
            }
            else
            {
                GardenSounds.Play(GardenSound.Release, transform.position);
            }

            if (GardenEditHistory.Instance)
                GardenEditHistory.Instance.RecordMove(transform, _grabStartPos, _grabStartRot);
        }

        /// <summary>
        /// Remet l'objet droit (rotation Y seulement), sur la grille et posé sur le sol.
        /// Renvoie false s'il n'y a pas de sol sous l'objet (hors du terrain) : rien n'est modifié.
        /// </summary>
        public bool SnapToGround() => TrySnapAt(transform.position);

        /// <summary>Essaie de poser l'objet au sol à la verticale de 'position'. False si pas de sol.</summary>
        bool TrySnapAt(Vector3 position)
        {
            Vector3 p = position;
            if (_gridSize > 0f)
            {
                p.x = Mathf.Round(p.x / _gridSize) * _gridSize;
                p.z = Mathf.Round(p.z / _gridSize) * _gridSize;
            }

            if (!Physics.Raycast(new Vector3(p.x, p.y + 50f, p.z), Vector3.down, out RaycastHit hit,
                                 200f, _groundMask, QueryTriggerInteraction.Ignore))
                return false;

            p.y = hit.point.y + (Data ? Data.yOffset : 0f);
            transform.SetPositionAndRotation(p, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
            return true;
        }

        /// <summary>
        /// Parcourt le segment [lâcher → position d'avant] et pose l'objet sur le premier point
        /// qui a du sol, avec une petite marge pour ne pas rester collé au bord.
        /// </summary>
        void PlaceNearestValid(Vector3 dropPos, Vector3 previousPos)
        {
            const int steps = 40;
            const float edgeMargin = 0.15f; // mètres vers l'intérieur du terrain

            Vector3 start = new Vector3(dropPos.x, previousPos.y, dropPos.z);
            Vector3 dir = previousPos - start;
            float length = dir.magnitude;

            if (length > 0.001f)
            {
                dir /= length;
                for (int i = 0; i <= steps; i++)
                {
                    float d = length * i / steps;
                    Vector3 candidate = start + dir * d;
                    if (!HasGround(candidate)) continue;

                    // Marge vers l'intérieur, sans dépasser la position d'avant
                    Vector3 inside = start + dir * Mathf.Min(length, d + edgeMargin);
                    if (TrySnapAt(HasGround(inside) ? inside : candidate)) return;
                }
            }

            // Dernier recours : position d'avant la saisie
            transform.SetPositionAndRotation(previousPos, _grabStartRot);
        }

        bool HasGround(Vector3 p) =>
            Physics.Raycast(new Vector3(p.x, p.y + 50f, p.z), Vector3.down, 200f, _groundMask, QueryTriggerInteraction.Ignore);

        /// <summary>Beaucoup de prefabs décoratifs n'ont pas de collider : on en crée un à la taille du modèle.</summary>
        void EnsureCollider()
        {
            if (GetComponentInChildren<Collider>() != null) return;

            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            // Calcul des bornes sans rotation pour une boîte bien ajustée
            Quaternion rot = transform.rotation;
            transform.rotation = Quaternion.identity;

            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);

            var box = gameObject.AddComponent<BoxCollider>();
            box.center = transform.InverseTransformPoint(b.center);
            Vector3 s = transform.lossyScale;
            box.size = new Vector3(
                b.size.x / Mathf.Max(Mathf.Abs(s.x), 0.0001f),
                b.size.y / Mathf.Max(Mathf.Abs(s.y), 0.0001f),
                b.size.z / Mathf.Max(Mathf.Abs(s.z), 0.0001f));

            transform.rotation = rot;
        }
    }
}
