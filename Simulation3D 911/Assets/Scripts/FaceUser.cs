using UnityEngine;

public class FaceUser : MonoBehaviour
{
    public Transform user;
    public float turnSpeed = 8f;

    void Awake()
    {
        if (!user)
        {
            var cam = Camera.main;      // ¨Ì Tag = MainCamera ´M§ä
            if (cam) user = cam.transform;
        }
    }

    void LateUpdate()
    {
        if (!user) return;

        Vector3 dir = user.position - transform.position;
        dir.y = 0f;                           // Âê Y¡AÁ×§K©ïÀY§CÀY
        if (dir.sqrMagnitude < 0.0001f) return;

        var target = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.Slerp(transform.rotation, target, Time.deltaTime * turnSpeed);
    }
}
