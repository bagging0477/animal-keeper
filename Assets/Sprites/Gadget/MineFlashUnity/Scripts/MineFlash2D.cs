using System.Collections;
using UnityEngine;

[RequireComponent(typeof(SpriteRenderer), typeof(BoxCollider2D))]
public class MineFlash2D : MonoBehaviour
{
    [SerializeField] private Sprite flashSprite;
    [SerializeField, Min(0.05f)] private float flashDuration = 0.25f;
    [SerializeField, Min(0.1f)] private float flashSize = 1.8f;
    private bool triggered;
    private SpriteRenderer mineRenderer;

    private void Awake()
    {
        mineRenderer = GetComponent<SpriteRenderer>();
        GetComponent<BoxCollider2D>().isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        bool isPlayer = other.CompareTag("Player") ||
            (other.attachedRigidbody != null && other.attachedRigidbody.CompareTag("Player"));
        if (isPlayer) Activate();
    }

    [ContextMenu("Test Red Flash")]
    public void Activate()
    {
        if (!Application.isPlaying || triggered) return;
        if (flashSprite == null)
        {
            Debug.LogWarning("Assign red-flash-32 to Flash Sprite.", this);
            return;
        }
        triggered = true;
        GetComponent<BoxCollider2D>().enabled = false;
        StartCoroutine(ShowFlash());
    }

    private IEnumerator ShowFlash()
    {
        GameObject effect = new GameObject("RedFlash");
        effect.transform.SetParent(transform, false);
        SpriteRenderer renderer = effect.AddComponent<SpriteRenderer>();
        renderer.sprite = flashSprite;
        renderer.sortingLayerID = mineRenderer.sortingLayerID;
        renderer.sortingOrder = mineRenderer.sortingOrder + 10;
        mineRenderer.enabled = false;
        float elapsed = 0f;
        while (elapsed < flashDuration)
        {
            float progress = elapsed / flashDuration;
            effect.transform.localScale = Vector3.one * Mathf.Lerp(flashSize * 0.65f, flashSize, progress);
            renderer.color = new Color(1f, 1f, 1f, 1f - progress);
            elapsed += Time.deltaTime;
            yield return null;
        }
        Destroy(gameObject);
    }
}
