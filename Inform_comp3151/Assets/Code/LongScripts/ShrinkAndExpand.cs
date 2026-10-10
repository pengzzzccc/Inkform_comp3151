using UnityEngine;

public class ShrinkAndExpand : MonoBehaviour
{
    // Minimum scale multiplier relative to the original scale during the shrink phase
    [SerializeField] private float minScaleMultiplier = 0.8f;

    // Maximum scale multiplier relative to the original scale during the expand phase
    [SerializeField] private float maxScaleMultiplier = 1.2f;

    // Speed of the shrink and expand pulsing animation
    [SerializeField] private float pulseSpeed = 5f;

    // Tag used to identify the player object
    [SerializeField] private string playerTag = "Player";

    // Stores the original scale of the object when the game starts
    private Vector3 originalScale;

    // Tracks whether the player is currently inside the trigger area
    private bool isPlayerNearby = false;

    private void Start()
    {
        // Cache the initial scale of the object
        originalScale = transform.localScale;
    }

    private void Update()
    {
        if (isPlayerNearby)
        {
            // Create a smooth pulsing effect using a Sine wave mapped between min and max multipliers
            float scaleFactor = Mathf.Lerp(minScaleMultiplier, maxScaleMultiplier, (Mathf.Sin(Time.time * pulseSpeed) + 1f) / 2f);
            transform.localScale = originalScale * scaleFactor;
        }
        else
        {
            // Smoothly return to the original scale when the player leaves the zone
            transform.localScale = Vector3.Lerp(transform.localScale, originalScale, Time.deltaTime * pulseSpeed);
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        // Check if the object entering the trigger is the player
        if (collision.CompareTag(playerTag))
        {
            isPlayerNearby = true;
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        // Check if the object exiting the trigger is the player
        if (collision.CompareTag(playerTag))
        {
            isPlayerNearby = false;
        }
    }

    // Call this public method from your player interaction script when the item is collected
    public void PickUpItem()
    {
        // Stop tracking proximity and destroy the item object upon pickup
        isPlayerNearby = false;
        Destroy(gameObject);
    }
}