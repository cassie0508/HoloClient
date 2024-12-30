using UnityEngine;
using UnityEngine.InputSystem;

public class HintController : MonoBehaviour
{
    // Define an array of positions for the Cylinder
    private Vector3[] positions = new Vector3[]
    {
        new Vector3(-0.000116f, 0.001123f, 0.000868f),
        new Vector3(0.000278f, 0.000738f, 0.000868f),
        new Vector3(-0.000142f, 0.000040f, 0.000868f),
        new Vector3(0.000017f, -0.000254f, 0.000868f),
        new Vector3(0.000255f, -0.000624f, 0.000868f),
        new Vector3(-0.000192f, -0.000956f, 0.000868f),
        new Vector3(0.000188f, -0.001232f, 0.000868f)
    };

    private int currentIndex = 0; // Current position index

    // Input Actions for Back and Next controls
    public InputAction BackAction; // Action to move back (X button)
    public InputAction NextAction; // Action to move forward (B button)

    private void OnEnable()
    {
        // Enable Input Actions
        BackAction.Enable();
        NextAction.Enable();

        // Subscribe to performed events for each action
        BackAction.performed += OnBackButtonPressed;
        NextAction.performed += OnNextButtonPressed;
    }

    private void OnDisable()
    {
        // Disable Input Actions
        BackAction.Disable();
        NextAction.Disable();

        // Unsubscribe from performed events to prevent memory leaks
        BackAction.performed -= OnBackButtonPressed;
        NextAction.performed -= OnNextButtonPressed;
    }

    // Called when the Back button (X) is pressed
    private void OnBackButtonPressed(InputAction.CallbackContext context)
    {
        if (currentIndex > 0) // Ensure the index doesn't go below 0
        {
            currentIndex--; // Decrease the index
            UpdatePosition(); // Update the position of the Cylinder
        }
    }

    // Called when the Next button (B) is pressed
    private void OnNextButtonPressed(InputAction.CallbackContext context)
    {
        if (currentIndex < positions.Length - 1) // Ensure the index doesn't exceed the array length
        {
            currentIndex++; // Increase the index
            UpdatePosition(); // Update the position of the Cylinder
        }
    }

    // Updates the position of the Cylinder based on the current index
    private void UpdatePosition()
    {
        transform.localPosition = positions[currentIndex]; // Set the position of the Cylinder
        Debug.Log($"Cylinder position updated to: {transform.localPosition}"); // Log the updated position
    }
}
