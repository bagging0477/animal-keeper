using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(SpriteRenderer))]
public class InstantButtonBomb2D : MonoBehaviour
{
    public Sprite fireSprite;
    [Min(0.05f)] public float effectDuration = 0.45f;
    [Min(0.1f)] public float effectScale = 2.5f;
    public bool enableSpaceKey = true;
    public UnityEvent onDetonated = new UnityEvent();
    private bool detonated;

    private void Update()
    {
        if (!enableSpaceKey || detonated) return;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) Detonate();
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Space)) Detonate();
#endif
    }

    [ContextMenu("Test Detonate (Play Mode)")]
    public void Detonate()
    {
        if (!Application.isPlaying || detonated) return;
        if (fireSprite == null)
        {
            Debug.LogWarning("Assign fire-explosion-32 to Fire Sprite.", this);
            return;
        }
        detonated = true;
        SpriteRenderer bomb = GetComponent<SpriteRenderer>();
        GameObject effect = new GameObject("Fire Explosion");
        effect.transform.position = transform.position;
        SpriteRenderer renderer = effect.AddComponent<SpriteRenderer>();
        renderer.sprite = fireSprite;
        renderer.sortingLayerID = bomb.sortingLayerID;
        renderer.sortingOrder = bomb.sortingOrder + 10;
        renderer.sharedMaterial = bomb.sharedMaterial;
        FireBurstEffect2D animation = effect.AddComponent<FireBurstEffect2D>();
        float worldScale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
        animation.Initialize(renderer, effectDuration, effectScale * worldScale);
        bomb.enabled = false;
        // Independent effect stays visible even if the owner is removed by an event.
        onDetonated.Invoke();
        if (this != null) Destroy(gameObject);
    }
}
