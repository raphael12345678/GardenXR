using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics; // XRI 3.x

namespace GardenXR
{
    /// <summary>
    /// Ghost preview qui suit le rayon de la main → Trigger pour poser → snap grille optionnel.
    /// Séquence 3I² : Grab (sélection catalogue) → Ghost preview → Release → Snap.
    /// </summary>
    public class PrefabPlacer : MonoBehaviour
    {
        [Header("Rayon")]
        [Tooltip("Origine du rayon : 'Right Controller' du XR Origin (ou son enfant 'Near-Far Interactor').")]
        [SerializeField] Transform rayOrigin;
        [SerializeField] float maxDistance = 30f;
        [SerializeField] LayerMask groundMask = ~0;
        [Tooltip("Composant 'Haptic Impulse Player' du Right Controller (optionnel).")]
        [SerializeField] HapticImpulsePlayer haptics;

        [Header("Entrées")]
        [Tooltip("Ex. : <XRController>{RightHand}/triggerPressed")]
        [SerializeField] InputActionProperty confirmAction;
        [Tooltip("Ex. : <XRController>{RightHand}/secondaryButton (B)")]
        [SerializeField] InputActionProperty cancelAction;
        [Tooltip("Ex. : <XRController>{RightHand}/primary2DAxis")]
        [SerializeField] InputActionProperty rotateAction;
        [SerializeField] float rotateSpeed = 120f;
        [Tooltip("Rester en mode placement après une pose (pratique pour les massifs).")]
        [SerializeField] bool keepPlacing = false;

        [Header("Ghost")]
        [SerializeField] Material ghostValidMaterial;   // vert semi-transparent
        [SerializeField] Material ghostInvalidMaterial; // rouge semi-transparent

        [Header("Snap")]
        [SerializeField] bool snapToGrid = true;
        [SerializeField] float gridSize = 0.25f;

        [Header("Feedback")]
        [SerializeField] AudioSource audioSource;
        [SerializeField] AudioClip placeClip;
        [SerializeField] AudioClip errorClip;

        [Header("Sortie")]
        [SerializeField] Transform placedItemsRoot;

        GardenItemData _current;
        GameObject _ghost;
        Renderer[] _ghostRenderers;
        float _yaw;
        bool _validPose;
        bool _waitForRelease; // évite de poser avec le même clic qui a sélectionné le bouton

        public bool IsPlacing => _current != null;

        void OnEnable()
        {
            confirmAction.action?.Enable();
            cancelAction.action?.Enable();
            rotateAction.action?.Enable();
        }

        public void BeginPlacement(GardenItemData item)
        {
            CancelPlacement();
            _current = item;
            _yaw = 0f;
            _waitForRelease = true;
            _ghost = CreateGhost(item.prefab);
        }

        public void CancelPlacement()
        {
            if (_ghost) Destroy(_ghost);
            _ghost = null;
            _current = null;
        }

        void Update()
        {
            if (!IsPlacing) return;

            bool confirmPressed = confirmAction.action != null && confirmAction.action.IsPressed();
            if (_waitForRelease) { if (!confirmPressed) _waitForRelease = false; }

            if (cancelAction.action != null && cancelAction.action.WasPressedThisFrame())
            {
                CancelPlacement();
                return;
            }

            if (rotateAction.action != null)
            {
                Vector2 stick = rotateAction.action.ReadValue<Vector2>();
                if (Mathf.Abs(stick.x) > 0.3f) _yaw += stick.x * rotateSpeed * Time.deltaTime;
            }

            UpdateGhostPose();

            if (!_waitForRelease && confirmAction.action != null && confirmAction.action.WasPressedThisFrame())
                TryPlace();
        }

        void UpdateGhostPose()
        {
            RaycastHit rh = default;
            bool hit = rayOrigin != null && Physics.Raycast(
                rayOrigin.position, rayOrigin.forward, out rh,
                maxDistance, groundMask, QueryTriggerInteraction.Ignore);

            _validPose = hit;
            _ghost.SetActive(hit);
            if (!_validPose) return;

            Vector3 pos = rh.point;
            if (snapToGrid && gridSize > 0f)
            {
                pos.x = Mathf.Round(pos.x / gridSize) * gridSize;
                pos.z = Mathf.Round(pos.z / gridSize) * gridSize;
            }
            pos.y += _current.yOffset;

            _ghost.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, _yaw, 0f));
            _validPose = Vector3.Angle(rh.normal, Vector3.up) < 35f; // pente trop forte = invalide
            SetGhostMaterial(_validPose);
        }

        void TryPlace()
        {
            if (!_validPose)
            {
                Play(errorClip);
                return;
            }

            var placed = SpawnItem(_current, _ghost.transform.position, _ghost.transform.rotation);
            Play(placeClip);
            if (haptics) haptics.SendHapticImpulse(0.4f, 0.08f);

            if (!keepPlacing) CancelPlacement();
        }

        /// <summary>Même signature que GardenItemSpawner.SpawnItem du document de conception.</summary>
        public GameObject SpawnItem(GardenItemData itemData, Vector3 position, Quaternion rotation)
        {
            var go = Instantiate(itemData.prefab, position, rotation, placedItemsRoot);
            go.name = itemData.displayName;
            return go;
        }

        GameObject CreateGhost(GameObject prefab)
        {
            var ghost = Instantiate(prefab);
            ghost.name = "Ghost_" + prefab.name;

            // Le ghost ne doit ni collisionner ni être saisissable, ni intercepter le rayon
            foreach (var c in ghost.GetComponentsInChildren<Collider>()) c.enabled = false;
            foreach (var rb in ghost.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
            foreach (var mb in ghost.GetComponentsInChildren<MonoBehaviour>()) mb.enabled = false;
            SetLayerRecursive(ghost, LayerMask.NameToLayer("Ignore Raycast"));

            _ghostRenderers = ghost.GetComponentsInChildren<Renderer>();
            _lastValid = null;
            SetGhostMaterial(true);
            return ghost;
        }

        bool? _lastValid;
        void SetGhostMaterial(bool valid)
        {
            if (_ghostRenderers == null || _lastValid == valid) return;
            _lastValid = valid;

            var mat = valid ? ghostValidMaterial : ghostInvalidMaterial;
            if (mat == null) return;
            foreach (var r in _ghostRenderers)
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            if (layer < 0) return;
            go.layer = layer;
            foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
        }

        void Play(AudioClip clip)
        {
            if (audioSource && clip) audioSource.PlayOneShot(clip);
        }
    }
}