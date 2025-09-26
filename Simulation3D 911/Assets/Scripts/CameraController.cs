using UnityEngine;

public class CameraController : MonoBehaviour
{
    public Transform playerBody;
    public float sensitivity = 100f;
    public bool allowCameraControl = false; // ✅ 控制權開關

    private float pitch = 0f;
    private bool useZAxis = false;

    void Start()
    {
        if (playerBody == null)
            playerBody = transform.parent;

        pitch = transform.localEulerAngles.x;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (!allowCameraControl)
            return;

        float mouseX = Input.GetAxis("Mouse X") * sensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * sensitivity * Time.deltaTime;

        pitch -= mouseY;
        pitch = NormalizeAngle(pitch);
        Vector3 currentAngles = transform.localEulerAngles;
        transform.localRotation = Quaternion.Euler(pitch, currentAngles.y, currentAngles.z);

        if (Input.GetKeyDown(KeyCode.Z))
            useZAxis = !useZAxis;

        if (useZAxis)
            playerBody.Rotate(Vector3.forward * -mouseX);
        else
            playerBody.Rotate(Vector3.up * mouseX);
    }

    float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }
}
