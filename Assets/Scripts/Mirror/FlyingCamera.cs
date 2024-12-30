using System.Collections;
using UnityEngine;

public class FlyingCamera : MonoBehaviour
{
#if UNITY_EDITOR
    public float rotation_speed = 3;
    public float movement_speed = 0.3f;

    private Vector2 MousePosition;

    public void Start()
    {
        MousePosition = Input.mousePosition;

    }

    private void OnApplicationFocus(bool focus)
    {
        MousePosition = Input.mousePosition;
    }

    private void Update()
    {

        if (Input.GetMouseButton(1))
        {

            var currentMousePos = Input.mousePosition;
            
            if(Vector2.Distance(MousePosition, currentMousePos) > Screen.width / 10)
            {
                MousePosition = currentMousePos;
            }
            float x = 0.05f * (Input.mousePosition.x - MousePosition.x);
            float y = -0.05f * (Input.mousePosition.y - MousePosition.y);

            transform.eulerAngles += new Vector3(y, x);
        }

        float speedMultiplier = Input.GetKey(KeyCode.LeftShift)? 10: 1;
        var position = transform.position;
        if (Input.GetKey(KeyCode.W))
        {
            position += speedMultiplier * movement_speed * transform.forward * Time.deltaTime;
        }

        if (Input.GetKey(KeyCode.S))
        {
            position += speedMultiplier * movement_speed * -transform.forward * Time.deltaTime;
        }

        if (Input.GetKey(KeyCode.D))
        {
            position += speedMultiplier * movement_speed * transform.right * Time.deltaTime;
        }

        if (Input.GetKey(KeyCode.A))
        {
            position += speedMultiplier * movement_speed * -transform.right * Time.deltaTime;
        }

        if (Input.GetKey(KeyCode.R))
        {
            position += speedMultiplier * movement_speed * transform.up * Time.deltaTime;
        }

        if (Input.GetKey(KeyCode.F))
        {
            position += speedMultiplier * movement_speed * -transform.up * Time.deltaTime;
        }

        if (Input.GetKey(KeyCode.Q))
        {
            transform.localEulerAngles += speedMultiplier * new Vector3(0,0,5) * Time.deltaTime;
        }

        if (Input.GetKey(KeyCode.E))
        {
            transform.localEulerAngles += speedMultiplier * new Vector3(0, 0, -5) * Time.deltaTime;
        }

            transform.position = position;
        MousePosition = Input.mousePosition;
    }
#endif
}
