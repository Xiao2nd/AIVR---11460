using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using SimpleFileBrowser;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 呼叫後端產生模型，並把 GLB / 縮圖存到正確的平台路徑：
/// - Editor/PC:   Assets/Resources/BackpackModels, Assets/Resources/ModelThumbnails
/// - Android/VR:  persistentDataPath/BackpackModels, persistentDataPath/ModelThumbnails
///
/// 後端回應型態支援：
/// 1) 直接回 GLB 二進位 (content-type: model/gltf-binary 或 application/octet-stream)
/// 2) JSON: { glbBase64, thumbBase64?, fileName? }
/// </summary>
public class NewApiCreate : MonoBehaviour
{
    [Header("Server")]
    [Tooltip("你的後端 API 入口，例如 http://127.0.0.1:8081/generate")]
    public string serverUrl = "http://127.0.0.1:8081/generate";

    [Tooltip("可選：若需要認證，會帶在 Authorization: Bearer <apiKey>")]
    public string apiKey = "";

    [Tooltip("請求逾時（秒）")]
    public int timeoutSeconds = 300;

    [Header("Request Payload (example)")]
    [Tooltip("示範參數（如後端需要文字參數才使用）")]
    public string prompt = "a desk";

    [Serializable] public class ImageOnlyPayload { public string image; }
    [Serializable] public class SimpleGenerateRequest { public string prompt; }
    [Serializable]
    public class JsonResponse
    {
        public string glbBase64;
        public string thumbBase64;
        public string fileName;
    }

    /// <summary>存檔成功通知 (glbPath, thumbPath)。thumbPath 可能為 null。</summary>
    public event Action<string, string> OnModelSaved;

    #region Public entry points

    /// <summary>
    /// 叫出檔案挑選器，選一張圖片後：轉 Base64 → JSON POST 到 /generate
    /// </summary>
    public void SelectImageAndGenerate()
    {
        StartCoroutine(FileBrowserUtil.PickImagePath(path =>
        {
            if (string.IsNullOrEmpty(path))
            {
                Debug.Log("[NewApiCreate] 使用者取消選擇圖片");
                return;
            }

            try
            {
                if (!CrossPlatformFileExists(path))
                {
                    Debug.LogError("[NewApiCreate] 圖片檔不存在或無法存取: " + path);
                    return;
                }

                // 這裡會自動處理 content:// 或一般檔案路徑
                byte[] imgBytes = CrossPlatformReadAllBytes(path);
                string b64 = Convert.ToBase64String(imgBytes);

                var payload = new ImageOnlyPayload { image = b64 };
                StartCoroutine(PostGenerateAndSave(serverUrl, payload));
            }
            catch (Exception e)
            {
                Debug.LogError("[NewApiCreate] 讀圖失敗: " + e);
            }
        }));
    }


    private static bool CrossPlatformFileExists(string path)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
    if (!string.IsNullOrEmpty(path) && path.StartsWith("content://"))
        return FileBrowserHelpers.FileExists(path);
#endif
        return File.Exists(path);
    }

    private static byte[] CrossPlatformReadAllBytes(string path)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
    if (!string.IsNullOrEmpty(path) && path.StartsWith("content://"))
        return FileBrowserHelpers.ReadBytesFromFile(path);
#endif
        return File.ReadAllBytes(path);
    }

    /// <summary>
    /// 範例：以 JSON（含 prompt）送到 /generate
    /// </summary>
    public void GenerateModel()
    {
        var payload = new SimpleGenerateRequest { prompt = prompt };
        StartCoroutine(PostGenerateAndSave(serverUrl, payload));
    }

    /// <summary>
    /// 想自行組 payload 時可呼叫這個（payload 會被轉成 JSON）。
    /// </summary>
    public void GenerateModel(object payload)
    {
        StartCoroutine(PostGenerateAndSave(serverUrl, payload));
    }

    #endregion

    #region HTTP & save pipeline

    private IEnumerator PostGenerateAndSave(string url, object payload)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            Debug.LogError("[NewApiCreate] serverUrl 未設定");
            yield break;
        }

        // 準備 JSON
        string json = JsonUtility.ToJson(payload ?? new SimpleGenerateRequest { prompt = prompt });
        byte[] body = Encoding.UTF8.GetBytes(json);

        using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            req.timeout = timeoutSeconds;
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(apiKey))
                req.SetRequestHeader("Authorization", "Bearer " + apiKey);

            Debug.Log($"[NewApiCreate] POST {url}\nPayload: {json}");
            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool hasError = req.result == UnityWebRequest.Result.ProtocolError ||
                            req.result == UnityWebRequest.Result.ConnectionError ||
                            req.result == UnityWebRequest.Result.DataProcessingError;
#else
            bool hasError = req.isNetworkError || req.isHttpError;
#endif
            if (hasError)
            {
                Debug.LogError($"[NewApiCreate] HTTP Error {req.responseCode}: {req.error}\n{req.downloadHandler.text}");
                yield break;
            }

            string contentType = (req.GetResponseHeader("content-type") ?? "").ToLowerInvariant();
            Debug.Log($"[NewApiCreate] Response content-type: {contentType}");

            // 情況 A：直接回 GLB 二進位（常見為 model/gltf-binary 或 application/octet-stream）
            if (contentType.Contains("model/gltf-binary") || contentType.Contains("octet-stream") || contentType.Contains("gltf"))
            {
                byte[] glbBytes = req.downloadHandler.data;
                if (glbBytes == null || glbBytes.Length == 0)
                {
                    Debug.LogError("[NewApiCreate] 後端回傳空的 GLB");
                    yield break;
                }

                // 優先從 Content-Disposition 取檔名，否則用 timestamp
                string fileNameNoExt = SafeFileName(
                    GetFileNameFromContentDisposition(req.GetResponseHeader("Content-Disposition"))
                    ?? DateTime.Now.ToString("yyyyMMdd_HHmmss")
                );

                yield return SaveAndNotify(glbBytes, null, fileNameNoExt);
            }
            else
            {
                // 情況 B：JSON（包含 base64）
                string text = req.downloadHandler.text;
                if (string.IsNullOrEmpty(text))
                {
                    Debug.LogError("[NewApiCreate] 後端回傳空字串");
                    yield break;
                }

                JsonResponse resp;
                try
                {
                    resp = JsonUtility.FromJson<JsonResponse>(text);
                }
                catch (Exception e)
                {
                    Debug.LogError("[NewApiCreate] 無法解析 JSON 回應：" + e + "\nRaw: " + text);
                    yield break;
                }

                if (resp == null || string.IsNullOrEmpty(resp.glbBase64))
                {
                    Debug.LogError("[NewApiCreate] JSON 回應缺少 glbBase64");
                    yield break;
                }

                byte[] glbBytes = Convert.FromBase64String(resp.glbBase64);
                byte[] thumbBytes = null;
                if (!string.IsNullOrEmpty(resp.thumbBase64))
                    thumbBytes = Convert.FromBase64String(resp.thumbBase64);

                string fileNameNoExt = SafeFileName(
                    string.IsNullOrEmpty(resp.fileName) ? DateTime.Now.ToString("yyyyMMdd_HHmmss") : resp.fileName
                );

                yield return SaveAndNotify(glbBytes, thumbBytes, fileNameNoExt);
            }
        }
    }

    private IEnumerator SaveAndNotify(byte[] glbBytes, byte[] thumbBytes, string nameNoExt)
    {
        var task = SaveGeneratedModel(glbBytes, thumbBytes, nameNoExt);
        while (!task.IsCompleted) yield return null;

        var (glbPath, thumbPath) = task.Result;
        if (!string.IsNullOrEmpty(glbPath))
            OnModelSaved?.Invoke(glbPath, thumbPath);
    }

    #endregion

    #region Save to correct directories (PC/VR)

    /// <summary>把 GLB / PNG 縮圖寫到正確資料夾。</summary>
    public async Task<(string glbPath, string thumbPath)> SaveGeneratedModel(
        byte[] glbBytes, byte[] thumbnailPngBytes = null, string preferredNameNoExt = null)
    {
        if (glbBytes == null || glbBytes.Length == 0)
        {
            Debug.LogError("[NewApiCreate] glbBytes is null or empty.");
            return (null, null);
        }

        string nameNoExt = !string.IsNullOrEmpty(preferredNameNoExt)
            ? SafeFileName(preferredNameNoExt)
            : DateTime.Now.ToString("yyyyMMdd_HHmmss");

        // 取得寫入資料夾
        string modelsDir = GetModelsWriteDirCompat();
        string thumbsDir = GetThumbsWriteDir();

        EnsureDir(modelsDir);
        EnsureDir(thumbsDir);

        string glbPath = Path.Combine(modelsDir, nameNoExt + ".glb");
        string thumbPath = (thumbnailPngBytes != null && thumbnailPngBytes.Length > 0)
            ? Path.Combine(thumbsDir, nameNoExt + ".png")
            : null;

        try
        {
            await WriteAllBytesAsync(glbPath, glbBytes);
            if (!string.IsNullOrEmpty(thumbPath))
                await WriteAllBytesAsync(thumbPath, thumbnailPngBytes);

#if UNITY_EDITOR
            ImportAndRefreshIfUnderAssets(glbPath);
            if (!string.IsNullOrEmpty(thumbPath))
                ImportAndRefreshIfUnderAssets(thumbPath);
#endif
            Debug.Log($"✅ Saved GLB: {glbPath}");
            if (!string.IsNullOrEmpty(thumbPath))
                Debug.Log($"✅ Saved Thumbnail: {thumbPath}");

            return (glbPath, thumbPath);
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ SaveGeneratedModel failed: {e}");
            return (null, null);
        }
    }

    // 若專案中有 BackpackAndBlockSystem 的靜態路徑方法，就優先使用
    private static string GetModelsWriteDirCompat()
    {
        var t = Type.GetType("BackpackAndBlockSystem");
        if (t != null)
        {
            var m = t.GetMethod("GetModelsWriteDir", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (m != null)
            {
                try
                {
                    var dir = m.Invoke(null, null) as string;
                    if (!string.IsNullOrEmpty(dir)) return dir;
                }
                catch { /* ignore */ }
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        return Path.Combine(Application.persistentDataPath, "BackpackModels");
#else
        return Path.Combine(Application.dataPath, "Resources/BackpackModels");
#endif
    }

    private static string GetThumbsWriteDir()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return Path.Combine(Application.persistentDataPath, "ModelThumbnails");
#else
        return Path.Combine(Application.dataPath, "Resources/ModelThumbnails");
#endif
    }

    private static void EnsureDir(string dir)
    {
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
    }

    private static async Task WriteAllBytesAsync(string path, byte[] data)
    {
        if (File.Exists(path)) File.Delete(path);
        using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await fs.WriteAsync(data, 0, data.Length);
        }
    }

#if UNITY_EDITOR
    private static void ImportAndRefreshIfUnderAssets(string absPath)
    {
        string projectPath = Application.dataPath.Replace('\\', '/');
        string normalized = absPath.Replace('\\', '/');
        if (normalized.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
        {
            string assetPath = "Assets" + normalized.Substring(projectPath.Length);
            AssetDatabase.ImportAsset(assetPath);
            AssetDatabase.Refresh();
        }
    }
#endif

    #endregion

    #region helpers

    private static string SafeFileName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "model";
        foreach (char c in Path.GetInvalidFileNameChars()) raw = raw.Replace(c, '_');
        return raw.Trim();
    }

    private static string GetFileNameFromContentDisposition(string cd)
    {
        // 例如: attachment; filename="desk1.glb"
        if (string.IsNullOrEmpty(cd)) return null;
        const string key = "filename=";
        int idx = cd.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        string part = cd.Substring(idx + key.Length).Trim().Trim('"');
        if (part.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
            part = part.Substring(0, part.Length - 4);
        return part;
    }

    #endregion
}
