using UnityEngine;

namespace GardenXR
{
    public enum GardenCategory { Arbre, Arbuste, Fleur, Mobilier, Dallage, Decoration, Irrigation }

    /// <summary>
    /// Fiche d'un élément du catalogue (plante, mobilier, dallage...).
    /// Clic droit > Create > GardenXR > Garden Item
    /// </summary>
    [CreateAssetMenu(menuName = "GardenXR/Garden Item", fileName = "NewGardenItem")]
    public class GardenItemData : ScriptableObject
    {
        public string displayName;
        public Sprite icon;
        public GameObject prefab;
        public GardenCategory category = GardenCategory.Decoration;

        [Tooltip("Décalage vertical appliqué à la pose (si le pivot n'est pas à la base).")]
        public float yOffset = 0f;
    }
}
