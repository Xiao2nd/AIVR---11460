using UnityEngine;
using System.IO;
using System.Threading.Tasks;
using GLTFast;

public class ScenesImporter : MonoBehaviour
{
    [Header("GLB 資料夾 (StreamingAssets 下)")]
    public string folderName = "DownloadedModels";

    [Header("自動套用的材質 (Vertex Color/一般用)")]
    [Tooltip("若你的 GLB 需要顯示頂點色，請指定支援 Vertex Color 的 URP 材質；若留空會退回 URP/Lit。")]
    public Material vertexColorMaterial;

    [Header("臨時地板使用的材質 (可選)")]
    [Tooltip("若留空，執行時會自動建立一個 URP/Lit 材質，避免在裝置上變粉紅。")]
    public Material baseFloorMaterial;

    [Header("Transform 設定 (GLB 產生節點)")]
    public Vector3 position = new Vector3(10f, 0f, 10f);
    public Vector3 rotation = Vector3.zero;   // 你原本會覆蓋成 (-90,0,0)；保留選項
    public Vector3 scale = Vector3.one;

    private GameObject baseFloor;
    private int placeableLayer;

    void Start()
    {
        // 取得 Placeable 圖層，如無則退回 Default
        placeableLayer = LayerMask.NameToLayer("Placeable");
        if (placeableLayer < 0)
        {
            Debug.LogWarning("⚠ 找不到圖層 \"Placeable\"，將改用 Default。");
            placeableLayer = LayerMask.NameToLayer("Default");
        }

        // 建立臨時地板（使用 URP 友善材質）
        CreateBaseFloor();
        // 準備安全的材質備援
        EnsureSafeFallbackMaterials();
    }

    /// <summary>
    /// UI Button 呼叫
    /// </summary>
    public void ImportGlbModelsFromButton()
    {
        string fullPath = Path.Combine(Application.streamingAssetsPath, folderName);
        Debug.Log("📂 嘗試載入資料夾：" + fullPath);

        if (!Directory.Exists(fullPath))
        {
            Debug.LogWarning("❌ 找不到資料夾：" + fullPath);
            return;
        }

        string[] glbFiles = Directory.GetFiles(fullPath, "*.glb");
        if (glbFiles.Length == 0)
        {
            Debug.Log("⚠ 沒有找到任何 .glb 檔案");
            return;
        }

        foreach (string glbPath in glbFiles)
        {
            Debug.Log("📦 找到 GLB 檔案：" + glbPath);
            _ = LoadAndApply(glbPath);
        }
    }

    #region Floor & Materials

    private void CreateBaseFloor()
    {
        baseFloor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        baseFloor.name = "TemporaryBaseFloor";
        baseFloor.transform.position = Vector3.zero;
        baseFloor.transform.localScale = new Vector3(10, 1, 10);

        // 指定圖層
        baseFloor.layer = placeableLayer;

        // 🔹 指定 URP 友善材質，避免在 URP/Quest 上變粉紅
        var renderer = baseFloor.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            if (baseFloorMaterial == null)
            {
                baseFloorMaterial = CreateSafeUrpLitMaterial(new Color(0.7f, 0.7f, 0.7f, 1f));
            }
            renderer.material = baseFloorMaterial;
        }

        // 碰撞與物理
        var collider = baseFloor.GetComponent<Collider>();
        if (collider != null)
        {
            collider.isTrigger = false;

            var physicsMaterial = new PhysicsMaterial
            {
                dynamicFriction = 0.6f,
                staticFriction = 0.6f,
                bounciness = 0,
                frictionCombine = PhysicsMaterialCombine.Multiply,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            collider.material = physicsMaterial;
        }

        var rb = baseFloor.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    /// <summary>
    /// UI Button 可呼叫：刪除臨時基礎地板
    /// </summary>
    public void RemoveBaseFloor()
    {
        if (baseFloor != null)
        {
            Destroy(baseFloor);
            baseFloor = null;
            Debug.Log("✅ 已移除臨時基礎地板");
        }
        else
        {
            Debug.Log("⚠ 沒有臨時基礎地板可移除");
        }
    }

    /// <summary>
    /// 確保 vertexColorMaterial 與 baseFloorMaterial 至少是 URP 可用材質
    /// </summary>
    private void EnsureSafeFallbackMaterials()
    {
        if (vertexColorMaterial == null)
        {
            // 沒有提供就給 URP/Lit，至少不會變粉紅
            vertexColorMaterial = CreateSafeUrpLitMaterial(Color.white);
        }
    }

    /// <summary>
    /// 盡量取得 URP/Lit，找不到則退到 URP/Unlit；再不行就用內建 Default（最後手段）
    /// </summary>
    private Material CreateSafeUrpLitMaterial(Color color)
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit != null)
        {
            var m = new Material(lit);
            // URP/Lit 的主色屬性名稱為 _BaseColor
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            return m;
        }

        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlit != null)
        {
            var m = new Material(unlit);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            return m;
        }

        Debug.LogWarning("⚠ 找不到 URP Shader（Lit/Unlit）。將使用 Default-Material 作為最後退路。建議到 Project Settings/Graphics 將 URP Shader 加到 Always Included Shaders。");
        return new Material(Shader.Find("Standard")); // 最後手段（桌面可用，行動裝置可能被剔除）
    }

    #endregion

    #region GLB Loading

    private async Task LoadAndApply(string path)
    {
        try
        {
            Debug.Log("🔄 開始載入：" + path);

            GltfImport gltf = new GltfImport();
            string uri = new System.Uri(path).AbsoluteUri;
            bool success = await gltf.Load(uri);

            if (!success)
            {
                Debug.LogError("❌ glTFast 載入失敗：" + path);
                return;
            }

            GameObject root = new GameObject(Path.GetFileNameWithoutExtension(path));
            bool instantiated = await gltf.InstantiateMainSceneAsync(root.transform);

            if (!instantiated)
            {
                Debug.LogError("❌ InstantiateMainSceneAsync() 失敗");
                return;
            }

            SetLayerRecursively(root, LayerMask.NameToLayer("Placeable"));

            root.transform.position = position;
            root.transform.eulerAngles = new Vector3(-90f, 0f, 0f);
            root.transform.localScale = scale;

            if (vertexColorMaterial != null)
            {
                MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
                foreach (var mr in renderers)
                {
                    mr.material = vertexColorMaterial;
                }
                Debug.Log($"✅ 已套用材質至 {renderers.Length} 個 MeshRenderer：{root.name}");
            }

            MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in meshFilters)
            {
                if (mf.gameObject.GetComponent<Collider>() == null)
                {
                    var meshCollider = mf.gameObject.AddComponent<MeshCollider>();
                    meshCollider.convex = false;
                    meshCollider.isTrigger = false;
                }
            }

            if (baseFloor != null)
            {
                Destroy(baseFloor);
                baseFloor = null;
                Debug.Log("✅ 已移除臨時基礎地板");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError("🔥 匯入流程發生錯誤：" + ex.Message + "\n" + ex.StackTrace);
        }
    }

    private void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    #endregion
}
