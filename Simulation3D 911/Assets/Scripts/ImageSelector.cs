using UnityEngine;
using System.Collections;
using SimpleFileBrowser;

public class ImageSelector : MonoBehaviour
{
    public static string selectedImagePath;

    void Start()
    {
        // 場景裡已放好且啟用的 SimpleFileBrowserCanvas：
        // 先把它隱藏（但物件仍然啟用，確保會用到這個世界空間 Canvas）
        FileBrowser.HideDialog();
    }

    // 你的 UI 按鈕 (Select Image) 的 OnClick 指到這個方法
    public void OpenFileBrowser()
    {
        StartCoroutine(ShowLoadDialogCoroutine());
    }

    IEnumerator ShowLoadDialogCoroutine()
    {
        // 可選：只顯示圖片
        FileBrowser.SetFilters(true, ".png", ".jpg", ".jpeg");
        FileBrowser.SingleClickMode = true;

        // 這行會把剛才隱藏的對話框「顯示」在你放的世界空間 Canvas 上
        yield return FileBrowser.WaitForLoadDialog(
            pickMode: FileBrowser.PickMode.Files,
            allowMultiSelection: false,
            initialPath: null,
            initialFilename: null,
            title: "Select Image",
            loadButtonText: "Load"
        );

        if (!FileBrowser.Success) yield break;

        selectedImagePath = FileBrowser.Result[0];
        Debug.Log("✅ 選擇圖片路徑：" + selectedImagePath);

        //（在 Quest/Android 10+ 上，之後讀檔建議用 FileBrowserHelpers.ReadBytesFromFile）
    }
}