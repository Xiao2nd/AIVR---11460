using UnityEngine;
using UnityEngine.UI;
using TMPro; // 若你使用的是 TextMeshPro

public class ScenarioUIManager : MonoBehaviour
{
    public TMP_InputField scenarioInputField; // 或 InputField
    public Button sendScenarioButton;
    public GPTActionResponder gptResponder;

    void Start()
    {
        sendScenarioButton.onClick.AddListener(OnScenarioSubmit);
    }

    void OnScenarioSubmit()
    {
        string scenario = scenarioInputField.text.Trim();

        if (!string.IsNullOrEmpty(scenario))
        {
            gptResponder.SetScenario(scenario);
            Debug.Log($"✅ 設定情境為：{scenario}");
        }
        else
        {
            Debug.LogWarning("⚠️ 請輸入情境文字");
        }
    }
}
