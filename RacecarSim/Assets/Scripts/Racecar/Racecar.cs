using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Encapsulates a RACECAR-MN.
/// </summary>
public class Racecar : MonoBehaviour
{
    #region Set in Unity Editor
    /// <summary>
    /// The cameras through which the user can observe the car.
    /// </summary>
    [SerializeField]
    private Camera[] playerCameras;

    /// <summary>
    /// The front half of the car's chassis.
    /// </summary>
    [SerializeField]
    private GameObject chassisFront;

    /// <summary>
    /// The rear half of the car's chassis.
    /// </summary>
    [SerializeField]
    private GameObject chassisBack;
    #endregion

    #region Constants
    /// <summary>
    /// The distance from which each player camera follows the car.
    /// </summary>
    private static readonly Vector3[] cameraOffsets =
    {
        new Vector3(0, 4, -8),
        new Vector3(0, 20, -2),
        new Vector3(0, 4, 8)
    };

    /// <summary>
    /// HUD elements to toggle hide/show on button press (start hidden)
    /// </summary>
    private string[] hideHudElements = { "PhysicsModule", "Inputs" };


    /// <summary>
    /// The speed at which the camera follows the car.
    /// </summary>
    private const float cameraSpeed = 6;
    #endregion

    #region Public Interface
    /// <summary>
    /// The index of the racecar.
    /// </summary>
    public int Index { get; private set; }

    /// <summary>
    /// Exposes the RealSense D435i color and depth channels.
    /// </summary>
    public CameraModule Camera { get; private set; }

    /// <summary>
    /// Exposes the car motors.
    /// </summary>
    public Drive Drive { get; private set; }

    /// <summary>
    /// Exposes the YDLIDAR X4 sensor.
    /// </summary>
    public Lidar Lidar { get; private set; }

    /// <summary>
    /// Exposes the RealSense D435i IMU.
    /// </summary>
    public PhysicsModule Physics { get; private set; }

    /// <summary>
    /// The heads-up display controlled by this car, if any.
    /// </summary>
    public Hud Hud { get; set; }

    /// <summary>
    /// The center point of the car.
    /// </summary>
    public Vector3 Center
    {
        get
        {
            return this.transform.position + this.transform.up * 0.8f;
        }
    }

    /// <summary>
    /// Called on the first frame when the car enters default drive mode.
    /// </summary>
    public void DefaultDriveStart()
    {
        this.Drive.MaxSpeed = Drive.DefaultMaxSpeed;
        this.Drive.Stop();
    }

    /// <summary>
    /// Tracks whether the triggers were touched (workaround for unity/PS5 limitation)
    /// </summary>
    private bool[] triggerTouched = { false, false };

    /// <summary>
    /// Called each frame that the car is in default drive mode.
    /// </summary>
    public void DefaultDriveUpdate()
    {
        // Accelerator and brake (PS values, plus 'wrong' special value of zero from unity on initialization)
        float rawAccelerator = Controller.GetTrigger(Controller.Trigger.RIGHT);
        float rawBrake = Controller.GetTrigger(Controller.Trigger.LEFT);

        // The first real value (usually around -1f) sets relevant triggerTouched to true
        triggerTouched[0] = (triggerTouched[0] == true) || (rawAccelerator != 0f);
        triggerTouched[1] = (triggerTouched[1] == true) || (rawBrake != 0f);

        // PS5 values from -1 (fully released) to +1 (fully depressed) scaled to 0..1
        float scaledAccelerator = triggerTouched[0] ? (rawAccelerator + 1) / 2 : 0;
        float scaledBrake = triggerTouched[1] ? (rawBrake + 1) / 2 : 0;

        // Apply deadzone to avoid creep
        scaledAccelerator = scaledAccelerator < 0.05f ? 0f : scaledAccelerator;
        scaledBrake = scaledBrake < 0.05f ? 0f : scaledBrake;

        this.Drive.Speed = ApplyExponentialCurve(scaledAccelerator, 2.0f) - ApplyExponentialCurve(scaledBrake, 2.0f);

        // Adaptive stearing (limited range at high speed to reduce spinouts)
        float rawAngle = ApplyDeadzone(Controller.GetJoystick(Controller.Joystick.LEFT).x);
        float shapedAngle = ApplyExponentialCurve(rawAngle, 2.5f);
        float currentSpeedMps = this.GetComponent<Rigidbody>().velocity.magnitude;
        this.Drive.Angle = shapedAngle * GetSteeringScalar(currentSpeedMps);

        if (Controller.WasPressed(Controller.Button.A))
        {
            print("Kachow!");
            // TODO: Add tron mode lighting switch
        }

        if (Controller.WasPressed(Controller.Button.RJOY))
        {
            NextCamera();   // cycle through camera views
        }

        if (Controller.WasPressed(Controller.Button.Y))
        {
            foreach (string hidElement in hideHudElements) ToggleHudElement(hidElement);
        }

        if (Controller.WasPressed(Controller.Button.X))
        {
            TronMode tron = GetComponent<TronMode>();
            if (tron != null) tron.Toggle();
        }

        // Use the bumpers to adjust max speed
        if (Controller.WasPressed(Controller.Button.RB))
        {
            this.Drive.MaxSpeed = Mathf.Min(this.Drive.MaxSpeed + 0.1f, 1);
        }
        if (Controller.WasPressed(Controller.Button.LB))
        {
            this.Drive.MaxSpeed = Mathf.Max(this.Drive.MaxSpeed - 0.1f, 0);
        }
    }

    /// <summary>
    /// Apply deadzone to stick to prevent car crawling due to joystick noise
    /// </summary>
    float ApplyDeadzone(float value, float threshold = 0.05f)
    {
        if (Mathf.Abs(value) < threshold)
            return 0f;
        return Mathf.Sign(value) * (Mathf.Abs(value) - threshold) / (1f - threshold);
    }

    /// <summary>
    /// Power law curve for more natural throttle and stearing control
    /// </summary>
    float ApplyExponentialCurve(float value, float exponent)
    {
        return Mathf.Sign(value) * Mathf.Pow(Mathf.Abs(value), exponent);
    }

    /// <summary>
    /// Speed adaptive steering (to help avoid skidding out at high speeds)
    /// </summary>
    float GetSteeringScalar(float speedMps, float minSpeed = 0.5f, float maxSpeed = 40f, float minScalar = 0.2f)
    {
        if (Mathf.Abs(speedMps) < minSpeed)
            return 1f;
        float t = Mathf.Clamp01((Mathf.Abs(speedMps) - minSpeed) / (maxSpeed - minSpeed));
        return Mathf.Lerp(1f, minScalar, t);
    }

    /// <summary>
    /// Toggle given hud element visibility
    /// </summary>
    private void ToggleHudElement(string elementName)
    {
        if (this.Hud == null) return;

        Transform element = this.Hud.transform.Find(elementName);
        if (element != null)
        {
            element.gameObject.SetActive(!element.gameObject.activeSelf);
        }
    }

    /// <summary>
    /// Sets the render texture and audio listener of the player perspective (3rd person) cameras.
    /// </summary>
    /// <param name="texture">The render texture to which to assign the cameras.</param>
    /// <param name="enableAudio">True if the audio listeners of the cameras should be enabled.</param>
    public void SetPlayerCameraFeatures(RenderTexture texture, bool enableAudio)
    {
        foreach (Camera camera in this.playerCameras)
        {
            camera.targetTexture = texture;
            camera.GetComponent<AudioListener>().enabled = enableAudio;
        }
    }

    /// <summary>
    /// Sets the index of the car.
    /// </summary>
    /// <param name="index">The index of the car in the race.</param>
    public void SetIndex(int index)
    {
        this.Index = index;

        // Set car color and customization based on saved data
        CarCustomization customization = SavedDataManager.Data.CarCustomizations[index];

        Material frontMaterial = this.chassisFront.GetComponent<Renderer>().material;
        frontMaterial.color = customization.FrontColor.Color;
        frontMaterial.SetFloat("_Metallic", customization.IsFrontShiny ? 1 : 0);

        Material backMaterial = this.chassisBack.GetComponent<Renderer>().material;
        backMaterial.color = customization.BackColor.Color;
        backMaterial.SetFloat("_Metallic", customization.IsBackShiny ? 1 : 0);
    }

    /// <summary>
    /// Sets the player camera which shows the user's view of the car.
    /// </summary>
    /// <param name="cameraIndex">The index of the player camera to use.</param>
    public void SetCamera(int cameraIndex)
    {
        this.playerCameras[this.curCamera].enabled = false;
        this.curCamera = cameraIndex;
        this.playerCameras[this.curCamera].enabled = true;
    }

    public void NextCamera()
    {
        SetCamera((this.curCamera + 1) % this.playerCameras.Length);
    }

    #endregion

    /// <summary>
    /// The index in PlayerCameras of the current active camera.
    /// </summary>
    private int curCamera;

    private void Awake()
    {
        this.curCamera = 0;

        // Find submodules
        this.Camera = this.GetComponent<CameraModule>();
        this.Drive = this.GetComponent<Drive>();
        this.Lidar = this.GetComponentInChildren<Lidar>();
        this.Physics = this.GetComponent<PhysicsModule>();

        // Begin with main player camera (0th camera)
        if (this.playerCameras.Length > 0)
        {
            this.playerCameras[0].enabled = true;
            for (int i = 1; i < this.playerCameras.Length; i++)
            {
                this.playerCameras[i].enabled = false;
            }
        }
    }

    private void Update()
    {
        // Toggle camera when the space bar is pressed
        if (Input.GetKeyDown(KeyCode.Space))
        {
            NextCamera();
        }
    }

    /// <summary>
    /// Camera look-around state
    /// </summary>
    private static readonly float[] cameraYawSign = { 1f, 1f, -1f };
    private float cameraYaw = 0f;
    private float cameraPitch = 0f;
    private const float cameraMaxPitch = 20f;
    private const float cameraLookSpeed = 120f;
    private const float cameraReturnSpeed = 3f;
    private const float cameraMaxYaw = 120f;
    private const float cameraLookDeadzone = 0.3f;
    private static readonly float[] cameraBasePitch = { -10f, 0f, -10f };

    /// <summary>
    /// Camera shake state
    /// </summary>
    private Vector3 cameraShakeOffset = Vector3.zero;
    private float cameraShakeTimer = 0f;
    private float cameraShakeIntensity = 0f;
    private Vector3 cameraShakeDirection = Vector3.zero;
    private const float cameraShakeDuration = 1f;
    private const float cameraShakeMinImpactIntensity = 20f;
    private const float cameraShakeMaxIntensity = 1f;
    private const float cameraShakeFrequency = 10f;

    /// <summary>
    /// Triggered by collision — sets up directional camera shake
    /// </summary>
    private void OnCollisionEnter(Collision collision)
    {
        float impact = collision.relativeVelocity.magnitude;

        // Minimum threshold to avoid shake on gentle touches
        if (impact < cameraShakeMinImpactIntensity) return;

        // Normalize impact to 0-1 range (adjusted for 10x physics scale)
        float normalized = Mathf.Clamp01(impact / 40f);

        // Sqrt curve — spreads out the low end so light and hard hits feel different
        cameraShakeIntensity = Mathf.Pow(normalized, 0.5f) * cameraShakeMaxIntensity;
        cameraShakeTimer = cameraShakeDuration;

        // Get impact direction in local space — camera kicks opposite to hit
        Vector3 worldImpactDir = collision.relativeVelocity.normalized;
        Vector3 localImpactDir = this.transform.InverseTransformDirection(worldImpactDir);

        // Flatten to XY for camera offset (no Z — Z is forward/back which feels weird)
        cameraShakeDirection = new Vector3(localImpactDir.x, Mathf.Abs(localImpactDir.y) * 0.5f, 0f).normalized;
    }

    /// <summary>
    /// Handles camera view adjustements based on trackpad input and collisions
    /// </summary>
    private void LateUpdate()
    {
        Vector2 lookInput = Controller.GetJoystick(Controller.Joystick.RIGHT);

        // Consistent deadzone for both axes
        if (Mathf.Abs(lookInput.x) < cameraLookDeadzone)
            lookInput.x = 0f;
        else
            lookInput.x = Mathf.Sign(lookInput.x) * (Mathf.Abs(lookInput.x) - cameraLookDeadzone) / (1f - cameraLookDeadzone);

        if (Mathf.Abs(lookInput.y) < cameraLookDeadzone)
            lookInput.y = 0f;
        else
            lookInput.y = Mathf.Sign(lookInput.y) * (Mathf.Abs(lookInput.y) - cameraLookDeadzone) / (1f - cameraLookDeadzone);

        // Yaw
        if (Mathf.Abs(lookInput.x) > 0.01f)
        {
            float targetYaw = lookInput.x * cameraMaxYaw;
            cameraYaw = Mathf.MoveTowards(cameraYaw, targetYaw, cameraLookSpeed * Time.deltaTime);
        }
        else
        {
            cameraYaw = Mathf.Lerp(cameraYaw, 0f, cameraReturnSpeed * Time.deltaTime);
            if (Mathf.Abs(cameraYaw) < 0.5f) cameraYaw = 0f;
        }

        // Pitch — inverted Y axis, both up and down
        float pitchInput = -lookInput.y;
        float pitchTarget = -pitchInput * cameraMaxPitch;
        if (Mathf.Abs(pitchInput) > 0.01f)
        {
            cameraPitch = Mathf.Lerp(cameraPitch, pitchTarget, 2f * Time.deltaTime);
        }
        else
        {
            cameraPitch = Mathf.Lerp(cameraPitch, 0f, 1.5f * Time.deltaTime);
            if (Mathf.Abs(cameraPitch) < 0.3f) cameraPitch = 0f;
        }
        cameraPitch = Mathf.Clamp(cameraPitch, -cameraMaxPitch, cameraMaxPitch);

        // Camera shake decay
        if (cameraShakeTimer > 0f)
        {
            cameraShakeTimer -= Time.deltaTime;
            float decay = cameraShakeTimer / cameraShakeDuration;
            float dampedIntensity = cameraShakeIntensity * decay * decay;

            float noiseX = (Mathf.PerlinNoise(Time.time * cameraShakeFrequency, 0f) - 0.5f) * 2f;
            float noiseY = (Mathf.PerlinNoise(0f, Time.time * cameraShakeFrequency) - 0.5f) * 2f;

            float dirBlend = Mathf.Clamp01(decay * 2f);
            Vector3 randomShake = new Vector3(noiseX, noiseY, 0f);
            cameraShakeOffset = Vector3.Lerp(randomShake, cameraShakeDirection, dirBlend) * dampedIntensity;
        }
        else
        {
            cameraShakeOffset = Vector3.zero;
        }

        // Apply everything to each camera in a single pass
        for (int i = 0; i < this.playerCameras.Length; i++)
        {
            // Orbit position from yaw
            Quaternion yawRotation = Quaternion.Euler(0f, cameraYaw * cameraYawSign[i], 0f);
            Vector3 baseOffset = new Vector3(0f, Racecar.cameraOffsets[i].y, Racecar.cameraOffsets[i].z);
            Vector3 rotatedOffset = yawRotation * (this.transform.forward * baseOffset.z);
            Vector3 targetPosition = this.transform.position + new Vector3(rotatedOffset.x, baseOffset.y, rotatedOffset.z);

            // Add shake in camera-local space
            targetPosition += this.playerCameras[i].transform.right * cameraShakeOffset.x
                            + this.playerCameras[i].transform.up * cameraShakeOffset.y;

            // Smooth follow
            this.playerCameras[i].transform.position = Vector3.Lerp(
                this.playerCameras[i].transform.position,
                targetPosition,
                Racecar.cameraSpeed * Time.deltaTime);

            // Aim at car, then apply base pitch + look pitch + shake rotation
            this.playerCameras[i].transform.LookAt(this.transform.position);
            this.playerCameras[i].transform.Rotate(
                cameraBasePitch[i] + cameraPitch + cameraShakeOffset.y * 5f,  // vertical shake adds slight pitch wobble
                cameraShakeOffset.x * 5f,  // horizontal shake adds slight yaw wobble
                0f,
                Space.Self);
        }
    }

}