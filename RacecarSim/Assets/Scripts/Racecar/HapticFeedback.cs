using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;

public class HapticFeedback : MonoBehaviour
{
    [Header("Speed Rumble")]
    [SerializeField] private float minSpeedForRumble = 10f;
    [SerializeField] private float maxSpeedForRumble = 40f;
    [SerializeField] private float maxHighFreqRumble = 0.5f;

    [Header("Collision")]
    [SerializeField] private float collisionMinImpactIntensity = 15f;
    [SerializeField] private float collisionRumbleIntensity = 0.6f;
    [SerializeField] private float collisionRumbleDuration = 0.25f;

    [Header("Update Rate")]
    [SerializeField] private float hapticUpdateInterval = 0.05f; // 20Hz

    private Rigidbody rBody;
    private float collisionTimer = 0f;
    private float collisionMagnitude = 0f;
    private float hapticTimer = 0f;
    private float currentLowFreq = 0f;
    private float currentHighFreq = 0f;
    private float smoothing = 0.15f; // lower = smoother but more latent
    private float collisionHighMagnitude = 0f;

    void Start()
    {
        rBody = GetComponent<Rigidbody>();
    }

    void Update()
    {
        if (Gamepad.current == null) return;

        float speed = rBody.velocity.magnitude;

        // Collision transient (immediate, bypasses throttle)
        if (collisionTimer > 0f)
        {
            collisionTimer -= Time.deltaTime;
            float decay = collisionTimer / collisionRumbleDuration;

            // Low freq decays linearly, high freq cuts off faster
            float low = collisionMagnitude * decay;
            float high = collisionHighMagnitude * Mathf.Pow(decay, 3f);  // rapid cutoff

            Gamepad.current.SetMotorSpeeds(low, high);
            currentLowFreq = low;
            currentHighFreq = 0f;
            return;
        }

        // Calculate target rumble
        float targetLow = 0f;
        float targetHigh = 0f;
        float t = Mathf.Clamp01((speed - minSpeedForRumble) / (maxSpeedForRumble - minSpeedForRumble));
        targetHigh = Mathf.Lerp(0f, maxHighFreqRumble, t);

        // Smooth towards target
        currentLowFreq = Mathf.Lerp(currentLowFreq, targetLow, smoothing);
        currentHighFreq = Mathf.Lerp(currentHighFreq, targetHigh, smoothing);

        // Throttled send
        hapticTimer += Time.deltaTime;
        if (hapticTimer >= hapticUpdateInterval)
        {
            hapticTimer = 0f;

            // Cut tiny values to avoid constant low-level buzz
            float sendLow = currentLowFreq < 0.02f ? 0f : currentLowFreq;
            float sendHigh = currentHighFreq < 0.02f ? 0f : currentHighFreq;

            Gamepad.current.SetMotorSpeeds(sendLow, sendHigh);

            if (Gamepad.current is DualShockGamepad ds)
            {
                Color barColor = Color.Lerp(Color.blue, Color.red + Color.blue, speed / 40f);
                ds.SetLightBarColor(barColor);
            }
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (Gamepad.current == null) return;

        float impact = collision.relativeVelocity.magnitude;
        Debug.Log($"[Haptics] Collision impact: {impact:F2}");

        if (impact < collisionMinImpactIntensity) return;

        // Scale for 10x physics: gentle tap ~2-5, moderate ~10-20, hard ~30+
        // Use exponential curve so soft hits feel distinct from hard ones
        float normalized = Mathf.Clamp01(impact - collisionMinImpactIntensity / 40f);
        float shaped = Mathf.Pow(normalized, 0.5f);  // sqrt curve — spreads out the low end

        // Low freq for thump, high freq for sharp buzz
        float lowIntensity = shaped * collisionRumbleIntensity;
        float highIntensity = shaped > 0.5f ? shaped * 0.8f : 0f;  // high freq only on hard hits

        collisionMagnitude = lowIntensity;
        collisionHighMagnitude = highIntensity;
        collisionTimer = collisionRumbleDuration;

        // Immediate hit — bypasses throttle
        Gamepad.current.SetMotorSpeeds(lowIntensity, highIntensity);
    }

    void OnDisable()
    {
        // Stop rumble when script is disabled or scene stops
        if (Gamepad.current != null)
            Gamepad.current.ResetHaptics();
    }

    void OnApplicationQuit()
    {
        if (Gamepad.current != null)
            Gamepad.current.ResetHaptics();
    }
}