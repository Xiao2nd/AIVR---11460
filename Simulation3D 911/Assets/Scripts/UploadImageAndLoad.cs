using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using GLTFast;
using SimpleFileBrowser;  // 需安裝 SimpleFileBrowser 套件

// ====== DTOs ======
[Serializable] public class JobReply { public string job_id; public string status_url; public string download_url; }
[Serializable] public class JobStatus { public string status; public string message; public string download_url; }

public class UploadImageAndLoad : MonoBehaviour
{
    [Header("Server (填 Cloudflared 的主機或完整網址")]
    [Tooltip("例如：experimental-supported-improve-anthony.trycloudflare.com 或 https://experimental-supported-improve-anthony.trycloudflare.com")]
    [SerializeField] private string serverBaseInput = "https://<your-cloudflared-host>";
    [Tooltip("Flask 非同步 API 端點（建立工作）")]
    [SerializeField] private string jobsEndpoint = "/jobs";

    [Header("Local Save")]
    [SerializeField] private string folderName = "DownloadedModels";
    [SerializeField] private string filePrefix = "img2scene_";
    [SerializeField] private bool clearPreviousChildren = true;

    [Header("Safety Floor")]
    public bool addSafetyFloor = true;          // 是否自動鋪安全地板
    public float safetyFloorMarginY = 0.01f;    // 地板與模型最低點的垂直間隙
    public float safetyFloorPaddingXZ = 1.1f;   // 地板比模型邊界在 XZ 放大的倍數
    public PhysicsMaterial safetyFloorMaterial;  // （選用）物理材質


    [Header("Picked Image")]
    [SerializeField] private string pickedPath = "";  // 顯示目前選到的圖片路徑
                                                      // ===== Runtime 視覺/物理設定（可在 Inspector 調） =====
    [Header("Runtime Visual & Physics")]
    [SerializeField] private string placeableLayerName = "Placeable";
    [SerializeField] private bool addMeshColliders = true;
    [SerializeField] private bool addRigidbodies = false;
    [SerializeField] private bool autoCenterAndFit = true;  // 自動置中並把最大邊縮到 targetSize
    [SerializeField] private float targetSize = 2f;         // 自動縮放的目標最大邊（公尺）

    // ===== 讓 Runtime 套用固定 Transform（取代自動置中） =====
    [Header("Fixed Transform (像 ScenesImporter 一樣)")]
    [SerializeField] private bool useFixedTransform = true;                // 開啟後用下面三個值
    [SerializeField] private Vector3 fixedPosition = new Vector3(-1.3f, 0f, -1.6f);
    [SerializeField] private Vector3 fixedRotation = new Vector3(-90f, 0f, 0f); // 你原本常用
    [SerializeField] private Vector3 fixedScale = Vector3.one;

    // 若你之前有 autoCenterAndFit / targetSize，保留也沒關係；用這個開關決定誰生效


    [Tooltip("若 GLB 需要頂點色，指定支援 VertexColor 的 URP 材質；留空會退回 URP/Lit。")]
    [SerializeField] private Material vertexColorMaterial;

    [Tooltip("臨時地板用的 URP 材質；留空則自動生一個灰色 URP/Lit。")]
    [SerializeField] private Material baseFloorMaterial;

    private GameObject _tempFloor;
    private int _placeableLayer = -1;

    private void EnsureLayerAndFloor()
    {
        // Layer
        _placeableLayer = LayerMask.NameToLayer(placeableLayerName);
        if (_placeableLayer < 0) _placeableLayer = LayerMask.NameToLayer("Default");

        // 臨時地板（載入前放一塊，成功後移除）
        if (_tempFloor == null)
        {
            _tempFloor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _tempFloor.name = "TemporaryBaseFloor";
            _tempFloor.transform.position = Vector3.zero;
            _tempFloor.transform.localScale = new Vector3(10, 1, 10);
            _tempFloor.layer = _placeableLayer;

            var mr = _tempFloor.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                if (baseFloorMaterial == null)
                {
                    var lit = Shader.Find("Universal Render Pipeline/Lit");
                    baseFloorMaterial = new Material(lit);
                    if (baseFloorMaterial.HasProperty("_BaseColor"))
                        baseFloorMaterial.SetColor("_BaseColor", new Color(0.7f, 0.7f, 0.7f, 1f));
                }
                mr.material = baseFloorMaterial;
            }

            var rb = _tempFloor.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }
    }

    private void ApplyMaterialsRecursively(GameObject rootGO)
    {
        if (rootGO == null) return;

        // 如果沒有指定材質，就建立一個簡單的 URP/Lit 材質
        if (vertexColorMaterial == null)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            vertexColorMaterial = new Material(lit);
        }

        // 把指定材質套到所有 MeshRenderer
        foreach (var mr in rootGO.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = mr.sharedMaterials;
            if (mats == null || mats.Length == 0)
            {
                mr.sharedMaterial = vertexColorMaterial;
            }
            else
            {
                for (int i = 0; i < mats.Length; i++) mats[i] = vertexColorMaterial;
                mr.sharedMaterials = mats;
            }
        }
    }


    private void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform c in go.transform)
            SetLayerRecursively(c.gameObject, layer);
    }

    private void AddPhysicsRecursively(GameObject rootGO)
    {
        if (rootGO == null) return;

        if (addMeshColliders)
        {
            foreach (var mf in rootGO.GetComponentsInChildren<MeshFilter>(true))
            {
                // 確保每個 MeshFilter 都有 MeshCollider
                var mc = mf.GetComponent<MeshCollider>();
                if (mc == null) mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = false;
                mc.isTrigger = false;
            }
        }
        /*
                if (addRigidbodies)
                {
                    foreach (var mf in rootGO.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (!mf.TryGetComponent<Rigidbody>(out _))
                        {
                            var rb = mf.gameObject.AddComponent<Rigidbody>();

                            // 原本的設定：固定在原地，不受重力影響
                            rb.useGravity = false;
                            rb.isKinematic = true;
                        }
                    }
                }
        */
    }

    // 取得整棵物件的世界座標 Bounds（優先用 Renderer，退而求其次用 MeshFilter）
    private Bounds GetHierarchyWorldBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length > 0)
        {
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        var mfs = root.GetComponentsInChildren<MeshFilter>(true);
        if (mfs.Length > 0)
        {
            // 用第一個 Mesh 的世界 AABB 初始化
            var m = mfs[0];
            var wb = m.sharedMesh != null ? m.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.zero);
            var b = new Bounds(m.transform.TransformPoint(wb.center), Vector3.zero);
            b.Encapsulate(m.transform.TransformPoint(wb.min));
            b.Encapsulate(m.transform.TransformPoint(wb.max));
            for (int i = 1; i < mfs.Length; i++)
            {
                var mi = mfs[i];
                if (mi.sharedMesh == null) continue;
                var bb = mi.sharedMesh.bounds;
                b.Encapsulate(mi.transform.TransformPoint(bb.min));
                b.Encapsulate(mi.transform.TransformPoint(bb.max));
            }
            return b;
        }

        // 沒有可計算的元件，回傳一個極小的 Bounds
        return new Bounds(root.transform.position, Vector3.zero);
    }

    // 在模型最低點下方鋪一片不可見 BoxCollider 當安全網
    private void CreateSafetyFloorUnder(GameObject root)
    {
        if (!addSafetyFloor || root == null) return;

        Bounds b = GetHierarchyWorldBounds(root);
        if (b.size == Vector3.zero) return; // 沒東西可鋪

        float y = b.min.y - safetyFloorMarginY + 5.5f; //加高度

        var floor = new GameObject("SafetyFloor (auto)");
        // 套用與載入模型相同的 Placeable 圖層（若找不到則進 Default）
        int placeableLayer = LayerMask.NameToLayer(placeableLayerName);
        if (placeableLayer < 0) placeableLayer = 0;
        floor.layer = placeableLayer;

        // 位置放在模型的水平中心、最低點下方
        floor.transform.position = new Vector3(b.center.x, y, b.center.z);
        floor.transform.rotation = Quaternion.identity;
        floor.transform.localScale = Vector3.one;

        // 只要 Collider，不加任何 Renderer → 不可見
        var col = floor.AddComponent<BoxCollider>();
        float padX = Mathf.Max(0.1f, b.size.x * safetyFloorPaddingXZ);
        float padZ = Mathf.Max(0.1f, b.size.z * safetyFloorPaddingXZ);
        float thickness = Mathf.Max(0.1f, safetyFloorMarginY * 2f);
        col.size = new Vector3(padX, thickness, padZ);
        // 讓碰撞面剛好在 y 高度，Collider 體積往下長
        col.center = new Vector3(0f, -thickness * 0.5f, 0f);
        col.isTrigger = false;
        if (safetyFloorMaterial != null) col.material = safetyFloorMaterial;

        // 固定它在原地、無重力
        var rb = floor.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = true;
    }


    private void CenterAndFit(GameObject root)
    {
        var rens = root.GetComponentsInChildren<Renderer>();
        if (rens.Length == 0) return;

        Bounds b = rens[0].bounds;
        for (int i = 1; i < rens.Length; i++) b.Encapsulate(rens[i].bounds);

        // 置中到「這個腳本物件」的座標
        Vector3 offset = transform.position - b.center;
        root.transform.position += offset;

        float maxSize = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (maxSize > 0f)
        {
            float s = targetSize / maxSize;
            root.transform.localScale *= s;
        }
    }

    // ====== UI 綁定 ======
    public void OnPickImage() => StartCoroutine(PickWithSimpleFileBrowser());
    public void OnSendAndLoad() => StartCoroutine(SendAndLoadCoroutine());

    // ====== 選圖（SimpleFileBrowser） ======
    private IEnumerator PickWithSimpleFileBrowser()
    {
        FileBrowser.SetFilters(true, new FileBrowser.Filter("Images", ".png", ".jpg", ".jpeg"));
        FileBrowser.SetDefaultFilter(".png");
        FileBrowser.SingleClickMode = true;

        yield return FileBrowser.WaitForLoadDialog(FileBrowser.PickMode.Files, false, null, null, "Select Image", "Load");
        if (!FileBrowser.Success) yield break;

        pickedPath = FileBrowser.Result[0];
        Debug.Log(" imagepath：" + pickedPath);
    }

    // ====== 建立 Job 並開始輪詢 ======
    private IEnumerator SendAndLoadCoroutine()
    {
        string baseHttps = BuildHttpsBase(serverBaseInput);
        if (string.IsNullOrEmpty(baseHttps))
        {
            Debug.LogError(" serverBaseInput 無效，請填 cloudflared 網址或主機名");
            yield break;
        }
        bool exists =
#if UNITY_ANDROID && !UNITY_EDITOR
    SimpleFileBrowser.FileBrowserHelpers.FileExists(pickedPath);
#else
            File.Exists(pickedPath);
#endif

        if (string.IsNullOrWhiteSpace(pickedPath) || !exists)
        {
            Debug.LogError(" 尚未選圖或路徑不存在：" + pickedPath);
            yield break;
        }

        var root = GetDownloadRoot();
        Directory.CreateDirectory(root);
        string glbPath = Path.Combine(root, filePrefix + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".glb");

        byte[] imgBytes = null;
        try
        {
#if UNITY_ANDROID && !UNITY_EDITOR
    // 這行可直接讀取 content:// 的檔案
    imgBytes = SimpleFileBrowser.FileBrowserHelpers.ReadBytesFromFile(pickedPath);
#else
            imgBytes = File.ReadAllBytes(pickedPath);
#endif
        }
        catch (System.Exception ex)
        {
            Debug.LogError("讀圖失敗：" + ex.Message);
            yield break;
        }

        WWWForm form = new WWWForm();
        string fileName = Path.GetFileName(pickedPath);
        string mime = fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
        form.AddBinaryData("image", imgBytes, fileName, mime);
        form.AddField("mode", "i2s");

        // POST /jobs（強制 HTTPS）
        string createUrl = JoinUrl(baseHttps, jobsEndpoint);
        using (UnityWebRequest req = UnityWebRequest.Post(createUrl, form))
        {
            req.downloadHandler = new DownloadHandlerBuffer();  // 拿 JSON
            req.timeout = 0;

            Debug.Log("🚀 POST " + createUrl + " （上傳圖片：" + fileName + "）");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"❌ 建立 Job 失敗：{req.responseCode} {req.error}");
                yield break;
            }

            var reply = JsonUtility.FromJson<JobReply>(req.downloadHandler.text);
            if (reply == null || string.IsNullOrEmpty(reply.job_id))
            {
                Debug.LogError("❌ 無法解析 JobReply: " + req.downloadHandler.text);
                yield break;
            }
            Debug.Log("🆔 建立 Job 成功：" + reply.job_id);

            // 若後端回的是相對 URL，就補上 https://host
            string statusUrl = EnsureHttpsAbsolute(baseHttps, reply.status_url);
            string downloadUrl = EnsureHttpsAbsolute(baseHttps, reply.download_url);

            StartCoroutine(PollAndDownload(statusUrl, downloadUrl, glbPath));
        }
    }

    // ====== 輪詢狀態，完成後下載 GLB ======
    private IEnumerator PollAndDownload(string statusUrl, string downloadUrl, string glbPath)
    {
        float startTime = Time.time;
        while (true)
        {
            using (UnityWebRequest sreq = UnityWebRequest.Get(statusUrl))
            {
                sreq.downloadHandler = new DownloadHandlerBuffer();
                yield return sreq.SendWebRequest();

                if (sreq.result == UnityWebRequest.Result.Success)
                {
                    var status = JsonUtility.FromJson<JobStatus>(sreq.downloadHandler.text);
                    if (status != null)
                    {
                        Debug.Log($"🔄 Job 狀態：{status.status} - {status.message}");
                        if (status.status == "done")
                        {
                            // 最終下載網址以狀態內為主；若沒有則退回建立時那個
                            string finalUrl = !string.IsNullOrEmpty(status.download_url)
                                ? EnsureHttpsAbsolute(BuildHttpsBase(serverBaseInput), status.download_url)
                                : downloadUrl;

                            using (UnityWebRequest dreq = UnityWebRequest.Get(finalUrl))
                            {
                                dreq.downloadHandler = new DownloadHandlerFile(glbPath);
                                dreq.timeout = 0;
                                Debug.Log("⬇️ 下載 GLB：" + glbPath);
                                yield return dreq.SendWebRequest();

                                if (dreq.result == UnityWebRequest.Result.Success)
                                {
                                    yield return LoadGlbToScene(glbPath);
                                    yield break;
                                }
                                else
                                {
                                    Debug.LogError("❌ 下載失敗：" + dreq.error);
                                    yield break;
                                }
                            }
                        }
                        else if (status.status == "error")
                        {
                            Debug.LogError("❌ Job 執行錯誤：" + status.message);
                            yield break;
                        }
                    }
                }

                // 2 秒輪詢一次，最多 10 分鐘
                yield return new WaitForSeconds(2f);
                if (Time.time - startTime > 600f)
                {
                    Debug.LogError("❌ Job Timeout");
                    yield break;
                }
            }
        }
    }

    // ====== 路徑與載入 ======
    private string GetDownloadRoot()
    {
        string root = Path.Combine(Application.persistentDataPath, folderName);
        Debug.Log("📂 下載路徑：" + root);
        return root;
    }

    private IEnumerator LoadGlbToScene(string absPath)
    {
        if (!File.Exists(absPath))
        {
            Debug.LogError("❌ 檔案不存在：" + absPath);
            yield break;
        }

        // 清舊
        if (clearPreviousChildren)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
        }

        EnsureLayerAndFloor();

        Debug.Log("📦 載入 GLB：" + absPath);
        var gltf = new GLTFast.GltfImport();
        var load = gltf.Load(new Uri(new Uri(absPath).AbsoluteUri));
        while (!load.IsCompleted) yield return null;

        if (!load.Result)
        {
            Debug.LogError("❌ glTFast 載入失敗：" + absPath);
            yield break;
        }

        // 建一個根物件承載整個 GLB
        string rootName = Path.GetFileNameWithoutExtension(absPath);
        var rootGO = new GameObject(rootName);
        var inst = gltf.InstantiateMainSceneAsync(rootGO.transform);
        while (!inst.IsCompleted) yield return null;

        // 設 Layer、材質、物理
        SetLayerRecursively(rootGO, _placeableLayer);
        ApplyMaterialsRecursively(rootGO);
        AddPhysicsRecursively(rootGO);

        // ★★ 關鍵：用固定 Transform，或退回自動置中縮放 ★★
        if (useFixedTransform)
        {
            // 直接套用你事先設定好的值
            rootGO.transform.position = fixedPosition;
            rootGO.transform.eulerAngles = fixedRotation;
            rootGO.transform.localScale = fixedScale;
        }
        else
        {
            // 需要時才用自動置中/縮放
            if (autoCenterAndFit) CenterAndFit(rootGO);
        }

        // 載入成功 → 移除臨時地板
        if (_tempFloor != null) { Destroy(_tempFloor); _tempFloor = null; }

        Debug.Log("✅ GLB 已載入並套用材質/物理/Layer + 固定 Transform。");
        CreateSafetyFloorUnder(rootGO);

    }



    public void SetPickedPath(string path) => pickedPath = path;

    // ====== URL 工具（強制 HTTPS + 自動拼接） ======
    private static string BuildHttpsBase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        string s = input.Trim();
        // 去空白與尾斜線
        while (s.EndsWith("/")) s = s[..^1];

        // 若是 http:// 直接改 https://
        if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            s = "https://" + s.Substring("http://".Length);
        // 若沒有協定，補上 https://
        else if (!s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            s = "https://" + s;

        return s; // 形如 https://host 或 https://host:port
    }

    private static string JoinUrl(string baseUrl, string path)
    {
        if (string.IsNullOrEmpty(baseUrl)) return null;
        string b = baseUrl.TrimEnd('/');
        string p = string.IsNullOrEmpty(path) ? "" : path.Trim();
        if (!p.StartsWith("/")) p = "/" + p;
        return b + p;
    }

    private static string EnsureHttpsAbsolute(string baseHttps, string maybeUrl)
    {
        if (string.IsNullOrEmpty(maybeUrl)) return null;
        string m = maybeUrl.Trim();

        if (m.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            m = "https://" + m.Substring("http://".Length);

        if (m.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return m;

        // 相對路徑 → 拼到 base
        return JoinUrl(baseHttps, m);
    }
}
