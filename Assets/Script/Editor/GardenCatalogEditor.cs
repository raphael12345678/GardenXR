#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace GardenXR.EditorTools
{
    /// <summary>
    /// Ajoute un bouton dans l'inspecteur du GardenCatalog pour y charger
    /// automatiquement TOUS les prefabs d'un dossier (sous-dossiers inclus).
    /// À placer dans un dossier "Editor".
    /// </summary>
    [CustomEditor(typeof(GardenCatalog))]
    public class GardenCatalogEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var catalog = (GardenCatalog)target;

            EditorGUILayout.Space();
            if (GUILayout.Button("Remplir depuis le dossier", GUILayout.Height(28)))
            {
                if (!AssetDatabase.IsValidFolder(catalog.prefabFolder))
                {
                    EditorUtility.DisplayDialog("GardenXR", $"Dossier introuvable : {catalog.prefabFolder}", "OK");
                    return;
                }

                Undo.RecordObject(catalog, "Remplir le catalogue");
                catalog.prefabs.Clear();
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { catalog.prefabFolder }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab) catalog.prefabs.Add(prefab);
                }
                EditorUtility.SetDirty(catalog);
                Debug.Log($"[GardenCatalog] {catalog.prefabs.Count} prefabs chargés depuis {catalog.prefabFolder}");
            }
        }
    }
}
#endif
