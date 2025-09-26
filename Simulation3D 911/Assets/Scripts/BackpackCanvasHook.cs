using UnityEngine;

public class BackpackCanvasHook : MonoBehaviour
{
    public BackpackThumbnailPanel panel;
    void OnEnable() { if (panel) panel.RebuildButtonsNow(); }
}