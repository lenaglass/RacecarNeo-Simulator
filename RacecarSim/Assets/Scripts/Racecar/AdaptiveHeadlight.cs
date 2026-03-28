using UnityEngine;

/// <summary>
/// Makes the headlight assembly follow steering and tilts down realistically.
/// Attach to the headlight parent object — children are the actual light sources.
/// Created and managed by TronMode — do not add manually.
/// </summary>
public class AdaptiveHeadlight : MonoBehaviour
{
    [HideInInspector] public float steerFollowAmount = 0.5f;
    [HideInInspector] public float steerSmoothing = 5f;
    [HideInInspector] public float downTilt = 15f;

    private Drive drive;
    private float currentYaw = 0f;

    void Start()
    {
        drive = GetComponentInParent<Drive>();
    }

    void LateUpdate()
    {
        if (drive == null) return;

        // Follow steering — smoothed
        float targetYaw = drive.Angle * 20f * steerFollowAmount;
        currentYaw = Mathf.Lerp(currentYaw, targetYaw, steerSmoothing * Time.deltaTime);

        // Apply: base pitch down + steering yaw
        transform.localRotation = Quaternion.Euler(downTilt, currentYaw, 0f);
    }
}