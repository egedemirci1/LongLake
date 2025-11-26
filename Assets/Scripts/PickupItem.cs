using UnityEngine;

public class PickupItem : MonoBehaviour
{
    public string itemName = "Item";

    public void OnPickup()
    {
        Debug.Log("Picked up: " + itemName);
        Destroy(gameObject);
    }
}
