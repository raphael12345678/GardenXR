using UnityEngine;

namespace GardenXR
{
    /// <summary>
    /// Photographie un prefab avec une caméra cachée et renvoie un Sprite.
    /// Fonctionne en jeu et en build (pas besoin de préparer des icônes à la main).
    /// </summary>
    public static class ThumbnailRenderer
    {
        const int ThumbnailLayer = 31; // layer réservé aux vignettes (pas besoin de le nommer)
        static readonly Vector3 StagePosition = new Vector3(0f, -1000f, 0f);
        static readonly Color Background = new Color(0.86f, 0.91f, 0.84f, 1f); // vert pâle

        static Camera _cam;
        static RenderTexture _rt;

        public static Sprite Render(GameObject prefab, int size = 256)
        {
            if (prefab == null) return null;
            EnsureCamera(size);

            // Pose le modèle loin de la scène, en vue 3/4
            var go = Object.Instantiate(prefab, StagePosition, Quaternion.Euler(0f, 150f, 0f));
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>()) mb.enabled = false;
            foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
            SetLayer(go, ThumbnailLayer);

            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                go.SetActive(false);
                Object.Destroy(go);
                return null;
            }

            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);

            // Cadre l'objet entier, vu de face et légèrement au-dessus
            float radius = Mathf.Max(b.extents.magnitude, 0.01f);
            float dist = radius / Mathf.Sin(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            Vector3 dir = new Vector3(0f, 0.45f, -1f).normalized;

            _cam.transform.position = b.center + dir * dist;
            _cam.transform.LookAt(b.center);
            _cam.nearClipPlane = Mathf.Max(0.01f, dist - radius * 1.5f);
            _cam.farClipPlane = dist + radius * 1.5f;
            _cam.Render();

            var previous = RenderTexture.active;
            RenderTexture.active = _rt;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;

            // Désactivé tout de suite pour ne pas apparaître dans la vignette suivante
            go.SetActive(false);
            Object.Destroy(go);

            tex.name = "Thumb_" + prefab.name;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        public static void Release()
        {
            if (_cam) Object.Destroy(_cam.gameObject);
            if (_rt) { _rt.Release(); Object.Destroy(_rt); }
            _cam = null;
            _rt = null;
        }

        static void EnsureCamera(int size)
        {
            if (_rt == null || _rt.width != size)
            {
                if (_rt) _rt.Release();
                _rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            }

            if (_cam != null) { _cam.targetTexture = _rt; return; }

            var camGO = new GameObject("ThumbnailCamera") { hideFlags = HideFlags.HideAndDontSave };
            _cam = camGO.AddComponent<Camera>();
            _cam.enabled = false;                                  // rend uniquement sur demande
            _cam.stereoTargetEye = StereoTargetEyeMask.None;       // pas une caméra du casque
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Background;
            _cam.cullingMask = 1 << ThumbnailLayer;
            _cam.fieldOfView = 30f;
            _cam.targetTexture = _rt;
        }

        static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform) SetLayer(t.gameObject, layer);
        }
    }
}
