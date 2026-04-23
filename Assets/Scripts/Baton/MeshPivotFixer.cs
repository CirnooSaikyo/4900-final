using UnityEngine;

/// <summary>
/// Offsets mesh vertices at runtime to fix pivot placement.
/// Maya's Rotate Pivot isn't recognized by Unity animation,
/// so we shift verts to place the pivot at the handle end.
/// </summary>
public class MeshPivotFixer : MonoBehaviour
{
    [Tooltip("Vertex offset in local space (unscaled).\n" +
             "Positive Y = mesh shifts up, pivot at bottom.\n" +
             "Negative Y = mesh shifts down, pivot at top.")]
    [SerializeField] private Vector3 _pivotOffset = new Vector3(0f, 0.01f, 0f);

    private void Awake()
    {
        var mf = GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
            return;

        var mesh = Instantiate(mf.sharedMesh);
        var verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++)
            verts[i] += _pivotOffset;

        mesh.vertices = verts;
        mesh.RecalculateBounds();
        mf.mesh = mesh;
    }
}
