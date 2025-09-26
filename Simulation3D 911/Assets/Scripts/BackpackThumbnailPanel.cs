using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem; // Keyboard + InputActionProperty

// glTFast (runtime glTF/.glb importer)
using GLTFast;

public class BackpackThumbnailPanel : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject buttonPrefab;
    [SerializeField] private Transform buttonParent;
    [SerializeField] private GameObject backpackCanvas;

    [Header("Source Settings")]
    [Tooltip("Resources 下的資料夾名稱（讀 prefab 用）。")]
    [SerializeField] private string resourcesFolder = "BackpackModels";

    [Tooltip("Android/Quest：是否列出 persistentDataPath/BackpackModels 的 .glb（可直接放置，需 glTFast）。")]
    [SerializeField] private bool listPersistentGlbOnAndroid = false;

    [Header("Block Placement")]
    [Tooltip("射線起點（例如 Right/Left 手的 Interactor 或 Camera）。")]
    [SerializeField] private Transform rayOrigin;
    [Tooltip("可放置區域 LayerMask。")]
    [SerializeField] private LayerMask placeable;
    [Tooltip("最大射線距離。")]
    [SerializeField] private float maxDistance = 10f;
    [Tooltip("若有綁 XR 的 Trigger 動作，會優先使用；否則用鍵盤 G。")]
    [SerializeField] private InputActionProperty triggerAction;

    // 目前選中的項目（prefab 名稱；或 .glb 檔案路徑）
    public string CurrentSelectedNameOrPath { get; private set; } = string.Empty;

    private readonly List<string> _androidGlbPaths = new List<string>();
    public static string PersistentModelsDir => Path.Combine(Application.persistentDataPath, "BackpackModels");

    private void OnEnable()
    {
        PositionCanvasInFrontOfPlayer();
        StartCoroutine(RefreshButtons());

        if (triggerAction != null && triggerAction.action != null)
        {
            try { triggerAction.action.Enable(); } catch { }
        }
    }

    private void OnDisable()
    {
        if (triggerAction != null && triggerAction.action != null)
        {
            try { triggerAction.action.Disable(); } catch { }
        }
    }

    private void Update()
    {
        bool pressedByTrigger = false;
        bool pressedByG = false;

        if (triggerAction != null && triggerAction.action != null && triggerAction.action.WasPressedThisFrame())
        {
            pressedByTrigger = true;
            var act = triggerAction.action;
            if (act.activeControl != null)
            {
                var ctrl = act.activeControl;
                string dev = ctrl.device != null ? (ctrl.device.displayName ?? ctrl.device.name) : "";
                Debug.Log($"[Backpack] TriggerAction 被觸發 via {ctrl.displayName}{(string.IsNullOrEmpty(dev) ? "" : $" ({dev})")}");
            }
            else
            {
                Debug.Log("[Backpack] TriggerAction 被觸發");
            }
        }

        if (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame)
        {
            pressedByG = true;
            Debug.Log("[Backpack] G 鍵被觸發");
        }

        if (!(pressedByTrigger || pressedByG)) return;

        // 1) 先嘗試拿起（命中含 BackpackObject 的物件就直接移除）
        if (TryPickUpBlock()) return;

        // 2) 再嘗試放置（支援 Resources prefab 與 persistent .glb）
        if (string.IsNullOrEmpty(CurrentSelectedNameOrPath)) return;

        // Raycast for placement point first (兩種路徑共用)
        if (rayOrigin == null)
        {
            Debug.LogWarning("[Backpack] rayOrigin 未綁定，無法放置");
            return;
        }

        var ray = new Ray(rayOrigin.position, rayOrigin.forward);
        Debug.DrawRay(ray.origin, ray.direction * maxDistance, Color.green, 1f);

        if (!Physics.Raycast(ray, out var hit, maxDistance))
        {
            Debug.Log("[Backpack] 射線完全沒命中任何物件");
            return;
        }

        bool onPlaceable = ((1 << hit.collider.gameObject.layer) & placeable.value) != 0;
        Debug.Log($"[Backpack] Raycast 命中：{hit.collider.gameObject.name} (Layer={LayerMask.LayerToName(hit.collider.gameObject.layer)}), placeable={onPlaceable}");
        if (!onPlaceable)
        {
            Debug.Log("[Backpack] 命中物件不是 Placeable Layer");
            return;
        }

        Vector3 placePos = SnapToGrid(hit.point);

        // 分支：Prefab 或 GLB
        if (IsResourcesPrefabName(CurrentSelectedNameOrPath))
        {
            TryPlacePrefab(CurrentSelectedNameOrPath, placePos);
        }
        else if (Path.GetExtension(CurrentSelectedNameOrPath).ToLowerInvariant() == ".glb")
        {
            StartCoroutine(PlaceGlbCoroutine(CurrentSelectedNameOrPath, placePos));
        }
    }

    // ------------------------------------------------------------
    // UI 生成
    // ------------------------------------------------------------
    public void RebuildButtonsNow() => StartCoroutine(RefreshButtons());

    private IEnumerator RefreshButtons()
    {
        if (buttonParent == null)
        {
            Debug.LogError("[Backpack] Button Parent 未綁定");
            yield break;
        }

        for (int i = buttonParent.childCount - 1; i >= 0; i--)
            Destroy(buttonParent.GetChild(i).gameObject);

        if (Application.platform == RuntimePlatform.Android && listPersistentGlbOnAndroid)
        {
            EnsureDir(PersistentModelsDir);
            yield return StartCoroutine(BuildButtonsFromPersistentGlb());
        }
        else
        {
            yield return StartCoroutine(BuildButtonsFromResourcesPrefabs());
        }
    }

    private IEnumerator BuildButtonsFromResourcesPrefabs()
    {
        var prefabs = Resources.LoadAll<GameObject>(resourcesFolder);
        Debug.Log($"[Backpack] Load Resources/{resourcesFolder} => {prefabs?.Length ?? 0}");

        if (prefabs == null || prefabs.Length == 0)
        {
            Debug.LogWarning($"[Backpack] 找不到任何 Resources/{resourcesFolder} prefab");
            yield break;
        }

        foreach (var prefab in prefabs)
        {
            CreateButton(prefab.name, () =>
            {
                CurrentSelectedNameOrPath = prefab.name;
                Debug.Log($"[Backpack] Select prefab: {CurrentSelectedNameOrPath}");
                if (backpackCanvas) backpackCanvas.SetActive(false);
            });
            yield return null;
        }

        Debug.Log("[Backpack] Prefab 按鈕建立完成");
    }

    private IEnumerator BuildButtonsFromPersistentGlb()
    {
        _androidGlbPaths.Clear();
        var glbs = Directory.GetFiles(PersistentModelsDir, "*.glb");
        Debug.Log($"[Backpack] Found GLB: {glbs.Length} in {PersistentModelsDir}");

        if (glbs.Length == 0)
        {
            CreateButton("(No GLB in persistent folder)", () => { });
            yield break;
        }

        foreach (var glb in glbs)
        {
            _androidGlbPaths.Add(glb);
            string display = Path.GetFileNameWithoutExtension(glb);

            CreateButton(display, () =>
            {
                CurrentSelectedNameOrPath = glb; // 這裡會走 glb 分支
                Debug.Log($"[Backpack] Select GLB: {CurrentSelectedNameOrPath}");
                if (backpackCanvas) backpackCanvas.SetActive(false);
            });

            yield return null;
        }
    }

    private void CreateButton(string label, System.Action onClick)
    {
        if (buttonPrefab == null || buttonParent == null)
        {
            Debug.LogError("[Backpack] buttonPrefab 或 buttonParent 未綁定");
            return;
        }

        var go = Instantiate(buttonPrefab, buttonParent);
        go.name = "Btn_" + label;

        var txt = go.GetComponentInChildren<TMP_Text>();
        if (txt != null) txt.text = label;

        var btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
        btn.onClick.RemoveAllListeners();
        if (onClick != null) btn.onClick.AddListener(() => onClick.Invoke());
    }

    // ------------------------------------------------------------
    // 放置：Prefab
    // ------------------------------------------------------------
    private void TryPlacePrefab(string modelName, Vector3 placePos)
    {
        GameObject prefab = Resources.Load<GameObject>($"{resourcesFolder}/{modelName}");
        if (prefab == null)
        {
            Debug.LogWarning($"[Backpack] 無法載入 prefab：Resources/{resourcesFolder}/{modelName}");
            return;
        }

        var placed = Instantiate(prefab, placePos, Quaternion.identity);
        AfterPlaced(placed, modelName, placePos);
    }

    // ------------------------------------------------------------
    // 放置：glb（runtime glTF）
    // ------------------------------------------------------------
    private IEnumerator PlaceGlbCoroutine(string glbPath, Vector3 placePos)
    {
        if (!File.Exists(glbPath))
        {
            Debug.LogWarning($"[Backpack] GLB 檔案不存在：{glbPath}");
            yield break;
        }

        Debug.Log($"[Backpack] 讀取 GLB：{glbPath}");

        var import = new GltfImport();

        // 1) 載入檔案
        Task<bool> loadTask = import.Load(glbPath);
        yield return new WaitUntil(() => loadTask.IsCompleted);

        if (!loadTask.Result)
        {
            Debug.LogWarning($"[Backpack] GLB 載入失敗：{glbPath}");
            yield break;
        }

        // 2) 建立根物件
        var root = new GameObject(Path.GetFileNameWithoutExtension(glbPath));
        root.transform.position = placePos;

        // 3) 實例化場景
        Task<bool> instTask = import.InstantiateMainSceneAsync(root.transform);
        yield return new WaitUntil(() => instTask.IsCompleted);

        if (!instTask.Result)
        {
            Debug.LogWarning($"[Backpack] GLB 實例化失敗：{glbPath}");
            Destroy(root);
            yield break;
        }

        AfterPlaced(root, Path.GetFileNameWithoutExtension(glbPath), placePos);
    }

    // 共用：放置後處理（加標記與 Collider）
    private void AfterPlaced(GameObject go, string nameForLog, Vector3 pos)
    {
        go.AddComponent<BackpackObject>();

        // 計算模型高度
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers)
                bounds.Encapsulate(r.bounds);

            float height = bounds.size.y;
            go.transform.position = pos + new Vector3(0, height / 2f, 0);
        }
        else
        {
            go.transform.position = pos;
        }

        // 自動補 Collider
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            var sub = mf.gameObject;
            if (!sub.GetComponent<Collider>())
            {
                var col = sub.AddComponent<MeshCollider>();
                col.convex = true;
                col.isTrigger = true;
            }
        }

        Debug.Log($"[Backpack] 放置完成：{nameForLog} @ {go.transform.position}");
    }


    // ------------------------------------------------------------
    // 拿取（移除）
    // ------------------------------------------------------------
    private bool TryPickUpBlock()
    {
        if (rayOrigin == null) return false;

        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
        if (Physics.Raycast(ray, out var hit, maxDistance))
        {
            var marker = hit.collider.GetComponentInParent<BackpackObject>();
            if (marker != null)
            {
                Debug.Log("[Backpack] 拿起並移除：" + marker.gameObject.name);
                Destroy(marker.gameObject);
                return true;
            }
        }
        return false;
    }

    // ------------------------------------------------------------
    // 其它
    // ------------------------------------------------------------
    private void PositionCanvasInFrontOfPlayer()
    {
        if (!backpackCanvas) return;
        var cam = Camera.main;
        if (!cam) return;

        Vector3 fwd = cam.transform.forward;
        fwd.y = 0f;
        fwd.Normalize();

        backpackCanvas.transform.position = cam.transform.position + fwd * 2f + Vector3.up * 0.2f;
        backpackCanvas.transform.rotation = Quaternion.LookRotation(fwd);
    }

    private static void EnsureDir(string dir)
    {
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
    }

    private static Vector3 SnapToGrid(Vector3 pos)
    {
        return new Vector3(
            Mathf.RoundToInt(pos.x),
            Mathf.RoundToInt(pos.y),
            Mathf.RoundToInt(pos.z)
        );
    }

    private bool IsResourcesPrefabName(string s)
    {
        // 沒有副檔名 => 視為 prefab 名稱；帶副檔名（.glb 等）則視為檔案路徑
        return string.IsNullOrEmpty(Path.GetExtension(s));
    }
}
