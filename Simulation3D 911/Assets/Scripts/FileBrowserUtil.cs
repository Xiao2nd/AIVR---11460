// FileBrowserUtil.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SimpleFileBrowser;

public static class FileBrowserUtil
{
    /// <summary>
    /// 用 SimpleFileBrowser 挑圖，回傳絕對路徑 (第一個選取的檔案)，取消回 null。
    /// </summary>
    public static IEnumerator PickImagePath(System.Action<string> onPicked)
    {
        // 設定過濾器，只允許圖檔
        SimpleFileBrowser.FileBrowser.SetFilters(true,
            new SimpleFileBrowser.FileBrowser.Filter("Images", ".png", ".jpg", ".jpeg", ".webp"));
        SimpleFileBrowser.FileBrowser.SetDefaultFilter(".png");

        // 打開檔案對話框
        yield return SimpleFileBrowser.FileBrowser.WaitForLoadDialog(
            SimpleFileBrowser.FileBrowser.PickMode.Files,
            allowMultiSelection: false,
            initialPath: null,
            initialFilename: null,
            title: "選擇圖片",
            loadButtonText: "選擇");

        if (SimpleFileBrowser.FileBrowser.Success &&
            SimpleFileBrowser.FileBrowser.Result != null &&
            SimpleFileBrowser.FileBrowser.Result.Length > 0)
        {
            onPicked?.Invoke(SimpleFileBrowser.FileBrowser.Result[0]);
        }
        else
        {
            onPicked?.Invoke(null);
        }
    }
}