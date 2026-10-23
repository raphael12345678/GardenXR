#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GardenXR.EditorTools
{
    /// <summary>
    /// Boutons dans l'inspecteur du GardenCatalog :
    /// - Remplir depuis le dossier : charge tous les prefabs et les range par groupe selon leur nom.
    /// - Groupes par défaut : recrée Plantes / Arbres / Fleurs / Autre.
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
            EditorGUILayout.HelpBox(
                "Remplir range chaque prefab dans le premier groupe dont un mot-clé apparaît dans son nom " +
                "(en partant du bas de la liste). Les autres vont dans le groupe sans mots-clés (Autre). " +
                "Tu peux ensuite glisser un prefab d'une liste à l'autre à la main.",
                MessageType.Info);

            if (GUILayout.Button("Remplir depuis le dossier", GUILayout.Height(28)))
                Fill(catalog);

            if (GUILayout.Button("Ajouter les groupes manquants (Buissons, Cactus, Rochers...)"))
                AddMissingGroups(catalog);

            if (GUILayout.Button("Groupes par défaut (tout réinitialiser)"))
            {
                Undo.RecordObject(catalog, "Groupes par défaut");
                catalog.groups = GardenCatalog.DefaultGroups();
                EditorUtility.SetDirty(catalog);
            }
        }

        /// <summary>
        /// Ajoute les groupes par défaut absents et met à jour les mots-clés des groupes existants,
        /// SANS vider leurs listes de prefabs. "Autre" reste en dernier.
        /// </summary>
        static void AddMissingGroups(GardenCatalog catalog)
        {
            Undo.RecordObject(catalog, "Ajouter les groupes manquants");
            var defaults = GardenCatalog.DefaultGroups();
            var result = new System.Collections.Generic.List<CatalogGroup>();
            foreach (var d in defaults)
            {
                var existing = catalog.groups.FirstOrDefault(g => string.Equals(g.name, d.name, System.StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.keywords = d.keywords;
                    result.Add(existing);
                }
                else result.Add(d);
            }
            // Groupes personnalisés ajoutés à la main : conservés, juste avant "Autre"
            foreach (var g in catalog.groups)
                if (!result.Contains(g)) result.Insert(Mathf.Max(0, result.Count - 1), g);
            catalog.groups = result;
            EditorUtility.SetDirty(catalog);
            Debug.Log("[GardenCatalog] Groupes : " + string.Join(", ", result.Select(g => g.name)));
        }

        static void Fill(GardenCatalog catalog)
        {
            var folders = new System.Collections.Generic.List<string> { catalog.prefabFolder };
            if (catalog.extraFolders != null) folders.AddRange(catalog.extraFolders);
            folders = folders.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim().TrimEnd('/')).Distinct().ToList();
            var missing = folders.Where(f => !AssetDatabase.IsValidFolder(f)).ToList();
            if (missing.Count > 0)
            {
                EditorUtility.DisplayDialog("GardenXR", "Dossier(s) introuvable(s) :\n" + string.Join("\n", missing), "OK");
                return;
            }

            Undo.RecordObject(catalog, "Remplir le catalogue");

            if (catalog.groups == null || catalog.groups.Count == 0)
                catalog.groups = GardenCatalog.DefaultGroups();

            // Groupe fourre-tout : le dernier sans mots-clés, sinon on en crée un
            var fallback = catalog.groups.LastOrDefault(g => !g.HasKeywords);
            if (fallback == null)
            {
                fallback = new CatalogGroup { name = "Autre" };
                catalog.groups.Add(fallback);
            }

            foreach (var g in catalog.groups) g.prefabs.Clear();

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", folders.ToArray()))
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
