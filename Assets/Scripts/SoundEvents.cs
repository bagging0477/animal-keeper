using System;
using UnityEngine;

public static class SoundEvents
{
    public static event Action<Vector3> OnSoundEmitted;

    public static void Emit(Vector3 position)
    {
        OnSoundEmitted?.Invoke(position);
    }
}
