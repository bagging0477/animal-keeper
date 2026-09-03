using UnityEngine;

public class AnimalSoundFlee : AnimalFlee
{
    protected override void OnStartFleeing()
    {
        Debug.Log($"동물이 소리를 냈다! 위치:({transform.position.x:F1}, {transform.position.y:F1})");
        SoundEvents.Emit(transform.position);
    }
}
