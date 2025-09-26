using UnityEngine;
using UnityEngine.InputSystem; // ✅ 新輸入系統

public class OneCharacterController : MonoBehaviour
{
    public Transform spawnPoint; // 角色生成位置
    private GameObject currentCharacter;
    private Animator currentPlayer;
    private string currentGender = "woman"; // 預設為女性

    void Update()
    {
        HandleActionInput();
    }

    void HandleActionInput()
    {
        if (currentPlayer == null) return;

        string triggerToSet = null;

        // --- Keyboard（新系統）---
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) triggerToSet = "Action1";
            else if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) triggerToSet = "Action2";
            else if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) triggerToSet = "Action3";
            else if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) triggerToSet = "Action4";
            else if (kb.digit5Key.wasPressedThisFrame || kb.numpad5Key.wasPressedThisFrame) triggerToSet = "Action5";
        }

        // --- Gamepad（可選）---
        var gp = Gamepad.current;
        if (gp != null && triggerToSet == null)
        {
            if (gp.buttonSouth.wasPressedThisFrame) triggerToSet = "Action1";   // A / Cross
            else if (gp.buttonEast.wasPressedThisFrame) triggerToSet = "Action2"; // B / Circle
            else if (gp.buttonWest.wasPressedThisFrame) triggerToSet = "Action3"; // X / Square
            else if (gp.buttonNorth.wasPressedThisFrame) triggerToSet = "Action4"; // Y / Triangle
            else if (gp.startButton.wasPressedThisFrame) triggerToSet = "Action5";
        }

        if (!string.IsNullOrEmpty(triggerToSet))
        {
            TriggerCurrentCharacterAnimation(triggerToSet);
        }
    }

    public string GetCurrentCharacterGender()
    {
        return currentGender;
    }

    public void SpawnCharacter(GameObject prefab)
    {
        // 移除原本角色
        if (currentCharacter != null)
        {
            Destroy(currentCharacter);
        }

        // 建立新角色
        currentCharacter = Instantiate(prefab, spawnPoint.position, spawnPoint.rotation);
        currentPlayer = currentCharacter.GetComponent<Animator>();

        // 根據 prefab 名稱判斷性別
        string prefabName = prefab.name.ToLower();
        if (prefabName.StartsWith("ma_"))
        {
            currentGender = "man";
        }
        else if (prefabName.StartsWith("gl_"))
        {
            currentGender = "woman";
        }
        else
        {
            currentGender = "woman"; // 預設
            Debug.LogWarning($"⚠️ 無法從 prefab 名稱判斷性別：{prefab.name}，預設為 woman");
        }

        if (currentPlayer == null)
        {
            Debug.LogWarning("⚠️ 角色 Prefab 上找不到 Animator！");
        }
        else
        {
            Debug.Log($"✅ 角色 {currentCharacter.name}（{currentGender}）已進場並可控制。");
        }
    }

    public void TriggerCurrentCharacterAnimation(string trigger)
    {
        if (currentPlayer == null)
        {
            Debug.LogWarning("⚠️ 尚未選擇角色！");
            return;
        }

        currentPlayer.ResetTrigger(trigger);
        currentPlayer.SetTrigger(trigger);

        Debug.Log($"🎬 {currentPlayer.gameObject.name} 播放動畫：{trigger}");
    }
}
