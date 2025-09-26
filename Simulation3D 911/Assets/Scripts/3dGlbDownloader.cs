using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.IO;

public class GlbDownloader : MonoBehaviour
{
    public string saveFolder = "DownloadedModels"; // 儲存資料夾（專案目錄下）

    public void DownloadGLB(string uid)
    {
        StartCoroutine(CheckStatusAndDownload(uid));
    }

    IEnumerator CheckStatusAndDownload(string uid)
    {
        string url = $"http://127.0.0.1:8080/status/{uid}";
        bool isDone = false;
        string modelBase64 = "";

        while (!isDone)
        {
            UnityWebRequest request = UnityWebRequest.Get(url);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("❌ 查詢失敗: " + request.error);
                yield break;
            }

            var json = request.downloadHandler.text;
            Debug.Log("查詢回應: " + json);

            if (json.Contains("completed"))
            {
                isDone = true;
                modelBase64 = ExtractBase64(json);
            }
            else
            {
                Debug.Log("⏳ 模型尚未完成，20秒後重試...");
                yield return new WaitForSeconds(20f);
            }
        }

        if (!string.IsNullOrEmpty(modelBase64))
        {
            byte[] glbBytes = System.Convert.FromBase64String(modelBase64);
            string path = Path.Combine(Application.dataPath, saveFolder);
            Directory.CreateDirectory(path);

            string fullPath = Path.Combine(path, "model_" + uid + ".glb");
            File.WriteAllBytes(fullPath, glbBytes);

            Debug.Log("✅ GLB 模型儲存完成: " + fullPath);
        }
    }

    // 非正式 JSON 解析，只抓 base64 字串
    string ExtractBase64(string json)
    {
        string key = "\"model_base64\":\"";
        int start = json.IndexOf(key);
        if (start == -1) return "";
        start += key.Length;
        int end = json.IndexOf("\"", start);
        return json.Substring(start, end - start);
    }
}
