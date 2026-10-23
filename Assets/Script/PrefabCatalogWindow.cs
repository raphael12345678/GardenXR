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
        [Tooltip("Nombre de lignes de vignettes visibles en même temps (la fenêtre s'agrandit en hauteur). 0 = ne pas toucher.")]
        [SerializeField] int visibleRows = 3;

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
        const int PathTab = -2;
        const int IrrigationTab = -3;
        const int ZoneTab = -4;
        const int TabRows = 2;
        const float TabBarHeight = 108f; // 2 lignes d'onglets
        ZoneTool _zoneTool;
        PathTool _pathTool;
        IrrigationNetworkBuilder _irrigation;

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
            FitHeightToRows();
            // La liste (et les vignettes) est construite à la première ouverture,
            // quand le pipeline de rendu est prêt.

            int total = 0;
            foreach (var g in _groups) total += g.items.Count;
            Debug.Log($"[Catalog] {total} éléments dans {_groups.Count} groupes.");

            SetOpen(false);
            _ready = true;
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
        bool _ready; // pas de son pour la fermeture initiale au lancement

        public void SetOpen(bool open)
        {
            if (open && !_listBuilt)
            {
                _listBuilt = true;
                ShowGroup(showAllTab || _groups.Count == 0 ? AllTab : 0);
            }
            if (open) PlaceInFrontOfHead();
            if (_ready && _canvas.enabled != open)
                GardenSounds.Play(open ? GardenSound.MenuOpen : GardenSound.MenuClose, transform.position);
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
                    data.groupName = source.name;
                    data.sunNeed = source.sunNeed;
                    data.waterNeed = source.waterNeed;
                    group.items.Add(data);
                }
                if (group.items.Count > 0) _groups.Add(group); // pas d'onglet vide
            }
        }

        // ---------- Onglets ----------

        void BuildTabs()
        {
            // Pas de barre d'onglets configurée à la main : on la crée automatiquement
            if (tabContainer == null) tabContainer = CreateTabBar();
            if (tabContainer == null) return;

            if (showAllTab)
            {
                int total = 0;
                foreach (var g in _groups) total += g.items.Count;
                CreateTab(TabLabel("Tout", total), AllTab);
            }

            for (int i = 0; i < _groups.Count; i++)
                CreateTab(TabLabel(_groups[i].name, _groups[i].items.Count), i);

            // Onglet « Allées » si un PathTool est présent dans la scène
            _pathTool = FindFirstObjectByType<PathTool>();
            if (_pathTool && _pathTool.Styles.Count > 0)
                CreateTab(TabLabel("Allées", _pathTool.Styles.Count), PathTab);

            // Onglet « Irrigation » si un IrrigationNetworkBuilder est présent
            _irrigation = FindFirstObjectByType<IrrigationNetworkBuilder>();
            if (_irrigation && _irrigation.Styles.Count > 0)
                CreateTab(TabLabel("Irrigation", _irrigation.Styles.Count), IrrigationTab);

            // Onglet « Zones » si un ZoneTool est présent
            _zoneTool = FindFirstObjectByType<ZoneTool>();
            if (_zoneTool && _zoneTool.Styles.Count > 0)
                CreateTab(TabLabel("Zones", _zoneTool.Styles.Count), ZoneTab);

            LayoutTabGrid();
        }

        /// <summary>Barre créée par code : onglets répartis sur 2 lignes, largeur égale.</summary>
        void LayoutTabGrid()
        {
            var grid = tabContainer ? tabContainer.GetComponent<GridLayoutGroup>() : null;
            if (grid == null || _tabs.Count == 0) return;

            int rows = _tabs.Count > 4 ? TabRows : 1;
            int cols = Mathf.CeilToInt(_tabs.Count / (float)rows);
            float width = ((RectTransform)tabContainer).rect.width;
            if (width <= 1f) width = ((RectTransform)transform).rect.width - 24f;

            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = cols;
            grid.cellSize = new Vector2((width - grid.spacing.x * (cols - 1)) / cols,
                                        (TabBarHeight - grid.spacing.y * (rows - 1)) / rows);
        }

        string TabLabel(string name, int count) => showCountInTab ? $"{name} ({count})" : name;

        /// <summary>
        /// Agrandit la fenêtre en hauteur pour que la zone de défilement affiche 'visibleRows' lignes
        /// de vignettes (en plus de la barre d'onglets).
        /// </summary>
        void FitHeightToRows()
        {
            if (visibleRows <= 0 || _scroll == null || contentRoot == null) return;
            var grid = contentRoot.GetComponent<GridLayoutGroup>();
            if (grid == null) return;

            float needed = grid.padding.top + grid.padding.bottom
                           + visibleRows * grid.cellSize.y + (visibleRows - 1) * grid.spacing.y;
            var viewport = _scroll.viewport ? _scroll.viewport : (RectTransform)_scroll.transform;
            float delta = needed - viewport.rect.height;
            if (Mathf.Abs(delta) < 1f) return;

            // On agrandit la zone de défilement (si elle a une hauteur fixe), le Canvas
            // et chaque parent qui n'est pas étiré verticalement
            var srt = (RectTransform)_scroll.transform;
            if (Mathf.Approximately(srt.anchorMin.y, srt.anchorMax.y)) srt.sizeDelta += new Vector2(0f, delta);

            for (var t = (RectTransform)_scroll.transform.parent; t != null; t = t.parent as RectTransform)
            {
                bool stretched = !Mathf.Approximately(t.anchorMin.y, t.anchorMax.y);
                if (!stretched || t == transform) t.sizeDelta += new Vector2(0f, delta);
                if (t == transform) break;
            }
        }

        /// <summary>
        /// Crée une barre d'onglets en haut du panneau et décale la zone de défilement en dessous.
        /// </summary>
        Transform CreateTabBar()
        {
            const float barHeight = TabBarHeight, margin = 12f;
            var parent = _scroll ? _scroll.transform.parent : transform;

            var bar = new GameObject("Tabs", typeof(RectTransform));
            bar.layer = parent.gameObject.layer;
            var rt = (RectTransform)bar.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(margin, -(barHeight + margin));
            rt.offsetMax = new Vector2(-margin, -margin);

            var layout = bar.AddComponent<GridLayoutGroup>();
            layout.spacing = new Vector2(8f, 8f);
            layout.startAxis = GridLayoutGroup.Axis.Horizontal;
            layout.childAlignment = TextAnchor.MiddleCenter;

            // La liste commence sous les onglets
            if (_scroll)
            {
                var srt = (RectTransform)_scroll.transform;
                srt.offsetMax = new Vector2(srt.offsetMax.x, srt.offsetMax.y - (barHeight + margin));
            }
            return bar.transform;
        }

        /// <summary>Onglet créé par code quand aucun modèle n'est fourni dans l'inspecteur.</summary>
        Button CreateTabButton(string label)
        {
            var go = new GameObject("Tab_" + label, typeof(RectTransform));
            go.layer = tabContainer.gameObject.layer;
            go.transform.SetParent(tabContainer, false);

            var img = go.AddComponent<Image>();
            img.color = tabNormalColor;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            var txtGo = new GameObject("Label", typeof(RectTransform));
            txtGo.layer = go.layer;
            var trt = (RectTransform)txtGo.transform;
            trt.SetParent(go.transform, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var t = txtGo.AddComponent<TextMeshProUGUI>();
            t.text = label;
            t.fontSize = 24;
            t.enableAutoSizing = true;   // les noms longs rétrécissent au lieu de déborder
            t.fontSizeMin = 14;
            t.fontSizeMax = 24;
            t.enableWordWrapping = false;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.color = new Color(0.1f, 0.15f, 0.1f);
            t.raycastTarget = false;
            return btn;
        }

        void CreateTab(string label, int groupIndex)
        {
            Button tab;
            if (tabButtonTemplate != null)
            {
                tab = Instantiate(tabButtonTemplate, tabContainer);
                tab.gameObject.SetActive(true);
                tab.name = "Tab_" + label;
                var text = tab.GetComponentInChildren<TMP_Text>();
                if (text) text.text = label;
            }
            else
            {
                tab = CreateTabButton(label);
            }

            tab.onClick.AddListener(() =>
            {
                GardenSounds.Play(GardenSound.Click, transform.position);
                ShowGroup(groupIndex);
            });
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
            else if (_currentGroup == PathTab)
                AddPathButtons();
            else if (_currentGroup == IrrigationTab)
                AddIrrigationButtons();
            else if (_currentGroup == ZoneTab && _zoneTool)
                foreach (var style in _zoneTool.Styles)
                {
                    var captured = style;
                    AddToolButton(style.name, style.color, () => _zoneTool.Begin(captured),
                                  style.lawn ? PathTextures.Grass() : null);
                }
            else if (_currentGroup >= 0 && _currentGroup < _groups.Count)
                AddButtons(_groups[_currentGroup].items);
        }

        /// <summary>Un bouton par type de tuyau : pastille de couleur + nom.</summary>
        void AddIrrigationButtons()
        {
            if (_irrigation == null) return;
            foreach (var style in _irrigation.Styles)
            {
                var captured = style;
                AddToolButton(style.name, style.color, () => _irrigation.Begin(captured));
            }
        }

        readonly Dictionary<Texture2D, Sprite> _toolSprites = new Dictionary<Texture2D, Sprite>();

        /// <summary>
        /// Bouton d'outil (zones, allées, irrigation) : aperçu de la texture (ou pastille de couleur)
        /// et nom toujours lisible : texte blanc gras, contour noir, sur un bandeau sombre au premier plan.
        /// </summary>
        void AddToolButton(string name, Color color, System.Action onClick, Texture2D preview = null)
        {
            var button = Instantiate(itemButtonTemplate, contentRoot);
            button.gameObject.SetActive(true);
            button.name = "Btn_Outil_" + name;

            var icon = button.transform.Find("Icon")?.GetComponent<Image>();
            if (icon)
            {
                if (preview)
                {
                    if (!_toolSprites.TryGetValue(preview, out var sp))
                    {
                        // Un morceau de la texture, à l'échelle d'une vignette
                        int w = Mathf.Min(preview.width, 256);
                        sp = Sprite.Create(preview, new Rect(0, 0, w, w), new Vector2(0.5f, 0.5f));
                        _toolSprites[preview] = sp;
                    }
                    icon.sprite = sp;
                    icon.color = Color.white;
                }
                else
                {
                    icon.sprite = null;
                    icon.color = color;
                }
                icon.preserveAspect = false;
                icon.enabled = true;
            }

            var label = button.transform.Find("Label")?.GetComponent<TMP_Text>();
            if (label)
            {
                label.text = name;
                label.gameObject.SetActive(true);
                label.color = Color.white;
                label.fontStyle = FontStyles.Bold;
                label.enableWordWrapping = true;
                label.alignment = TextAlignmentOptions.Center;
                label.outlineWidth = 0.25f;
                label.outlineColor = new Color32(0, 0, 0, 255);

                // Bandeau sombre derrière le texte
                var lrt = (RectTransform)label.transform;
                var bg = new GameObject("LabelBackground", typeof(RectTransform));
                bg.layer = label.gameObject.layer;
                var brt = (RectTransform)bg.transform;
                brt.SetParent(lrt.parent, false);
                brt.anchorMin = lrt.anchorMin; brt.anchorMax = lrt.anchorMax;
                brt.pivot = lrt.pivot;
                brt.anchoredPosition = lrt.anchoredPosition;
                brt.sizeDelta = lrt.sizeDelta;
                var bgImg = bg.AddComponent<Image>();
                bgImg.color = new Color(0f, 0f, 0f, 0.55f);
                bgImg.raycastTarget = false;
                brt.SetAsLastSibling();
                lrt.SetAsLastSibling(); // le texte passe devant l'aperçu
            }

            button.onClick.AddListener(() =>
            {
                GardenSounds.Play(GardenSound.Click, transform.position);
                onClick();
                if (closeOnSelect) SetOpen(false);
            });
            _spawnedButtons.Add(button.gameObject);
        }

        /// <summary>Un bouton par style d'allée : pastille de couleur + nom.</summary>
        void AddPathButtons()
        {
            if (_pathTool == null) return;
            foreach (var style in _pathTool.Styles)
            {
                var captured = style;
                AddToolButton(style.name, style.color, () => _pathTool.Begin(captured),
                              PathTextures.Get(PathTextures.Resolve(style)));
            }
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
            GardenSounds.Play(GardenSound.Click, transform.position);
            if (_pathTool) _pathTool.Stop(); // on quitte les outils de tracé si on choisit un objet
            if (_irrigation) _irrigation.Stop();
            if (_zoneTool) _zoneTool.Stop();
            if (placer != null) placer.BeginPlacement(item);
            else Debug.LogWarning("[Catalog] Aucun PrefabPlacer assigné.");

            if (closeOnSelect) SetOpen(false);
        }
    }
}
