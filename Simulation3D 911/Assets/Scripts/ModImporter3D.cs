using UnityEngine;
using System.IO;
using System.Threading.Tasks;
using GLTFast;

public class ModImporter3D : MonoBehaviour
{
    [Header("GLB Folder Name (under StreamingAssets)")]
    public string folderName = "DownloadedModels";

    [Header("Transform Settings")]
    public Vector3 position = new Vector3(-1.3f, 0f, -1.6f);
    public Vector3 rotation = new Vector3(0f, 0f, 90f);
    public Vector3 scale = Vector3.one;

    /// <summary>
    /// For UI Button to call (must be public void)
    /// </summary>
    public void ImportGlbModelsFromButton()
    {
        ImportGlbModels(); // Call main loader
    }

    /// <summary>
    /// Main GLB import method
    /// </summary>
    public void ImportGlbModels()
    {
        string fullPath = Path.Combine(Application.streamingAssetsPath, folderName);

        if (!Directory.Exists(fullPath))
        {
            Debug.LogWarning("GLB folder not found: " + fullPath);
            return;
        }

        string[] glbFiles = Directory.GetFiles(fullPath, "*.glb");

        foreach (string glbPath in glbFiles)
        {
            _ = LoadGlbAsync(glbPath);
        }
    }

    /// <summary>
    /// Asynchronously loads and instantiates a GLB model using glTFast
    /// </summary>
    private async Task LoadGlbAsync(string path)
    {
        GltfImport gltf = new GltfImport();
        bool success = await gltf.Load(path);

        if (success)
        {
            GameObject modelRoot = new GameObject(Path.GetFileNameWithoutExtension(path));
            gltf.InstantiateMainScene(modelRoot.transform);

            modelRoot.transform.position = position;
            modelRoot.transform.eulerAngles = rotation;
            modelRoot.transform.localScale = scale;

            Debug.Log("✅ Loaded: " + path);
        }
        else
        {
            Debug.LogError("❌ Failed to load: " + path);
        }
    }
}
