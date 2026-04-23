using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Base class for ultimate effects, instantiated and executed by UltimateState.
/// Fire-and-forget = buff-style (non-blocking); otherwise awaited as a cinematic.
/// </summary>
public abstract class UltimateEffectBase : MonoBehaviour
{
    public virtual bool IsFireAndForget => false;

    public abstract UniTask ExecuteAsync(Transform caster, CancellationToken ct);
}
