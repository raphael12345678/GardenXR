using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace GardenXR
{
    /// <summary>
    /// Panneau « montre » sur le poignet gauche pour régler l'heure du soleil.
    /// S'affiche uniquement quand la manette gauche pointe vers le haut.
    /// Construit entièrement par code. À mettre sur "Left Controller".
    /// </summary>
    public class WristSunPanel : MonoBehaviour
    {
        [SerializeField] SunlightSimulator sun;

        [Header("Placement (repère de la manette gauche)")]
        [Tooltip("Z négatif = vers le poignet. Y positif = côté tourné vers toi quand la manette pointe en haut.")]
        [SerializeField] Vector3 localPosition = new Vector3(0f, 0.035f, -0.15f);
        [Tooltip("90 en X : le panneau est face à toi quand la manette pointe vers le haut.")]
        [SerializeField] Vector3 localEuler = new Vector3(90f, 0f, 0f);
        [SerializeField] float widthMeters = 0.2f;

        [Header("Affichage")]
        [Tooltip("Apparaît quand la manette pointe vers le haut au-delà de ce seuil (1 = verticale).")]
        [Range(0f, 1f)] [SerializeField] float showThreshold = 0.6f;
        [Tooltip("Disparaît en dessous de ce seuil (un peu plus bas pour éviter le clignotement).")]
        [Range(0f, 1f)] [SerializeField] float hideThreshold = 0.4f;

        [Header("Heure")]
        [SerializeField] float minHour = 5f;
        [SerializeField] float maxHour = 22f;
        [Tooltip("Pas des boutons − / +, en heures.")]
        [SerializeField] float buttonStep = 1f;

        const float CanvasWidth = 400f, CanvasHeight = 170f;

        Canvas _canvas;
        TrackedDeviceGraphicRaycaster _raycaster;
        Slider _hourSlider;
        TMP_Text _label;
        bool _updating;

        void Start()
        {
            if (sun == null) sun = SunlightSimulator.Instance ? SunlightSimulator.Instance : FindFirstObjectByType<SunlightSimulator>();
            if (sun == null) { Debug.LogWarning("[WristSun] Aucun SunlightSimulator dans la scène."); enabled = false; return; }

            Build();
            SetVisible(false);
            sun.Changed += Refresh;
            Refresh();
        }

        void OnDestroy()
        {
            if (sun) sun.Changed -= Refresh;
        }

        void LateUpdate()
        {
            if (_canvas == null) return;

            // Manette gauche pointée vers le haut ?
            float up = Vector3.Dot(transform.forward, Vector3.up);
            if (!_canvas.enabled && up > showThreshold) SetVisible(true);
            else if (_canvas.enabled && up < hideThreshold) SetVisible(false);
        }

        void SetVisible(bool on)
        {
            _canvas.enabled = on;
            _raycaster.enabled = on;
        }

        // ---------- Synchronisation ----------

        void Refresh()
        {
            if (_label == null) return;
            _updating = true;
            _hourSlider.value = sun.Hour;
            _updating = false;

            int h = Mathf.FloorToInt(sun.Hour);
            int m = Mathf.FloorToInt((sun.Hour - h) * 60f);
            string night = sun.ElevationDeg < 0f ? "  (nuit)" : "";
            _label.text = $"Soleil  {h:00}:{m:00}{night}";
        }

        void Step(float delta) => sun.SetHour(Mathf.Clamp(sun.Hour + delta, minHour, maxHour));

        // ---------- Construction de l'UI ----------

        void Build()
        {
            var root = new GameObject("WristSunPanel", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            root.transform.localPosition = localPosition;
            root.transform.localRotation = Quaternion.Euler(localEuler);
            root.transform.localScale = Vector3.one * (widthMeters / CanvasWidth);
            int uiLayer = LayerMask.NameToLayer("UI");
            root.layer = uiLayer >= 0 ? uiLayer : 0;

            _canvas = root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            if (Camera.main) _canvas.worldCamera = Camera.main;
            root.AddComponent<CanvasScaler>();
            _raycaster = root.AddComponent<TrackedDeviceGraphicRaycaster>();
            ((RectTransform)root.transform).sizeDelta = new Vector2(CanvasWidth, CanvasHeight);

            // Fond (reçoit aussi le rayon, pour que le panneau ne soit pas "transparent" aux clics)
            var bg = CreateUI("Background", root.transform);
            Stretch(bg);
            bg.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.14f, 0.08f, 0.9f);

            _label = CreateText("Label", root.transform, new Vector2(0f, 50f), new Vector2(CanvasWidth - 20f, 44f), 32, FontStyles.Bold);

            // [ − ]  ===slider===  [ + ]
            CreateButton(root.transform, "−", new Vector2(-160f, -25f), () => Step(-buttonStep));
            CreateButton(root.transform, "+", new Vector2(160f, -25f), () => Step(buttonStep));

            _hourSlider = CreateSlider(root.transform, new Vector2(0f, -25f), new Vector2(240f, 40f), minHour, maxHour);
            _hourSlider.onValueChanged.AddListener(v => { if (!_updating) sun.SetHour(v); });
        }

        static RectTransform CreateUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            return (RectTransform)go.transform;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static TMP_Text CreateText(string name, Transform parent, Vector2 pos, Vector2 size, float fontSize,
                                   FontStyles style = FontStyles.Normal)
        {
            var rt = CreateUI(name, parent);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.fontSize = fontSize;
            t.fontStyle = style;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
            t.raycastTarget = false;
            return t;
        }

        static void CreateButton(Transform parent, string text, Vector2 pos, UnityEngine.Events.UnityAction onClick)
        {
            var rt = CreateUI("Btn_" + text, parent);
            rt.sizeDelta = new Vector2(60f, 60f);
            rt.anchoredPosition = pos;

            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(1f, 0.82f, 0.25f);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                GardenSounds.Play(GardenSound.Click, rt.position);
                onClick();
            });

            var label = CreateText("Text", rt, Vector2.zero, rt.sizeDelta, 44, FontStyles.Bold);
            label.color = new Color(0.1f, 0.15f, 0.1f);
        }

        static Slider CreateSlider(Transform parent, Vector2 pos, Vector2 size, float min, float max)
        {
            var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
            go.transform.SetParent(parent, false);
            foreach (var tr in go.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = parent.gameObject.layer;

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;

            var bgImg = go.transform.Find("Background")?.GetComponent<Image>();
            if (bgImg) bgImg.color = new Color(1f, 1f, 1f, 0.25f);
            var fill = go.transform.Find("Fill Area/Fill")?.GetComponent<Image>();
            if (fill) fill.color = new Color(1f, 0.82f, 0.25f);
            var handle = go.transform.Find("Handle Slide Area/Handle");
            if (handle)
            {
                handle.GetComponent<Image>().color = Color.white;
                ((RectTransform)handle).sizeDelta = new Vector2(30f, 20f);
            }

            var slider = go.GetComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = false;
            return slider;
        }
    }
}
