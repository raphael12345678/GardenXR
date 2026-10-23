using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

namespace GardenXR
{
    /// <summary>
    /// Vue aérienne : le bouton X (main gauche) fait monter l'utilisateur au-dessus du jardin
    /// pour placer zones, allées et tuyaux d'un coup d'œil. Chaque appui passe au niveau suivant :
    /// sol → altitude 1 → altitude 2 → sol.
    /// - Fondu au noir pendant le changement de hauteur (pas de mouvement vertical visible : anti-cinétose).
    /// - Gravité, saut et téléportation sont coupés en l'air ; le déplacement au stick reste possible
    ///   pour survoler le jardin.
    /// - Tous les outils (catalogue, zones, allées, irrigation) fonctionnent depuis le ciel.
    /// Un seul exemplaire dans la scène (objet vide "AerialView").
    /// </summary>
    public class AerialView : MonoBehaviour
    {
        public static AerialView Instance { get; private set; }
        public bool IsAerial => _level > 0;

        [Header("Hauteurs au-dessus du sol (m)")]
        [SerializeField] float[] altitudes = { 6f, 12f };

        [Header("Commande")]
        [Tooltip("Bouton de la main gauche : primaryButton = X.")]
        [SerializeField] string toggleButton = "primaryButton";

        [Header("Transition")]
        [SerializeField] float fadeTime = 0.2f;
        [SerializeField] LayerMask groundMask = ~0;

        [Header("Composants coupés en vue aérienne")]
        [Tooltip("Noms de types de composants désactivés en l'air (trouvés automatiquement dans la scène).")]
        [SerializeField] List<string> disableTypeNames = new List<string>
        {
            "GravityProvider", "JumpProvider", "TeleportationProvider", "ClimbProvider", "ClimbTeleportInteractor"
        };

        XROrigin _rig;
        Transform _head;
        int _level;
        bool _busy, _wasPressed;
        float _groundY;
        Material _fadeMat;
        GameObject _fade;
        TMP_Text _badge;
        readonly List<Behaviour> _disabled = new List<Behaviour>();

        void Awake() => Instance = this;

        void Start()
        {
            _rig = FindFirstObjectByType<XROrigin>();
            if (Camera.main) _head = Camera.main.transform;
            if (_rig == null) { Debug.LogWarning("[AerialView] Aucun XR Origin dans la scène."); enabled = false; return; }
            BuildFade();
            BuildBadge();
        }

        void Update()
        {
            var c = XRController.leftHand?.TryGetChildControl<ButtonControl>(toggleButton);
            bool pressed = c != null && c.isPressed;
            if (pressed && !_wasPressed && !_busy) NextLevel();
            _wasPressed = pressed;
        }

        /// <summary>Passe au niveau suivant (sol → altitude 1 → altitude 2 → sol).</summary>
        public void NextLevel() => StartCoroutine(GoTo((_level + 1) % (altitudes.Length + 1)));

        public void SetLevel(int level) => StartCoroutine(GoTo(Mathf.Clamp(level, 0, altitudes.Length)));

        IEnumerator GoTo(int level)
        {
            if (_busy || level == _level) yield break;
            _busy = true;

            GardenSounds.PlayAtHead(level > _level ? GardenSound.MenuOpen : GardenSound.MenuClose);
            yield return Fade(0f, 1f);

            Vector3 pos = _rig.transform.position;
            if (_level == 0) _groundY = pos.y; // mémorise la hauteur du sol au départ

            if (level == 0)
            {
                // Retour au sol, à la verticale de l'endroit survolé
                pos.y = GroundHeightBelow(pos, _groundY);
                SetAerialComponents(false);
            }
            else
            {
                pos.y = _groundY + altitudes[level - 1];
                if (_level == 0) SetAerialComponents(true);
            }
            MoveRig(pos);
            _level = level;
            UpdateBadge();

            yield return null; // laisse le temps au tracking de se recaler
            yield return Fade(1f, 0f);
            _busy = false;
        }

        void MoveRig(Vector3 pos)
        {
            // Le CharacterController du XR Origin bloque les téléportations directes : on le coupe le temps du déplacement
            var cc = _rig.GetComponent<CharacterController>();
            bool ccWas = cc && cc.enabled;
            if (cc) cc.enabled = false;
            _rig.transform.position = pos;
            if (cc) cc.enabled = ccWas;
        }

        float GroundHeightBelow(Vector3 p, float fallback)
        {
            if (Physics.Raycast(new Vector3(p.x, p.y + 1f, p.z), Vector3.down, out RaycastHit hit, 500f, groundMask,
                                QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return fallback;
        }

        void SetAerialComponents(bool aerial)
        {
            if (aerial)
            {
                _disabled.Clear();
                foreach (var b in FindObjectsByType<Behaviour>(FindObjectsSortMode.None))
                {
                    if (!b.enabled || !disableTypeNames.Contains(b.GetType().Name)) continue;
                    b.enabled = false;
                    _disabled.Add(b);
                }
            }
            else
            {
                foreach (var b in _disabled) if (b) b.enabled = true;
                _disabled.Clear();
            }
        }

        // ---------- Fondu ----------

        void BuildFade()
        {
            if (_head == null) return;
            _fade = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _fade.name = "AerialFade";
            Destroy(_fade.GetComponent<Collider>());
            _fade.transform.SetParent(_head, false);
            _fade.transform.localPosition = new Vector3(0f, 0f, 0.25f);
            _fade.transform.localScale = new Vector3(2f, 2f, 1f);
            _fadeMat = new Material(Shader.Find("Sprites/Default")) { color = new Color(0f, 0f, 0f, 0f), renderQueue = 5000 };
            var mr = _fade.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _fadeMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _fade.SetActive(false);
        }

        IEnumerator Fade(float from, float to)
        {
            if (_fadeMat == null) yield break;
            _fade.SetActive(true);
            for (float t = 0f; t < fadeTime; t += Time.deltaTime)
            {
                _fadeMat.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, t / fadeTime));
                yield return null;
            }
            _fadeMat.color = new Color(0f, 0f, 0f, to);
            if (to <= 0f) _fade.SetActive(false);
        }

        // ---------- Indicateur ----------

        void BuildBadge()
        {
            if (_head == null) return;
            var go = new GameObject("AerialBadge", typeof(RectTransform));
            go.transform.SetParent(_head, false);
            go.transform.localPosition = new Vector3(0f, -0.28f, 0.9f);
            go.transform.localScale = Vector3.one * 0.0012f;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = new Vector2(500f, 70f);
            _badge = go.AddComponent<TextMeshProUGUI>();
            _badge.fontSize = 36;
            _badge.alignment = TextAlignmentOptions.Center;
            _badge.color = new Color(0.85f, 0.95f, 1f);
            _badge.outlineWidth = 0.25f;
            _badge.outlineColor = new Color32(0, 0, 0, 200);
            _badge.raycastTarget = false;
            UpdateBadge();
        }

        void UpdateBadge()
        {
            if (_badge == null) return;
            _badge.enabled = _level > 0;
            if (_level > 0)
                _badge.text = $"Vue aérienne · {altitudes[_level - 1]:0} m   <size=70%>(X : {(_level < altitudes.Length ? "monter" : "redescendre")})</size>";
        }
    }
}
