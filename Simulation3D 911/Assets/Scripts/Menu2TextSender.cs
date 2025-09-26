using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using UnityEngine.Networking;
using System;

public class Menu2TextSender : MonoBehaviour
{
    public TMP_InputField userInputField;
    public Button sendButton;
    public TextMeshProUGUI resultText;

    private const string sendUrl = "http://127.0.0.1:8080/send";
    private const string statusUrl = "http://127.0.0.1:8080/status/";

    void Start()
    {
        sendButton.onClick.AddListener(OnSendButtonClicked);
    }

    void OnSendButtonClicked()
    {
        string textToSend = userInputField.text;
        if (!string.IsNullOrEmpty(textToSend))
        {
            StartCoroutine(SendTextAndPollStatus(textToSend));
        }
        else
        {
            Debug.LogWarning("❗ 輸入內容為空");
        }
    }

    IEnumerator SendTextAndPollStatus(string prompt)
    {
        // 準備 JSON 資料
        string jsonData = "{\"text\":\"" + prompt + "\"}";
        byte[] postData = System.Text.Encoding.UTF8.GetBytes(jsonData);

        UnityWebRequest request = new UnityWebRequest(sendUrl, "POST");
        request.uploadHandler = new UploadHandlerRaw(postData);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        // 傳送 POST 請求
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("❌ 傳送失敗: " + request.error);
            yield break;
        }

        // 解析回傳 JSON，取得 uid
        string responseText = request.downloadHandler.text;
        string uid = ExtractUid(responseText);
        if (string.IsNullOrEmpty(uid))
        {
            Debug.LogError("❌ 回傳格式錯誤，無法解析 uid: " + responseText);
            yield break;
        }

        Debug.Log("✅ 任務已提交，UID: " + uid);

        // 輪詢 status
        string checkUrl = statusUrl + uid;
        while (true)
        {
            UnityWebRequest statusRequest = UnityWebRequest.Get(checkUrl);
            yield return statusRequest.SendWebRequest();

            if (statusRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("❌ 輪詢失敗: " + statusRequest.error);
                yield break;
            }

            string statusResponse = statusRequest.downloadHandler.text;
            Debug.Log("📡 狀態回應: " + statusResponse);

            if (statusResponse.Contains("\"status\": \"done\""))
            {
                string outputPath = ExtractOutputPath(statusResponse);
                Debug.Log("✅ 任務完成，輸出: " + outputPath);
                if (resultText != null)
                    resultText.text = "✅ 輸出: " + outputPath;
                yield break;
            }
            else if (statusResponse.Contains("\"status\": \"error\""))
            {
                Debug.LogError("❌ 任務執行錯誤: " + statusResponse);
                yield break;
            }

            yield return new WaitForSeconds(2f); // 每2秒輪詢一次
        }
    }

    // 簡單解析 uid
    string ExtractUid(string json)
    {
        try
        {
            int idx = json.IndexOf("\"uid\":");
            if (idx == -1) return null;
            int start = json.IndexOf("\"", idx + 6) + 1;
            int end = json.IndexOf("\"", start);
            return json.Substring(start, end - start);
        }
        catch { return null; }
    }

    // 簡單解析 output_path
    string ExtractOutputPath(string json)
    {
        try
        {
            int idx = json.IndexOf("\"output_path\":");
            if (idx == -1) return null;
            int start = json.IndexOf("\"", idx + 14) + 1;
            int end = json.IndexOf("\"", start);
            return json.Substring(start, end - start);
        }
        catch { return null; }
    }
}
