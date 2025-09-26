// FileBrowserUtil.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SimpleFileBrowser;

public static class FileBrowserUtil
{
    /// <summary>
    /// �� SimpleFileBrowser �D�ϡA�^�ǵ�����| (�Ĥ@�ӿ�����ɮ�)�A�����^ null�C
    /// </summary>
    public static IEnumerator PickImagePath(System.Action<string> onPicked)
    {
        // �]�w�L�o���A�u���\����
        SimpleFileBrowser.FileBrowser.SetFilters(true,
            new SimpleFileBrowser.FileBrowser.Filter("Images", ".png", ".jpg", ".jpeg", ".webp"));
        SimpleFileBrowser.FileBrowser.SetDefaultFilter(".png");

        // ���}�ɮ׹�ܮ�
        yield return SimpleFileBrowser.FileBrowser.WaitForLoadDialog(
            SimpleFileBrowser.FileBrowser.PickMode.Files,
            allowMultiSelection: false,
            initialPath: null,
            initialFilename: null,
            title: "��ܹϤ�",
            loadButtonText: "���");

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