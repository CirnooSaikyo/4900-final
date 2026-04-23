using UnityEngine;

[CreateAssetMenu(menuName = "Conductor/UltimateConfig")]
public class ConductorUltimateConfig : ScriptableObject
{
    [System.Serializable]
    public class CombinationEntry
    {
        [Tooltip("Section combo via flags, e.g. Winds|Strings")]
        public SectionFlag combination;

        [Tooltip("Effect name for debug output")]
        public string effectName;

        [Tooltip("Energy cost (unused in skeleton phase)")]
        public float energyCost;

        [Tooltip("Prefab with UltimateEffectBase subclass; leave empty to only drain energy")]
        public GameObject effectPrefab;
    }

    [Tooltip("4 combo entries: AB / AC / BC / ABC")]
    public CombinationEntry[] combinations;

    public CombinationEntry Resolve(SectionFlag flags)
    {
        if (combinations == null) return null;
        foreach (var entry in combinations)
        {
            if (entry.combination == flags)
                return entry;
        }
        return null;
    }
}
