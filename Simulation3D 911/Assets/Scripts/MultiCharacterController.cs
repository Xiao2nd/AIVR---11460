using UnityEngine;

public class MultiCharacterController : MonoBehaviour
{
    public Animator playerA;
    public Animator playerB;
    public Animator playerC;
    public Animator playerD;

    private Animator currentPlayer;

    void Start()
    {
        currentPlayer = playerA; // 不預設角色
        Debug.Log("選擇角色A");
    }

    void Update()
    {
        HandleCharacterSwitch();
        HandleActionInput();
    }

    void HandleCharacterSwitch()
    {
        if (Input.GetKeyDown(KeyCode.A))
        {
            currentPlayer = playerA;
            Debug.Log("切換到角色 A");
        }
        else if (Input.GetKeyDown(KeyCode.B))
        {
            currentPlayer = playerB;
            Debug.Log("切換到角色 B");
        }
        else if (Input.GetKeyDown(KeyCode.C))
        {
            currentPlayer = playerC;
            Debug.Log("切換到角色 C");
        }
        else if (Input.GetKeyDown(KeyCode.D))
        {
            currentPlayer = playerD;
            Debug.Log("切換到角色 D");
        }    
    }

    void HandleActionInput()
    {
        if (currentPlayer == null) return;

        string triggerToSet = null;

        if (Input.GetKeyDown(KeyCode.Alpha1)) triggerToSet = "Action1";
        else if (Input.GetKeyDown(KeyCode.Alpha2)) triggerToSet = "Action2";
        else if (Input.GetKeyDown(KeyCode.Alpha3)) triggerToSet = "Action3";
        else if (Input.GetKeyDown(KeyCode.Alpha4)) triggerToSet = "Action4";
        else if (Input.GetKeyDown(KeyCode.Alpha5)) triggerToSet = "Action5";

        if (!string.IsNullOrEmpty(triggerToSet))
        {
            TriggerCurrentCharacterAnimation(triggerToSet);
        }
    }

    // ✅ 提供外部呼叫的動畫控制方法（給 GPT 使用）
    public void TriggerCurrentCharacterAnimation(string trigger)
    {
        if (currentPlayer == null)
        {
            Debug.LogWarning("⚠️ 尚未選擇角色！");
            return;
        }

        // 重設其他角色的 trigger，避免同時播放
        if (currentPlayer != playerA) playerA.ResetTrigger(trigger);
        if (currentPlayer != playerB) playerB.ResetTrigger(trigger);
        if (currentPlayer != playerC) playerC.ResetTrigger(trigger);
        if (currentPlayer != playerD) playerD.ResetTrigger(trigger);

        // 執行目前角色的動畫
        currentPlayer.ResetTrigger(trigger);
        currentPlayer.SetTrigger(trigger);

        Debug.Log($"✅ 角色 {currentPlayer.gameObject.name} 執行動畫觸發器：{trigger}");
    }
}
