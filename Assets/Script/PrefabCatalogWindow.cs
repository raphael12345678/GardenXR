using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace GardenXR
{
    /// <summary>
    /// Fenêtre canvas world-space qui s'ouvre devant l'utilisateur et liste tous les prefabs
    /// du catalogue. Un clic sur un bouton lance le placement via PrefabPlacer.
    ///
    /// Hiérarchie attendue :
    ///   CatalogWindow (Canvas World Space + ce script)
    ///     └ Panel
    ///         ├ Tabs (HorizontalLayoutGroup)          -> tabContainer (optionnel)
    ///         └ Scroll View (ScrollRect)
    ///             └ Viewport / Content (GridLayoutGroup) -> contentRoot
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class PrefabCatalogWindow : MonoBehaviour
    {
        [Header("Données")]
        [SerializeField] GardenCatalog catalog;
        [SerializeField] PrefabPlacer placer;

        [Header("UI")]
        [SerializeField] Transform contentRoot;
        [Tooltip("Bouton modèle : Button + Image (enfant 'Icon') + TMP_Text (enfant 'Label'). Désactivé dans la scène.")]
        [SerializeField] Button itemButtonTemplate;
        [SerializeField] Transform tabContainer;
        [SerializeField] Button tabButtonTemplate;

        [Header("Vignettes")]
        [Tooltip("Génère une image 3D de chaque prefab quand il n'a pas d'icône.")]
        [SerializeField] bool autoThumbnails = true;
        [SerializeField] int thumbnailSize = 256;
        [Tooltip("Afficher le nom sous la vignette.")]
        [SerializeField] bool showLabels = false;

        [Header("Ouverture")]
        [Tooltip("Ex. : bouton Menu de la main gauche (<XRController>{LeftHand}/menuButton).")]
        [SerializeField] InputActionProperty toggleAction;
        [SerializeField] Transform head; // Main Camera du XR Origin
        [SerializeField] float distance = 0.8f;
        [SerializeField] float heightOffset = -0.15f;
        [SerializeField] bool closeOnSelect = true;
        [Tooltip("Test : ouvrir la fenêtre dès le lancement.")]
        [SerializeField] bool openOnStart = false;

        readonly List<GardenItemData> _items = new List<GardenItemData>();
        readonly List<GameObject> _spawnedButtons = new List<GameObject>();
        readonly Dictionary<GameObject, Sprite> _thumbnails = new Dictionary<GameObject, Sprite>();
        Canvas _canvas;
        GardenCategory? _filter;

        void Awake()
        {
            _canvas = GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            if (_canvas.worldCamera == null && head != null)
                _canvas.worldCamera = head.GetComponent<Camera>();

            // Indispensable pour que les rayons XR cliquent sur l'UI
            if (GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();

            if (head == null && Camera.main != null) head = Camera.main.transform;

            itemButtonTemplate.gameObject.SetActive(false);
            if (tabButtonTemplate) tabButtonTemplate.gameObject.SetActive(false);

            CollectItems();
            BuildTabs();
            BuildList();
            Debug.Log($"[Catalog] {_items.Count} éléments chargés, {_spawnedButtons.Count} boutons créés.");
            SetOpen(false);
        }

        void Start()
        {
            if (openOnStart) SetOpen(true);
        }

        void OnEnable()
        {
            if (toggleAction.action == null) return;
            toggleAction.action.Enable();
            toggleAction.action.performed += OnToggle;
        }

        void OnDisable()
        {
            if (toggleAction.action == null) return;
            toggleAction.action.performed -= OnToggle;
        }

        void OnToggle(InputAction.CallbackContext _)
        {
            Debug.Log("[Catalog] Bouton Menu reçu");
            SetOpen(!IsOpen);
        }

        public bool IsOpen => _canvas.enabled;

        public void SetOpen(bool open)
        {
            if (open) PlaceInFrontOfHead();
            _canvas.enabled = open;
            // Le raycaster suit le canvas pour ne pas bloquer les rayons quand c'est fermé
            var raycaster = GetComponent<TrackedDeviceGraphicRaycaster>();
            if (raycaster) raycaster.enabled = open;
        }

        /// <summary>Positionne la fenêtre face au regard, à plat (sans inclinaison de tête).</summary>
        void PlaceInFrontOfHead()
        {
            if (head == null) return;
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f) forward = head.up; // regard vertical
            forward.Normalize();

            transform.position = head.position + forward * distance + Vector3.up * heightOffset;
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        void CollectItems()
        {
            _items.Clear();
            if (catalog == null) { Debug.LogWarning("[Catalog] Aucun GardenCatalog assigné."); return; }

            foreach (var data in catalog.items)
                if (data != null && data.prefab != null) _items.Add(data);

            // Prefabs bruts sans fiche : on crée une fiche à la volée
            foreach (var prefab in catalog.prefabs)
            {
                if (prefab == null || _items.Exists(i => i.prefab == prefab)) continue;
                var data = ScriptableObject.CreateInstance<GardenItemData>();
                data.displayName = prefab.name;
                data.prefab = prefab;
                _items.Add(data);
            }
        }

        void BuildTabs()
        {
            if (tabContainer == null || tabButtonTemplate == null) return;

            CreateTab("Tout", null);
            foreach (GardenCategory cat in System.Enum.GetValues(typeof(GardenCategory)))
                if (_items.Exists(i => i.category == cat))
                    CreateTab(cat.ToString(), cat);
        }

        void CreateTab(string label, GardenCategory? category)
        {
            var tab = Instantiate(tabButtonTemplate, tabContainer);
            tab.gameObject.SetActive(true);
            var text = tab.GetComponentInChildren<TMP_Text>();
            if (text) text.text = label;
            tab.onClick.AddListener(() => { _filter = category; BuildList(); });
        }

        void BuildList()
        {
            foreach (var b in _spawnedButtons) Destroy(b);
            _spawnedButtons.Clear();

            foreach (var item in _items)
            {
                if (_filter.HasValue && item.category != _filter.Value) continue;

                var button = Instantiate(itemButtonTemplate, contentRoot);
                button.gameObject.SetActive(true);
                button.name = "Btn_" + item.displayName;

                var label = button.transform.Find("Label")?.GetComponent<TMP_Text>();
                if (label)
                {
                    label.text = item.displayName;
                    label.gameObject.SetActive(showLabels);
                }

                var icon = button.transform.Find("Icon")?.GetComponent<Image>();
                if (icon)
                {
                    Sprite sprite = item.icon != null ? item.icon : GetThumbnail(item.prefab);
                    icon.sprite = sprite;
                    icon.preserveAspect = true;
                    icon.enabled = sprite != null;
                }

                var captured = item;
                button.onClick.AddListener(() => OnItemClicked(captured));
                _spawnedButtons.Add(button.gameObject);
            }
        }

        Sprite GetThumbnail(GameObject prefab)
        {
            if (!autoThumbnails || prefab == null) return null;
            if (_thumbnails.TryGetValue(prefab, out var cached)) return cached;

            var sprite = ThumbnailRenderer.Render(prefab, thumbnailSize);
            _thumbnails[prefab] = sprite;
            return sprite;
        }

        void OnDestroy() => ThumbnailRenderer.Release();

        void OnItemClicked(GardenItemData item)
        {
            if (placer != null) placer.BeginPlacement(item);
            else Debug.LogWarning("[Catalog] Aucun PrefabPlacer assigné.");

            if (closeOnSelect) SetOpen(false);
        }
    }
}