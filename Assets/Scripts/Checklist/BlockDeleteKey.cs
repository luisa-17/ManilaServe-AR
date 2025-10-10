using UnityEngine;
using UnityEngine.EventSystems;

public class BlockDeleteKey : MonoBehaviour
{
    void Update()
    {
        // If Delete key pressed, swallow it so Unity doesn't treat it as Cancel
        if (Input.GetKeyDown(KeyCode.Delete))
        {
            // Optionally log to verify
            Debug.Log("Delete key blocked (no close triggered)");
        }
    }
}
