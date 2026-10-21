using UnityEngine;

namespace GardenXR
{
    public enum GardenCategory { Arbre, Arbuste, Fleur, Mobilier, Dallage, Decoration, Irrigation }

    /// <summary>Besoin en lumière d'une plante. None = pas une plante (décoration, pot...).</summary>
    public enum SunNeed { None, PleinSoleil, MiOmbre, Ombre }

    /// <summary>Besoin en eau d'une plante.</summary>
    public enum WaterNeed { None, Faible, Moyen, Eleve }

    /// <summary>
    /// Fiche d'un élément du catalogue (plante, mobilier, dallage...).
    /// Créée automatiquement par le catalogue à partir des groupes.
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

        [Header("Besoins (repris du groupe du catalogue)")]
        public string groupName;
        public SunNeed sunNeed = SunNeed.None;
        public WaterNeed waterNeed = WaterNeed.None;

        public bool IsPlant => sunNeed != SunNeed.None;
    }
}
