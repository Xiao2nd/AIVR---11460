using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.Networking;
using System.IO;

public class Menu2ImageSender : MonoBehaviour
{
    public Button sendButton;

    void Start()
    {
        if (sendButton != null)
            sendButton.onClick.AddListener(OnSendImage);
        else
            Debug.LogError("❌ Send Button 未綁定");
    }

    void OnSendImage()
    {
        string imagePath = ImageSelector.selectedImagePath;

        if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
        {
            byte[] imageBytes = File.ReadAllBytes(imagePath);
            string base64Image = System.Convert.ToBase64String(imageBytes);
            StartCoroutine(SendImageToServer(base64Image));
        }
        else
        {
            Debug.LogWarning("⚠️ 沒有選擇圖片或圖片路徑無效");
        }
    }

    [System.Serializable]
    public class ModelRequest
    {
        public string image;
        public bool texture = true;
        public int face_count = 30000;
        public string type = "glb";
        public int seed = 1234;
        public int octree_resolution = 128;
        public int num_inference_steps = 5;
        public float guidance_scale = 5.0f;
    }

    IEnumerator SendImageToServer(string base64Image)
    {
        string url = "http://127.0.0.1:8080/send";

        ModelRequest requestData = new ModelRequest();
        requestData.image = base64Image;

        string jsonData = JsonUtility.ToJson(requestData);
        byte[] postData = System.Text.Encoding.UTF8.GetBytes(jsonData);

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(postData);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("✅ 圖片傳送成功: " + request.downloadHandler.text);
            string uid = ExtractUid(request.downloadHandler.text);
            Object.FindFirstObjectByType<GlbDownloader>()?.DownloadGLB(uid);
        }
        else
        {
            Debug.LogError("❌ 圖片傳送失敗: " + request.error);
        }
    }

    string ExtractUid(string json)
    {
        string key = "\"uid\":\"";
        int start = json.IndexOf(key);
        if (start == -1) return "";
        start += key.Length;
        int end = json.IndexOf("\"", start);
        return json.Substring(start, end - start);
    }
}
