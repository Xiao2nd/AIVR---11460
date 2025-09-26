using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.Networking;
using System.IO;

public class ImageUploader : MonoBehaviour
{
    public Button uploadButton;

    void Start()
    {
        uploadButton.onClick.AddListener(UploadImage);
    }

    void UploadImage()
    {
        string path = ImageSelector.selectedImagePath;

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Debug.LogError("❌ 圖片路徑無效或尚未選擇圖片！");
            return;
        }

        StartCoroutine(UploadImageCoroutine(path));
    }

    IEnumerator UploadImageCoroutine(string path)
    {
        byte[] imageData = File.ReadAllBytes(path);
        WWWForm form = new WWWForm();
        form.AddBinaryData("image", imageData, Path.GetFileName(path), "image/jpeg");

        UnityWebRequest request = UnityWebRequest.Post("http://127.0.0.1:5000/generate_image", form);
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("✅ 成功傳送圖片並生成場景: " + request.downloadHandler.text);
        }
        else
        {
            Debug.LogError("❌ 上傳失敗: " + request.error + " 圖片路徑: " + path);
        }
    }
}
