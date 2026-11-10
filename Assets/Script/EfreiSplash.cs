using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GardenXR
{
    /// <summary>
    /// Écran de lancement : logo EFREI + nom de l'application, affichés face à l'utilisateur
    /// au démarrage (fondu d'entrée, maintien, fondu de sortie), puis le jardin.
    /// À mettre sur un objet vide "EfreiSplash" dans la scène principale.
    /// Logo : importer le PNG de l'EFREI, Texture Type = Sprite (2D and UI), le glisser dans "Logo".
    /// </summary>
    public class EfreiSplash : MonoBehaviour
    {
        [SerializeField] Sprite logo;
        [SerializeField] string title = "GardenXR";
        [SerializeField] string subtitle = "Concevoir son jardin en réalité virtuelle";
        [TextArea] [SerializeField] string team =
            "Raphaël Girard · Robinson Diallo · Yassine Hadded\nOrlane Ndjibou · Femi Oussou";
        [SerializeField] string footer = "EFREI · Projet XR 2026-2027";

        [Header("Durées (s)")]
        [SerializeField] float fadeIn = 0.6f;
        [SerializeField] float hold = 3f;
        [SerializeField] float fadeOut = 0.6f;

        [Header("Placement")]
        [SerializeField] float distance = 2f;
        [SerializeField] float widthMeters = 1.6f;
        [Tooltip("Hauteur par rapport aux yeux (m) : 0 = pile à hauteur des yeux, positif = plus haut.")]
        [SerializeField] float heightOffset = 0f;

        CanvasGroup _group;
        GameObject _root;
        Transform _head;

        IEnumerator Start()
        {
            // Attendre que le casque ait sa vraie position (Link peut mettre quelques images)
            _head = Camera.main ? Camera.main.transform : null;
            float wait = 0f;
            while (_head && _head.localPosition.y < 0.3f && wait < 2f) { wait += Time.deltaTime; yield return null; }
            yield return null;
            Build(_head);
            PlaceInFront(1f);

            yield return Fade(0f, 1f, fadeIn);
            yield return new WaitForSeconds(hold);
            yield return Fade(1f, 0f, fadeOut);
            Destroy(_root);
        }

        /// <summary>Le panneau reste devant le regard, à hauteur des yeux, et suit doucement la tête.</summary>
        void LateUpdate()
        {
            if (_root) PlaceInFront(Time.deltaTime * 3f);
        }

        void PlaceInFront(float lerp)
        {
            if (_head == null || _root == null) return;
            Vector3 fwd = Vector3.ProjectOnPlane(_head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 pos = _head.position + fwd * distance + Vector3.up * heightOffset;
            var t = _root.transform;
            t.position = Vector3.Lerp(t.position, pos, Mathf.Clamp01(lerp));
            t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(t.position - _head.position, Vector3.up), Mathf.Clamp01(lerp));
        }

        IEnumerator Fade(float from, float to, float time)
        {
            for (float t = 0f; t < time; t += Time.deltaTime)
            {
                _group.alpha = Mathf.Lerp(from, to, t / time);
                yield return null;
            }
            _group.alpha = to;
        }

        void Build(Transform head)
        {
            const float w = 1000f, h = 620f;
            _root = new GameObject("EfreiSplashCanvas", typeof(RectTransform));
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 2000;
            ((RectTransform)_root.transform).sizeDelta = new Vector2(w, h);
            _root.transform.localScale = Vector3.one * (widthMeters / w);
            _group = _root.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;


            var bg = NewUI("Fond", _root.transform);
            Stretch(bg);
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.color = new Color(0.04f, 0.08f, 0.05f, 0.92f);

            if (logo)
            {
                var lg = NewUI("Logo", _root.transform);
                lg.anchoredPosition = new Vector2(0f, 165f);
                lg.sizeDelta = new Vector2(520f, 230f);
                var img = lg.gameObject.AddComponent<Image>();
                img.sprite = logo;
                img.preserveAspect = true;
            }
            else Debug.LogWarning("[EfreiSplash] Aucun logo assigné : glisse le logo EFREI dans le champ Logo.");

            Text(_root.transform, title, new Vector2(0f, -30f), 90, FontStyles.Bold, Color.white);
            Text(_root.transform, subtitle, new Vector2(0f, -105f), 38, FontStyles.Normal, new Color(0.75f, 0.9f, 0.7f));
            var names = Text(_root.transform, team, new Vector2(0f, -185f), 32, FontStyles.Normal, Color.white);
            ((RectTransform)names.transform).sizeDelta = new Vector2(960f, 90f);
            Text(_root.transform, footer, new Vector2(0f, -268f), 28, FontStyles.Italic, new Color(0.7f, 0.7f, 0.7f));
        }

        static TMP_Text Text(Transform parent, string s, Vector2 pos, float size, FontStyles style, Color color)
        {
            var rt = NewUI("Texte", parent);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(960f, size * 1.4f);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = s;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            return t;
        }

        static RectTransform NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
