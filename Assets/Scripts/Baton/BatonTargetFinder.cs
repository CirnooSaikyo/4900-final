using UnityEngine;

[DisallowMultipleComponent]
public class BatonTargetFinder : MonoBehaviour
{
    [SerializeField] private LayerMask _enemyLayer;

    public Transform FindTarget(Vector3 searchCenter, float searchRange, Camera cam)
    {
        Collider[] hits = Physics.OverlapSphere(searchCenter, searchRange, _enemyLayer);
        if (hits == null || hits.Length == 0 || cam == null)
            return null;

        Vector2 screenCenter = new(Screen.width * 0.5f, Screen.height * 0.5f);
        Transform best = null;
        float bestDistSqr = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            Transform t = hits[i].transform;
            Vector3 sp = cam.WorldToScreenPoint(t.position);
            if (sp.z <= 0f)
                continue;

            float d = (new Vector2(sp.x, sp.y) - screenCenter).sqrMagnitude;
            if (d < bestDistSqr)
            {
                bestDistSqr = d;
                best = t;
            }
        }

        return best;
    }

    /// <summary>Falls back to camera's horizontal forward when no target found</summary>
    public static Vector3 GetFallbackDirection(Camera cam)
    {
        if (cam == null)
            return Vector3.forward;

        Vector3 forward = cam.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f)
            return Vector3.forward;

        return forward.normalized;
    }
}
