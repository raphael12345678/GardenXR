using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace GardenXR
{
    /// <summary>
    /// Fenêtre canvas world-space qui s'ouvre devant l'utilisateur, avec un onglet par groupe
    /// du catalogue (Plantes, Arbres, Fleurs, Autre...). Un clic sur un élément lance le placement.
    ///
    /// Hiérarchie attendue :
    ///   Canvas (World Space + ce script)
    ///     └ Panel
    ///         ├ Tabs (Horizontal Layout Group)       -> tabContainer
    ///         │   └ TabButton (Button + TMP_Text)    -> tabButtonTemplate
    ///         └ Scroll View / Viewport / Content     -> contentRoot
    ///               └ Button (Icon + Label)          -> itemButtonTemplate
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class PrefabCatalogWindow : MonoBehaviour
    {
        [Header("Données")]
        [SerializeField] GardenCatalog catalog;
        [SerializeField] PrefabPlacer placer;

        [Header("UI")]
        [SerializeField] Transform contentRoot;
        [Tooltip("Bouton modèle : Button + Image (enfant 'Icon') + TMP_Text (enfant 'Label').")]
        [SerializeField] Button itemButtonTemplate;

        [Header("Onglets (groupes)")]
        [SerializeField] Transform tabContainer;
        [SerializeField] Button tabButtonTemplate;
        [Tooltip("Ajoute un onglet 'Tout' en premier.")]
        [SerializeField] bool showAllTab = false;
        [Tooltip("Afficher le nombre d'éléments dans le nom de l'onglet.")]
        [SerializeField] bool showCountInTab = true;
        [SerializeField] Color tabNormalColor = new Color(1f, 1f, 1f, 1f);
        [SerializeField] Color tabSelectedColor = new Color(0.55f, 0.82f, 0.5f, 1f);

        [Header("Vignettes")]
        [Tooltip("Génère une image 3D de chaque prefab quand il n'a pas d'icône.")]
        [SerializeField] bool autoThumbnails = true;
        [SerializeField] int thumbnailSize = 256;
        [Tooltip("Afficher le nom sous la vignette.")]
        [SerializeField] bool showLabels = false;

        [Header("Ouverture")]
        [Tooltip("Bouton Menu de la main gauche : <XRController>{LeftHand}/menuButton")]
        [SerializeField] InputActionProperty toggleAction;
        [SerializeField] Transform head; // Main Camera du XR Origin
        [SerializeField] float distance = 0.8f;
        [SerializeField] float heightOffset = -0.15f;
        [SerializeField] bool closeOnSelect = true;
        [Tooltip("Test : ouvrir la fenêtre dès le lancement.")]
        [SerializeField] bool openOnStart = false;

        class Group
        {
            public string name;
            public readonly List<GardenItemData> items = new List<GardenItemData>();
        }

        const int AllTab = -1;

        readonly List<Group> _groups = new List<Group>();
        readonly List<GameObject> _spawnedButtons = new List<GameObject>();
        readonly List<(Image image, int groupIndex)> _tabs = new List<(Image, int)>();
        readonly Dictionary<GameObject, Sprite> _thumbnails = new Dictionary<GameObject, Sprite>();
        Canvas _canvas;
        ScrollRect _scroll;
        int _currentGroup;

        public bool IsOpen => _canvas.enabled;

        void Awake()
        {
            _canvas = GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (_canvas.worldCamera == null && head != null)
                _canvas.worldCamera = head.GetComponent<Camera>();

            // Indispensable pour que les rayons XR cliquent sur l'UI
            if (GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();

            _scroll = contentRoot ? contentRoot.GetComponentInParent<ScrollRect>() : null;
            itemButtonTemplate.gameObject.SetActive(false);
            if (tabButtonTemplate) tabButtonTemplate.gameObject.SetActive(false);

            CollectGroups();
            BuildTabs();
            // La liste (et les vignettes) est construite à la première ouverture,
            // quand le pipeline de rendu est prêt.

            int total = 0;
            foreach (var g in _groups) total += g.items.Count;
            Debug.Log($"[Catalog] {total} éléments dans {_groups.Count} groupes.");

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

        void OnDestroy() => ThumbnailRenderer.Release();

        void OnToggle(InputAction.CallbackContext _) => SetOpen(!IsOpen);

        bool _listBuilt;

        public void SetOpen(bool open)
        {
            if (open && !_listBuilt)
            {
                _listBuilt = true;
                ShowGroup(showAllTab || _groups.Count == 0 ? AllTab : 0);
            }
            if (open) PlaceInFrontOfHead();
            _canvas.enabled = open;
            var raycaster = GetComponent<TrackedDeviceGraphicRaycaster>();
            if (raycaster) raycaster.enabled = open;
        }

        /// <summary>Positionne la fenêtre face au regard, à plat (sans inclinaison de tête).</summary>
        void PlaceInFrontOfHead()
        {
            if (head == null) return;
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f) forward = head.up;
            forward.Normalize();

            transform.position = head.position + forward * distance + Vector3.up * heightOffset;
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        // ---------- Données ----------

        void CollectGroups()
        {
            _groups.Clear();
            if (catalog == null) { Debug.LogWarning("[Catalog] Aucun GardenCatalog assigné."); return; }

            foreach (var source in catalog.groups)
            {
                var group = new Group { name = source.name };
                foreach (var prefab in source.prefabs)
                {
                    if (prefab == null) continue;
                    var data = ScriptableObject.CreateInstance<GardenItemData>();
                    data.displayName = prefab.name;
                    data.prefab = prefab;
                    group.items.Add(data);
                }
                if (group.items.Count > 0) _groups.Add(group); // pas d'onglet vide
            }
        }

        // ---------- Onglets ----------

        void BuildTabs()
        {
            if (tabContainer == null || tabButtonTemplate == null) return;

            if (showAllTab)
            {
                int total = 0;
                foreach (var g in _groups) total += g.items.Count;
                CreateTab(TabLabel("Tout", total), AllTab);
            }

            for (int i = 0; i < _groups.Count; i++)
                CreateTab(TabLabel(_groups[i].name, _groups[i].items.Count), i);
        }

        string TabLabel(string name, int count) => showCountInTab ? $"{name} ({count})" : name;

        void CreateTab(string label, int groupIndex)
        {
            var tab = Instantiate(tabButtonTemplate, tabContainer);
            tab.gameObject.SetActive(true);
            tab.name = "Tab_" + label;

            var text = tab.GetComponentInChildren<TMP_Text>();
            if (text) text.text = label;

            tab.onClick.AddListener(() => ShowGroup(groupIndex));
            _tabs.Add((tab.GetComponent<Image>(), groupIndex));
        }

        void ShowGroup(int groupIndex)
        {
            _currentGroup = groupIndex;

            foreach (var (image, index) in _tabs)
                if (image) image.color = index == _currentGroup ? tabSelectedColor : tabNormalColor;

            BuildList();
            if (_scroll) _scroll.verticalNormalizedPosition = 1f; // revenir en haut
        }

        // ---------- Liste ----------

        void BuildList()
        {
            foreach (var b in _spawnedButtons) Destroy(b);
            _spawnedButtons.Clear();

            if (_currentGroup == AllTab)
                foreach (var g in _groups) AddButtons(g.items);
            else if (_currentGroup >= 0 && _currentGroup < _groups.Count)
                AddButtons(_groups[_currentGroup].items);
        }

        void AddButtons(List<GardenItemData> items)
        {
            foreach (var item in items)
            {
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

        void OnItemClicked(GardenItemData item)
        {
            if (placer != null) placer.BeginPlacement(item);
            else Debug.LogWarning("[Catalog] Aucun PrefabPlacer assigné.");

            if (closeOnSelect) SetOpen(false);
        }
    }
}
