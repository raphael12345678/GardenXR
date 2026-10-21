using System;
using System.Collections.Generic;
using UnityEngine;

namespace GardenXR
{
    /// <summary>Un groupe du catalogue = un onglet dans la fenêtre.</summary>
    [Serializable]
    public class CatalogGroup
    {
        public string name = "Autre";

        [Tooltip("Mots-clés séparés par des virgules, cherchés dans le nom du prefab pour le classement automatique. Vide = groupe 'fourre-tout'.")]
        public string keywords = "";

        [Tooltip("Besoin en lumière des éléments de ce groupe. None = pas des plantes (pas d'alerte).")]
        public SunNeed sunNeed = SunNeed.MiOmbre;
        public WaterNeed waterNeed = WaterNeed.Moyen;

        public List<GameObject> prefabs = new List<GameObject>();

        public bool HasKeywords => !string.IsNullOrWhiteSpace(keywords);

        public bool Matches(string prefabName)
        {
            if (!HasKeywords) return false;
            string lower = prefabName.ToLowerInvariant();
            foreach (var raw in keywords.Split(','))
            {
                string k = raw.Trim().ToLowerInvariant();
                if (k.Length > 0 && lower.Contains(k)) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Catalogue de tous les éléments plaçables, rangés par groupes.
    /// Clic droit > Create > GardenXR > Garden Catalog
    /// </summary>
    [CreateAssetMenu(menuName = "GardenXR/Garden Catalog", fileName = "GardenCatalog")]
    public class GardenCatalog : ScriptableObject
    {
        [Tooltip("Dossier scanné par le bouton 'Remplir depuis le dossier'.")]
        public string prefabFolder = "Assets/Garden Catalog";

        [Tooltip("Ordre de la liste = ordre des onglets. En cas de doute, le groupe le plus bas dans la liste gagne.")]
        public List<CatalogGroup> groups = DefaultGroups();

        public static List<CatalogGroup> DefaultGroups() => new List<CatalogGroup>
        {
            new CatalogGroup { name = "Plantes", sunNeed = SunNeed.MiOmbre,     waterNeed = WaterNeed.Moyen,
                               keywords = "plant, plante, bush, buisson, shrub, arbuste, grass, herbe, fern, fougere, leaf, feuille, cactus, succulent, ivy, lierre, hedge, haie" },
            new CatalogGroup { name = "Arbres",  sunNeed = SunNeed.PleinSoleil, waterNeed = WaterNeed.Moyen,
                               keywords = "tree, arbre, palm, palmier, pine, sapin, oak, chene, birch, bouleau" },
            new CatalogGroup { name = "Fleurs",  sunNeed = SunNeed.PleinSoleil, waterNeed = WaterNeed.Eleve,
                               keywords = "flower, fleur, rose, tulip, tulipe, daisy, marguerite, lily, lys, sunflower, tournesol, lavender, lavande, bloom, blossom" },
            new CatalogGroup { name = "Autre",   sunNeed = SunNeed.None,        waterNeed = WaterNeed.None,
                               keywords = "" },
        };
    }
}
