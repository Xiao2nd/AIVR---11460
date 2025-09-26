using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;

public class TextInputHandler : MonoBehaviour
{
    public TMP_InputField inputField;
    public TMP_Text displayText;
    public Button showTextButton;

    void Start()
    {
        showTextButton.onClick.AddListener(OnShowTextClicked);
    }

    void OnShowTextClicked()
    {
        string userInput = inputField.text;
        displayText.text = userInput;

        StartCoroutine(SendTextToPython(userInput));  // 新增這行
    }

    IEnumerator SendTextToPython(string text)
    {
        string url = "http://127.0.0.1:5000/generate";

        string json = JsonUtility.ToJson(new Payload() { text = text });

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("✅ 成功：" + request.downloadHandler.text);
        }
        else
        {
            Debug.LogError("❌ 失敗：" + request.error);
        }
    }

    [System.Serializable]
    public class Payload
    {
        public string text;
    }
}
