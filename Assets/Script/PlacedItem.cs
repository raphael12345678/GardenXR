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

        public GardenItemData Data { get; private set; }
        public XRGrabInteractable Grab { get; private set; }

        LayerMask _groundMask;
        float _gridSize;
        Vector3 _grabStartPos;
        Quaternion _grabStartRot;
        bool _deleting;

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
        }

        void OnEnable() => _deleting = false;

        public void MarkDeleting() => _deleting = true;

        void OnGrab(SelectEnterEventArgs _)
        {
            Held = this;
            _grabStartPos = transform.position;
            _grabStartRot = transform.rotation;
        }

        void OnRelease(SelectExitEventArgs _)
        {
            if (Held == this) Held = null;
            if (_deleting) return; // supprimé pendant qu'il était tenu

            SnapToGround();

            if (GardenEditHistory.Instance)
                GardenEditHistory.Instance.RecordMove(transform, _grabStartPos, _grabStartRot);
        }

        /// <summary>Remet l'objet droit (rotation Y seulement), sur la grille et posé sur le sol.</summary>
        public void SnapToGround()
        {
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

            Vector3 p = transform.position;
            if (_gridSize > 0f)
            {
                p.x = Mathf.Round(p.x / _gridSize) * _gridSize;
                p.z = Mathf.Round(p.z / _gridSize) * _gridSize;
            }

            if (Physics.Raycast(new Vector3(p.x, p.y + 50f, p.z), Vector3.down, out RaycastHit hit,
                                200f, _groundMask, QueryTriggerInteraction.Ignore))
            {
                p.y = hit.point.y + (Data ? Data.yOffset : 0f);
            }

            transform.position = p;
        }

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
