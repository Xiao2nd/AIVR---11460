using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using TMPro;

public class Hunyuan3DSender : MonoBehaviour
{
    public TMP_InputField inputField;

    public void OnGenerateClick()
    {
        string prompt = inputField.text;
        StartCoroutine(SendPromptToHunyuan(prompt));
    }

    IEnumerator SendPromptToHunyuan(string prompt)
    {
        string url = "http://127.0.0.1:8081/generate";
        var requestData = new RequestData { text = prompt };
        string json = JsonUtility.ToJson(requestData);

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("✅ 生成成功，模型已儲存在伺服器端！");
            // 可進一步加載檔案或提示用戶
        }
        else
        {
            Debug.LogError("❌ 發送失敗：" + request.error);
        }
    }

    [System.Serializable]
    public class RequestData
    {
        public string text;
    }
}
