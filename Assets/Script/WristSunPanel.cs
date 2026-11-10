using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace GardenXR
{
    /// <summary>
    /// Panneau « Soleil » au-dessus de la manette gauche (visible quand elle pointe vers le haut) :
    /// - arc du ciel : le soleil se déplace dessus selon l'heure ; points jaunes = jour, bleus = nuit ;
    /// - grande heure + moment de la journée (Matin, Midi, Après-midi, Soir, Nuit) ;
    /// - curseur large avec graduations (6 h → 21 h) ;
    /// - boutons -1 h, -15 min, Lecture / Pause, +15 min, +1 h ;
    /// - raccourcis Matin 8 h, Midi 13 h, Soir 18 h.
    /// L'heure change en douceur : le soleil glisse jusqu'à l'heure choisie et les ombres tournent progressivement.
    /// À mettre sur "Left Controller".
    /// </summary>
    public class WristSunPanel : MonoBehaviour
    {
        [SerializeField] SunlightSimulator sun;

        [Header("Placement (repère de la manette gauche)")]
        [Tooltip("Z positif = au-dessus de la manette quand elle pointe vers le haut ; Y = décalage vers toi.")]
        [SerializeField] Vector3 panelOffset = new Vector3(0f, 0.06f, 0.17f);
        [Tooltip("90 en X : le panneau te fait face quand la manette pointe vers le haut.")]
        [SerializeField] Vector3 panelEuler = new Vector3(90f, 0f, 0f);
        [SerializeField] float panelWidthMeters = 0.26f;

        [Header("Affichage")]
        [Range(0f, 1f)] [SerializeField] float showThreshold = 0.6f;
        [Range(0f, 1f)] [SerializeField] float hideThreshold = 0.4f;

        [Header("Heure")]
        [SerializeField] float minHour = 5f;
        [SerializeField] float maxHour = 22f;
        [Tooltip("Vitesse à laquelle le soleil rejoint l'heure choisie (heures par seconde).")]
        [SerializeField] float glideSpeed = 4f;
        [Tooltip("Vitesse de la lecture automatique (heures par seconde).")]
        [SerializeField] float playSpeed = 1f;

        const float W = 520f, H = 440f;
        const float ArcRadius = 150f;
        static readonly Vector2 ArcCenter = new Vector2(0f, 30f);

        Canvas _canvas;
        TrackedDeviceGraphicRaycaster _raycaster;
        Slider _slider;
        TMP_Text _hourText, _partText, _playText;
        RectTransform _sunIcon;
        Image[] _arcDots;
        Sprite _circle;
        float _target;
        bool _playing, _updating;

        void Start()
        {
            if (sun == null) sun = SunlightSimulator.Instance ? SunlightSimulator.Instance : FindFirstObjectByType<SunlightSimulator>();
            if (sun == null) { Debug.LogWarning("[WristSun] Aucun SunlightSimulator dans la scène."); enabled = false; return; }

            _target = Mathf.Clamp(sun.Hour, minHour, maxHour);
            _circle = CreateCircleSprite();
            Build();
            SetVisible(false);
            sun.Changed += Refresh;
            Refresh();
        }

        void OnDestroy()
        {
            if (sun) sun.Changed -= Refresh;
        }

        void Update()
        {
            if (sun == null) return;

            if (_playing)
            {
                _target += playSpeed * Time.deltaTime;
                if (_target > maxHour) _target = minHour + (_target - maxHour);
                sun.SetHour(_target);
                return;
            }

            // Glissement doux vers l'heure choisie (ralentit à l'arrivée)
            float diff = _target - sun.Hour;
            if (Mathf.Abs(diff) > 0.005f)
            {
                float step = Mathf.Max(glideSpeed * Time.deltaTime, Mathf.Abs(diff) * 6f * Time.deltaTime);
                sun.SetHour(Mathf.MoveTowards(sun.Hour, _target, step));
            }
        }

        void LateUpdate()
        {
            if (_canvas == null) return;
            float up = Vector3.Dot(transform.forward, Vector3.up);
            if (!_canvas.enabled && up > showThreshold) SetVisible(true);
            else if (_canvas.enabled && up < hideThreshold) SetVisible(false);
        }

        void SetVisible(bool on)
        {
            _canvas.enabled = on;
            _raycaster.enabled = on;
        }

        // ---------- Commandes ----------

        void SetTarget(float h)
        {
            _playing = false;
            _target = Mathf.Clamp(h, minHour, maxHour);
            RefreshControls();
        }

        void Step(float delta) => SetTarget(Mathf.Round((_target + delta) * 4f) / 4f); // pas de 15 min

        void TogglePlay()
        {
            _playing = !_playing;
            if (_playing && _target >= maxHour - 0.01f) _target = minHour;
            RefreshControls();
        }

        // ---------- Affichage ----------

        void Refresh()
        {
            if (_hourText == null) return;
            float h = sun.Hour;
            int hh = Mathf.FloorToInt(h);
            int mm = Mathf.FloorToInt((h - hh) * 60f);
            _hourText.text = $"{hh:00}:{mm:00}";
            _partText.text = DayPart(h, sun.ElevationDeg);

            // Soleil sur l'arc : à gauche au début de la plage, à droite à la fin
            float t = Mathf.InverseLerp(minHour, maxHour, h);
            float a = Mathf.PI * (1f - t);
            _sunIcon.anchoredPosition = ArcCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ArcRadius;
            var img = _sunIcon.GetComponent<Image>();
            img.color = sun.ElevationDeg > 0f
                ? Color.Lerp(new Color(1f, 0.55f, 0.25f), new Color(1f, 0.9f, 0.3f), Mathf.Clamp01(sun.ElevationDeg / 25f))
                : new Color(0.75f, 0.8f, 0.95f); // lune la nuit

            RefreshControls();
        }

        void RefreshControls()
        {
            if (_slider == null) return;
            _updating = true;
            _slider.value = _target;
            _updating = false;
            if (_playText) _playText.text = _playing ? "Pause" : "Lecture";
        }

        static string DayPart(float h, float elevation)
        {
            if (elevation < 0f) return "Nuit";
            if (h < 11f) return "Matin";
            if (h < 14f) return "Midi";
            if (h < 18f) return "Après-midi";
            return "Soir";
        }

        // ---------- Construction ----------

        void Build()
        {
            var root = new GameObject("WristSunPanel", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            root.transform.localPosition = panelOffset;
            root.transform.localRotation = Quaternion.Euler(panelEuler);
            root.transform.localScale = Vector3.one * (panelWidthMeters / W);
            int uiLayer = LayerMask.NameToLayer("UI");
            root.layer = uiLayer >= 0 ? uiLayer : 0;

            _canvas = root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            if (Camera.main) _canvas.worldCamera = Camera.main;
            root.AddComponent<CanvasScaler>();
            _raycaster = root.AddComponent<TrackedDeviceGraphicRaycaster>();
            ((RectTransform)root.transform).sizeDelta = new Vector2(W, H);

            // Fond
            var bg = UI("Fond", root.transform, Vector2.zero, new Vector2(W, H));
            bg.gameObject.AddComponent<Image>().color = new Color(0.06f, 0.1f, 0.16f, 0.92f);

            BuildArc(root.transform);

            _hourText = Label(root.transform, new Vector2(0f, 75f), new Vector2(300f, 70f), 60, FontStyles.Bold, Color.white);
            _partText = Label(root.transform, new Vector2(0f, 28f), new Vector2(300f, 40f), 28, FontStyles.Normal, new Color(0.75f, 0.85f, 1f));

            // Curseur + graduations
            _slider = CreateSlider(root.transform, new Vector2(0f, -40f), new Vector2(440f, 36f));
            _slider.onValueChanged.AddListener(v => { if (!_updating) SetTarget(v); });
            for (int h = 6; h <= 21; h += 3)
            {
                float x = Mathf.Lerp(-220f, 220f, Mathf.InverseLerp(minHour, maxHour, h));
                Label(root.transform, new Vector2(x, -76f), new Vector2(60f, 26f), 20, FontStyles.Normal, new Color(0.7f, 0.75f, 0.85f)).text = $"{h} h";
            }

            // Boutons de réglage
            float y = -128f;
            Button(root.transform, "-1 h", new Vector2(-190f, y), 84f, () => Step(-1f));
            Button(root.transform, "-15", new Vector2(-95f, y), 84f, () => Step(-0.25f));
            _playText = Button(root.transform, "Lecture", new Vector2(0f, y), 96f, TogglePlay, new Color(0.45f, 0.75f, 1f));
            Button(root.transform, "+15", new Vector2(95f, y), 84f, () => Step(0.25f));
            Button(root.transform, "+1 h", new Vector2(190f, y), 84f, () => Step(1f));

            // Raccourcis
            y = -185f;
            Button(root.transform, "Matin 8 h", new Vector2(-160f, y), 150f, () => SetTarget(8f), new Color(1f, 0.75f, 0.45f));
            Button(root.transform, "Midi 13 h", new Vector2(0f, y), 150f, () => SetTarget(13f), new Color(1f, 0.9f, 0.4f));
            Button(root.transform, "Soir 18 h", new Vector2(160f, y), 150f, () => SetTarget(18f), new Color(1f, 0.6f, 0.4f));
        }

        void BuildArc(Transform parent)
        {
            // Ligne d'horizon
            var horizon = UI("Horizon", parent, ArcCenter + new Vector2(0f, -4f), new Vector2(ArcRadius * 2f + 30f, 3f));
            horizon.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.25f);

            // Points de l'arc : une heure par point, jaune le jour, bleu la nuit
            int count = Mathf.RoundToInt(maxHour - minHour) + 1;
            _arcDots = new Image[count];
            for (int i = 0; i < count; i++)
            {
                float h = minHour + i;
                float t = Mathf.InverseLerp(minHour, maxHour, h);
                float a = Mathf.PI * (1f - t);
                var dot = UI("Heure_" + h, parent, ArcCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ArcRadius, new Vector2(10f, 10f));
                var img = dot.gameObject.AddComponent<Image>();
                img.sprite = _circle;
                bool day = sun.DirectionToSun(h).y > 0f;
                img.color = day ? new Color(1f, 0.85f, 0.4f, 0.7f) : new Color(0.4f, 0.5f, 0.8f, 0.6f);
                img.raycastTarget = false;
                _arcDots[i] = img;
            }

            // Soleil
            _sunIcon = UI("Soleil", parent, ArcCenter, new Vector2(44f, 44f));
            var s = _sunIcon.gameObject.AddComponent<Image>();
            s.sprite = _circle;
            s.raycastTarget = false;
        }

        RectTransform UI(string name, Transform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        TMP_Text Label(Transform parent, Vector2 pos, Vector2 size, float fontSize, FontStyles style, Color color)
        {
            var t = UI("Texte", parent, pos, size).gameObject.AddComponent<TextMeshProUGUI>();
            t.fontSize = fontSize;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.enableWordWrapping = false;
            t.raycastTarget = false;
            return t;
        }

        TMP_Text Button(Transform parent, string text, Vector2 pos, float width, UnityEngine.Events.UnityAction onClick,
                        Color? color = null)
        {
            var rt = UI("Btn_" + text, parent, pos, new Vector2(width, 48f));
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color ?? new Color(0.85f, 0.9f, 1f);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f);
            btn.colors = colors;
            btn.onClick.AddListener(() =>
            {
                GardenSounds.Play(GardenSound.Click, rt.position);
                onClick();
            });

            var label = Label(rt, Vector2.zero, rt.sizeDelta, 24, FontStyles.Bold, new Color(0.08f, 0.1f, 0.15f));
            label.text = text;
            return label;
        }

        Slider CreateSlider(Transform parent, Vector2 pos, Vector2 size)
        {
            var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
            go.transform.SetParent(parent, false);
            foreach (var tr in go.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var bgImg = go.transform.Find("Background")?.GetComponent<Image>();
            if (bgImg) bgImg.color = new Color(1f, 1f, 1f, 0.18f);
            var fill = go.transform.Find("Fill Area/Fill")?.GetComponent<Image>();
            if (fill) fill.color = new Color(1f, 0.82f, 0.3f);
            var handle = go.transform.Find("Handle Slide Area/Handle");
            if (handle)
            {
                var hi = handle.GetComponent<Image>();
                hi.sprite = _circle;
                hi.color = Color.white;
                ((RectTransform)handle).sizeDelta = new Vector2(44f, 0f); // poignée large, facile à attraper
            }

            var slider = go.GetComponent<Slider>();
            slider.minValue = minHour;
            slider.maxValue = maxHour;
            slider.wholeNumbers = false;
            return slider;
        }

        static Sprite CreateCircleSprite()
        {
            const int s = 64;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { name = "Cercle", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[s * s];
            float r = s * 0.5f;
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                px[y * s + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(r - d)); // bord adouci
            }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
        }
    }
}
