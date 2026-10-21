using System.Collections.Generic;
using UnityEngine;

namespace GardenXR
{
    /// <summary>
    /// Liste de tous les éléments plaçables. Un seul asset suffit pour tout le projet.
    /// Clic droit > Create > GardenXR > Garden Catalog
    /// </summary>
    [CreateAssetMenu(menuName = "GardenXR/Garden Catalog", fileName = "GardenCatalog")]
    public class GardenCatalog : ScriptableObject
    {
        [Tooltip("Utilisé par le bouton 'Remplir depuis le dossier' de l'inspecteur.")]
        public string prefabFolder = "Assets/Prefabs/Garden";

        [Tooltip("Prefabs bruts : utilisés si aucune GardenItemData n'est renseignée.")]
        public List<GameObject> prefabs = new List<GameObject>();

        [Tooltip("Fiches détaillées (icône, catégorie). Prioritaires sur la liste brute.")]
        public List<GardenItemData> items = new List<GardenItemData>();
    }
}
