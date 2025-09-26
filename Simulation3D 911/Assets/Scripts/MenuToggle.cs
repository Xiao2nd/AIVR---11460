using UnityEngine;
using UnityEngine.InputSystem;

public class MenuToggle : MonoBehaviour
{
    public GameObject mainMenuCanvas;
    public GameObject backpackCanvas;
    public GameObject generationCanvas;
    public GameObject characterCanvas;
    public CameraController cameraController;

    private PlayerControls controls;
    private bool isMainMenuOpen = false;

    private void Awake()
    {
        controls = new PlayerControls();
        controls.UI.ToggleMenu.performed += ctx => ToggleMainMenu();
    }

    private void OnEnable() => controls.UI.Enable();
    private void OnDisable() => controls.UI.Disable();

    private void ToggleMainMenu()
    {
        // 如果要關掉主選單
        if (isMainMenuOpen)
        {
            mainMenuCanvas.SetActive(false);
            isMainMenuOpen = false;
        }
        else
        {
            // 關閉其他子 Canvas（避免主選單以外的內容殘留）
            backpackCanvas.SetActive(false);
            generationCanvas.SetActive(false);
            characterCanvas.SetActive(false);

            mainMenuCanvas.SetActive(true);
            isMainMenuOpen = true;
            MoveMenuInFront();
        }

        // 控制滑鼠與鏡頭
        Cursor.lockState = isMainMenuOpen ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = isMainMenuOpen;

        if (cameraController != null)
            cameraController.allowCameraControl = !isMainMenuOpen;
    }

    private void MoveMenuInFront()
    {
        if (mainMenuCanvas == null) return;

        Transform cam = Camera.main.transform;
        Vector3 forward = cam.forward;
        forward.y = 0;
        forward.Normalize();

        float distance = 3.0f; // 加大距離
        Vector3 targetPosition = cam.position + forward * distance + Vector3.up * 0.1f;

        mainMenuCanvas.transform.position = targetPosition;
        mainMenuCanvas.transform.rotation = Quaternion.LookRotation(mainMenuCanvas.transform.position - cam.position );
    }

}
