using UnityEngine;
using UnityEngine.InputSystem;

public class BlockPlacer : MonoBehaviour
{
    public Transform rayOrigin;
    public LayerMask placeableSurface;
    public float maxDistance = 10f;
    public InputActionProperty triggerAction; // XR Trigger input

    void Update()
    {
        if (triggerAction.action.WasPressedThisFrame())
        {
            // 先看前方有沒有可拿起的方塊
            if (!TryPickUpBlock())
            {
                // 如果沒拿到，就嘗試放置新方塊
                TryPlaceBlock();
            }
        }
    }

    void TryPlaceBlock()
    {
        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
        Debug.DrawRay(ray.origin, ray.direction * maxDistance, Color.red, 1f);

        if (Physics.Raycast(ray, out var hit, maxDistance, placeableSurface))
        {
            GameObject prefab = Resources.Load<GameObject>("BuildingBlock");
            if (prefab != null)
            {
                Vector3 placePos = SnapToGrid(hit.point);
                Instantiate(prefab, placePos, Quaternion.identity);
            }
            else
            {
                Debug.LogWarning("❌ 找不到 BuildingBlock prefab");
            }
        }
        else
        {
            Debug.LogWarning("⚠ 沒有 Hit 到任何地面");
        }
    }

    bool TryPickUpBlock()
    {
        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
        if (Physics.Raycast(ray, out var hit, maxDistance))
        {
            GameObject target = hit.collider.gameObject;

            if (target.CompareTag("PlaceableBlock"))
            {
                Destroy(target);
                Debug.Log("🧲 拿起了：" + target.name);
                return true;
            }
        }

        return false;
    }

    Vector3 SnapToGrid(Vector3 pos)
    {
        return new Vector3(
            Mathf.Round(pos.x),
            Mathf.Round(pos.y),
            Mathf.Round(pos.z)
        );
    }
}
