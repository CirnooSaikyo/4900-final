using UnityEngine;

[CreateAssetMenu(menuName = "Conductor/OrchestraSection")]
public class OrchestraSection : ScriptableObject
{
    [Tooltip("Section name for UI and logs")]
    public string sectionName;

    [Tooltip("Matching SectionFlag bit")]
    public SectionFlag flag;

    [Tooltip("UI icon (can be null, uses themeColor as fallback)")]
    public Sprite icon;

    [Tooltip("Theme color for button highlights and particles")]
    public Color themeColor = Color.white;

    [Tooltip("Audio cue when this section is selected (optional)")]
    public AudioClip cueSound;
}
