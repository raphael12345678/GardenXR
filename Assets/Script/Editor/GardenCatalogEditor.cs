#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GardenXR.EditorTools
{
    [CustomEditor(typeof(GardenCatalog))]
    public class GardenCatalogEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var catalog = (GardenCatalog)target;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Remplir range chaque prefab dans le premier groupe dont un mot-clé apparaît dans son nom " +
                "(en partant du bas de la liste). Les autres vont dans le groupe sans mots-clés (Autre). " +
                "Tu peux ensuite glisser un prefab d'une liste à l'autre à la main.",
                MessageType.Info);

            if (GUILayout.Button("Remplir depuis le dossier", GUILayout.Height(28)))
                Fill(catalog);

            if (GUILayout.Button("Groupes par défaut (Plantes / Arbres / Fleurs / Autre)"))
            {
                Undo.RecordObject(catalog, "Groupes par défaut");
                catalog.groups = GardenCatalog.DefaultGroups();
                EditorUtility.SetDirty(catalog);
            }
        }

        static void Fill(GardenCatalog catalog)
        {
            if (!AssetDatabase.IsValidFolder(catalog.prefabFolder))
            {
                EditorUtility.DisplayDialog("GardenXR", $"Dossier introuvable : {catalog.prefabFolder}", "OK");
                return;
            }

            Undo.RecordObject(catalog, "Remplir le catalogue");

            if (catalog.groups == null || catalog.groups.Count == 0)
                catalog.groups = GardenCatalog.DefaultGroups();

            var fallback = catalog.groups.LastOrDefault(g => !g.HasKeywords);
            if (fallback == null)
            {
                fallback = new CatalogGroup { name = "Autre" };
                catalog.groups.Add(fallback);
            }

            foreach (var g in catalog.groups) g.prefabs.Clear();

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { catalog.prefabFolder }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab == null) continue;

                CatalogGroup target = fallback;
                for (int i = catalog.groups.Count - 1; i >= 0; i--)
                {
                    if (catalog.groups[i].Matches(prefab.name)) { target = catalog.groups[i]; break; }
                }
                target.prefabs.Add(prefab);
            }

            EditorUtility.SetDirty(catalog);
            Debug.Log("[GardenCatalog] " + string.Join(" | ",
                catalog.groups.Select(g => $"{g.name} : {g.prefabs.Count}")));
        }
    }
}
#endif