using UnityEngine;

namespace ArrowFixes
{
    /// <summary>
    /// VR, switching arrow type by looking at the bow: the HUD's "switch arrow type" icon sits at a fixed spot of the
    /// HUD, placed for vanilla's steep look down, so it showed away from the bow you look at. Here the selected type's
    /// icon floats just above the bow instead, facing you. It hangs off the bow, so it goes away with it.
    /// </summary>
    internal static class BowCycleIcon
    {
        private const float Size = 0.07f;    // metres
        private const float Above = 0.09f;   // above the bow's held position, in view space
        private const float Toward = 0.04f;  // toward the eyes, so the bow doesn't cover it

        private static GameObject _icon;
        private static Material _material;

        public static void Show(Transform bow, Texture texture, Transform eye)
        {
            if (!bow || !eye || !texture) { Hide(); return; }
            if (!_icon && !Create()) return;
            if (_icon.transform.parent != bow)
            {
                _icon.transform.SetParent(bow, false);
                _icon.layer = bow.gameObject.layer; // drawn with the bow
            }
            if (_material.mainTexture != texture) _material.mainTexture = texture;
            var toEye = eye.position - bow.position;
            var position = bow.position + eye.up * Above + (toEye.sqrMagnitude > 1e-6f ? toEye.normalized * Toward : Vector3.zero);
            _icon.transform.position = position;
            _icon.transform.rotation = Quaternion.LookRotation(position - eye.position, eye.up);
            _icon.transform.localScale = Vector3.one; // the mesh has the size; undo the bow's scale
            var lossy = _icon.transform.lossyScale;
            _icon.transform.localScale = new Vector3(1f / Mathf.Max(lossy.x, 1e-4f), 1f / Mathf.Max(lossy.y, 1e-4f), 1f / Mathf.Max(lossy.z, 1e-4f));
            if (!_icon.activeSelf) _icon.SetActive(true);
        }

        public static void Hide()
        {
            if (_icon && _icon.activeSelf) _icon.SetActive(false);
        }

        private static bool Create()
        {
            // NGUI's shader (the HUD's), else Unity's own transparent ones; whichever this build has.
            Shader shader = null;
            foreach (var name in new[] { "Unlit/Transparent Colored", "Unlit/Transparent", "Sprites/Default" })
                if (!shader) shader = Shader.Find(name);
            if (!shader)
            {
                Plugin.Log.LogWarning("Bow cycle icon: no transparent shader found, the icon isn't shown at the bow");
                return false;
            }
            if (!_material) _material = new Material(shader) { name = "ArrowFixes_BowCycleIcon" };
            _icon = new GameObject("ArrowFixes_BowCycleIcon");
            var mesh = new Mesh { name = "ArrowFixes_BowCycleIcon" };
            float h = Size / 2f;
            mesh.vertices = new[] { new Vector3(-h, -h, 0f), new Vector3(h, -h, 0f), new Vector3(h, h, 0f), new Vector3(-h, h, 0f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 }; // both sides
            mesh.RecalculateBounds();
            _icon.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = _icon.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _icon.SetActive(false);
            return true;
        }
    }
}
