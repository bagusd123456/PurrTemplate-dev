using NyxMachina.Multiplayer;
using PurrNet;
using PurrNet.Prediction;
using System;
using UnityEngine;
using UnityEngine.UI;

public class PrometeoCarController : PredictedIdentity<CarInputData, CarInputHandler.State>
{
    [Header("Camera Setup")]
    public Transform cameraTarget;
    public PurrNetVoiceChatHandler voiceReceiverPrefab;
    public PurrNetVoiceChatHandler voiceReceiver { get; private set; }

    [SerializeField]
    private VoiceReceiverInterfaceListener voiceInterface;

    [Space(20)]
    [Range(20, 190)] public int maxSpeed = 90;
    [Range(10, 120)] public int maxReverseSpeed = 45;
    [Range(1, 10)] public int accelerationMultiplier = 2;
    [Space(10)]
    [Range(10, 45)] public int maxSteeringAngle = 27;
    [Range(0.1f, 1f)] public float steeringSpeed = 0.5f;
    [Space(10)]
    [Range(100, 600)] public int brakeForce = 350;
    [Range(1, 10)] public int decelerationMultiplier = 2;
    [Range(1, 10)] public int handbrakeDriftMultiplier = 5;
    [Space(10)]
    public Vector3 bodyMassCenter;

    // WHEELS
    public GameObject frontLeftMesh; public WheelCollider frontLeftCollider;
    public GameObject frontRightMesh; public WheelCollider frontRightCollider;
    public GameObject rearLeftMesh; public WheelCollider rearLeftCollider;
    public GameObject rearRightMesh; public WheelCollider rearRightCollider;

    // EFFECTS, UI, AUDIO
    public bool useEffects = false;
    public ParticleSystem RLWParticleSystem;
    public ParticleSystem RRWParticleSystem;
    public TrailRenderer RLWTireSkid;
    public TrailRenderer RRWTireSkid;
    public bool useUI = false;
    public Text carSpeedText;
    public bool useSounds = false;
    public AudioSource carEngineSound;
    public AudioSource tireScreechSound;
    float initialCarEngineSoundPitch;

    // INTERNAL VARS
    Rigidbody carRigidbody;
    float localVelocityZ;
    float localVelocityX;
    float carSpeed;

    // FRICTION
    WheelFrictionCurve FLwheelFriction, FRwheelFriction, RLwheelFriction, RRwheelFriction;
    float FLWextremumSlip, FRWextremumSlip, RLWextremumSlip, RRWextremumSlip;

    public Vector3 offset;

    protected void Awake()
    {
        carRigidbody = GetComponent<Rigidbody>();
        carRigidbody.centerOfMass = bodyMassCenter;

        SetupFriction(frontLeftCollider, ref FLwheelFriction, ref FLWextremumSlip);
        SetupFriction(frontRightCollider, ref FRwheelFriction, ref FRWextremumSlip);
        SetupFriction(rearLeftCollider, ref RLwheelFriction, ref RLWextremumSlip);
        SetupFriction(rearRightCollider, ref RRwheelFriction, ref RRWextremumSlip);

        if (carEngineSound != null) initialCarEngineSoundPitch = carEngineSound.pitch;
    }

    protected override void LateAwake()
    {
        ulong clientId = 0;
        string ownerName = "NULL";
        if (owner.HasValue)
        {
            clientId = owner.Value.id.value;
            var lobbyDate = LobbyHandlerUtil.GetPlayerDataByClientId(clientId);
            if (lobbyDate != null)
            {
                ownerName = lobbyDate.Username;
            }
        }

        if (!voiceReceiver)
        {
            voiceReceiver = Instantiate(voiceReceiverPrefab);
        }

        voiceReceiver.name = $"{voiceReceiverPrefab.name}-{clientId}";

        if (isOwner)
        {
            if (cameraTarget == null)
            {
                // If user forgot to assign it, create a temporary one to avoid crashes
                GameObject tempTarget = new GameObject("CameraTarget");
                tempTarget.transform.localPosition = new Vector3(0, 1f, 0); // Slight offset up
                tempTarget.transform.localRotation = Quaternion.identity;
                cameraTarget = tempTarget.transform;
            }

            // Assign the TARGET to the camera, not the car root
            if (CameraFollow.Instance != null)
            {
                CameraFollow.Instance.SetTarget(cameraTarget);
            }

            // Setup UI
            if (SpeedTextListener.Instance != null)
            {
                carSpeedText = SpeedTextListener.Instance.GetTextAsset();
                useUI = carSpeedText != null;
            }

            voiceInterface.Init(clientId, ownerName);

            if (voiceReceiver is NetworkIdentity identity)
            {
                identity.GiveOwnership(owner);
                Debug.Log($"Ownership transferred to: {owner.Value.id.value}");
                // ReSharper disable once Unity.InstantiateWithoutParent
                voiceReceiver.transform.SetParent(transform);
                voiceReceiver.transform.localPosition = new Vector3(0, 1, 0);
            }

            if (!gameObject.TryGetComponent(out AudioListener audioListener))
            {
                gameObject.AddComponent<AudioListener>();
            }
        }
        else if (isServer)
        {
            voiceInterface.Init(clientId, ownerName);

            if (voiceReceiver is NetworkIdentity identity)
            {
                identity.GiveOwnership(owner);
                Debug.Log($"Ownership transferred to: {owner.Value.id.value}");
                // ReSharper disable once Unity.InstantiateWithoutParent
                voiceReceiver.transform.SetParent(transform);
                voiceReceiver.transform.localPosition = new Vector3(0, 1, 0);
            }
        }
    }

    protected override void Update()
    {
        base.Update();
        if (isOwner)
        {
            cameraTarget.transform.position = transform.position + offset;
        }
    }

    protected override void Destroyed()
    {
        base.Destroyed();

        // If the local player disconnects or dies, stop the camera
        if (isOwner && CameraFollow.Instance != null)
        {
            CameraFollow.Instance.SetTarget(null);
        }
    }

    // Helper for friction setup
    private void SetupFriction(WheelCollider wc, ref WheelFrictionCurve wfc, ref float slip)
    {
        wfc = new WheelFrictionCurve();
        wfc.extremumSlip = wc.sidewaysFriction.extremumSlip;
        slip = wc.sidewaysFriction.extremumSlip;
        wfc.extremumValue = wc.sidewaysFriction.extremumValue;
        wfc.asymptoteSlip = wc.sidewaysFriction.asymptoteSlip;
        wfc.asymptoteValue = wc.sidewaysFriction.asymptoteValue;
        wfc.stiffness = wc.sidewaysFriction.stiffness;
    }

    protected override void GetFinalInput(ref CarInputData input)
    {
        input.Horizontal = Input.GetAxis("Horizontal");
        input.Vertical = Input.GetAxis("Vertical");
        input.Handbrake = Input.GetKey(KeyCode.Space);
    }

    protected override void Simulate(CarInputData input, ref CarInputHandler.State state,  float deltaTime) 
    {
        float dt = deltaTime;

        // Only check if there is any input
        bool isStopped = carRigidbody.velocity.sqrMagnitude < 0.01f && carRigidbody.angularVelocity.sqrMagnitude < 0.01f;
        bool noInput = input.Vertical == 0 && input.Horizontal == 0 && !input.Handbrake;

        if (isStopped && noInput && !state.IsDrifting && !state.DeceleratingCar)
        {
            Brakes(); 
            return;
        }

        // Calculate the value after input
        carSpeed = (2 * Mathf.PI * frontLeftCollider.radius * frontLeftCollider.rpm * 60) / 1000;
        localVelocityX = transform.InverseTransformDirection(carRigidbody.velocity).x;
        localVelocityZ = transform.InverseTransformDirection(carRigidbody.velocity).z;

        // Handle input
        HandleSteering(input.Horizontal, dt, ref state);

        if (input.Vertical > 0)
        {
            state.DeceleratingCar = false;
            GoForward(dt, ref state);
        }
        else if (input.Vertical < 0)
        {
            state.DeceleratingCar = false;
            GoReverse(dt, ref state);
        }
        else if (input.Vertical == 0 && !input.Handbrake && !state.DeceleratingCar)
        {
            state.DeceleratingCar = true;
        }

        if (input.Handbrake)
        {
            state.DeceleratingCar = false;
            Handbrake(dt, ref state);
        }
        else
        {
            RecoverTraction(dt, ref state);
        }

        if (state.DeceleratingCar && input.Vertical == 0 && !input.Handbrake)
        {
            DecelerateCarLogic(dt, ref state);
        }
        else if (input.Vertical == 0 && !input.Handbrake)
        {
            ThrottleOff();
        }
    }

    protected override void UpdateView(CarInputHandler.State state, CarInputHandler.State? verified)
    {
        HandleVisuals(state);
        AnimateWheelMeshes();
        CarSpeedUI();
        CarSounds(state);
    }
    
    void HandleSteering(float horizontalInput, float dt, ref CarInputHandler.State state)
    {
        if (horizontalInput < 0)
        {
            state.SteeringAxis -= (dt * 10f * steeringSpeed);
            if (state.SteeringAxis < -1f) state.SteeringAxis = -1f;
        }
        else if (horizontalInput > 0)
        {
            state.SteeringAxis += (dt * 10f * steeringSpeed);
            if (state.SteeringAxis > 1f) state.SteeringAxis = 1f;
        }
        else
        {
             if (state.SteeringAxis < 0f) state.SteeringAxis += (dt * 10f * steeringSpeed);
             else if (state.SteeringAxis > 0f) state.SteeringAxis -= (dt * 10f * steeringSpeed);
             if (Mathf.Abs(state.SteeringAxis) < 0.15f) state.SteeringAxis = 0f;
        }

        var steeringAngle = state.SteeringAxis * maxSteeringAngle;
        SetSteerAngle(frontLeftCollider, steeringAngle);
        SetSteerAngle(frontRightCollider, steeringAngle);
    }

    void GoForward(float dt, ref CarInputHandler.State state)
    {
        state.IsDrifting = Mathf.Abs(localVelocityX) > 2.5f;
        state.ThrottleAxis += (dt * 3f);
        if (state.ThrottleAxis > 1f) state.ThrottleAxis = 1f;

        if (localVelocityZ < -1f) Brakes();
        else
        {
            if (Mathf.RoundToInt(carSpeed) < maxSpeed) ApplyMotorTorque((accelerationMultiplier * 50f) * state.ThrottleAxis);
            else ApplyMotorTorque(0);
        }
    }

    void GoReverse(float dt, ref CarInputHandler.State state)
    {
        state.IsDrifting = Mathf.Abs(localVelocityX) > 2.5f;
        state.ThrottleAxis -= (dt * 3f);
        if (state.ThrottleAxis < -1f) state.ThrottleAxis = -1f;

        if (localVelocityZ > 1f) Brakes();
        else
        {
            if (Mathf.Abs(Mathf.RoundToInt(carSpeed)) < maxReverseSpeed) ApplyMotorTorque((accelerationMultiplier * 50f) * state.ThrottleAxis);
            else ApplyMotorTorque(0);
        }
    }

    void DecelerateCarLogic(float dt, ref CarInputHandler.State state)
    {
        state.IsDrifting = Mathf.Abs(localVelocityX) > 2.5f;
        if (state.ThrottleAxis != 0f)
        {
            if (state.ThrottleAxis > 0f) state.ThrottleAxis -= (dt * 10f);
            else if (state.ThrottleAxis < 0f) state.ThrottleAxis += (dt * 10f);
            if (Mathf.Abs(state.ThrottleAxis) < 0.15f) state.ThrottleAxis = 0f;
        }

        carRigidbody.velocity = carRigidbody.velocity * (1f / (1f + (0.025f * decelerationMultiplier)));
        ApplyMotorTorque(0);

        if (carRigidbody.velocity.magnitude < 0.25f)
        {
            carRigidbody.velocity = Vector3.zero;
            state.DeceleratingCar = false;
        }
    }

    void Handbrake(float dt, ref CarInputHandler.State state)
    {
        state.DriftingAxis += dt;
        float secureStartingPoint = state.DriftingAxis * FLWextremumSlip * handbrakeDriftMultiplier;

        if (secureStartingPoint < FLWextremumSlip) state.DriftingAxis = FLWextremumSlip / (FLWextremumSlip * handbrakeDriftMultiplier);
        if (state.DriftingAxis > 1f) state.DriftingAxis = 1f;

        state.IsDrifting = Mathf.Abs(localVelocityX) > 2.5f;

        if (state.DriftingAxis < 1f) ApplyFriction(state.DriftingAxis * handbrakeDriftMultiplier);
        
        state.IsTractionLocked = true;
    }

    void RecoverTraction(float dt, ref CarInputHandler.State state)
    {
        state.IsTractionLocked = false;
        state.DriftingAxis -= (dt / 1.5f);
        if (state.DriftingAxis < 0f) state.DriftingAxis = 0f;

        if (FLwheelFriction.extremumSlip > FLWextremumSlip) ApplyFriction(state.DriftingAxis * handbrakeDriftMultiplier);
        else ApplyFriction(0, true);
    }

    void ApplyFriction(float multiplier, bool reset = false)
    {
        if (reset)
        {
            SetSidewaysFriction(frontLeftCollider, FLwheelFriction);
            SetSidewaysFriction(frontRightCollider, FRwheelFriction);
            SetSidewaysFriction(rearLeftCollider, RLwheelFriction);
            SetSidewaysFriction(rearRightCollider, RRwheelFriction);
        }
        else
        {
            WheelFrictionCurve curve = FLwheelFriction; curve.extremumSlip *= multiplier; 
            SetSidewaysFriction(frontLeftCollider, curve);
            
            curve = FRwheelFriction; curve.extremumSlip *= multiplier;
            SetSidewaysFriction(frontRightCollider, curve);
            
            curve = RLwheelFriction; curve.extremumSlip *= multiplier;
            SetSidewaysFriction(rearLeftCollider, curve);
            
            curve = RRwheelFriction; curve.extremumSlip *= multiplier;
            SetSidewaysFriction(rearRightCollider, curve);
        }
    }

    void ApplyMotorTorque(float torque)
    {
        SetBrakeTorque(frontLeftCollider, 0); SetBrakeTorque(frontRightCollider, 0);
        SetBrakeTorque(rearLeftCollider, 0); SetBrakeTorque(rearRightCollider, 0);
        SetMotorTorque(frontLeftCollider, torque); SetMotorTorque(frontRightCollider, torque);
        SetMotorTorque(rearLeftCollider, torque); SetMotorTorque(rearRightCollider, torque);
    }

    void ThrottleOff()
    {
        SetMotorTorque(frontLeftCollider, 0); SetMotorTorque(frontRightCollider, 0);
        SetMotorTorque(rearLeftCollider, 0); SetMotorTorque(rearRightCollider, 0);
    }

    void Brakes()
    {
        SetBrakeTorque(frontLeftCollider, brakeForce); SetBrakeTorque(frontRightCollider, brakeForce);
        SetBrakeTorque(rearLeftCollider, brakeForce); SetBrakeTorque(rearRightCollider, brakeForce);
    }

    void HandleVisuals(CarInputHandler.State state)
    {
        if (!useEffects) return;
        bool showDrift = state.IsDrifting;
        bool showSkid = (state.IsTractionLocked || Mathf.Abs(localVelocityX) > 5f) && Mathf.Abs(carSpeed) > 12f;

        if (showDrift && !RLWParticleSystem.isPlaying) { RLWParticleSystem.Play(); RRWParticleSystem.Play(); }
        else if (!showDrift && RLWParticleSystem.isPlaying) { RLWParticleSystem.Stop(); RRWParticleSystem.Stop(); }

        RLWTireSkid.emitting = showSkid;
        RRWTireSkid.emitting = showSkid;
    }

    void AnimateWheelMeshes()
    {
        UpdateWheelPose(frontLeftCollider, frontLeftMesh);
        UpdateWheelPose(frontRightCollider, frontRightMesh);
        UpdateWheelPose(rearLeftCollider, rearLeftMesh);
        UpdateWheelPose(rearRightCollider, rearRightMesh);
    }
    
    void UpdateWheelPose(WheelCollider collider, GameObject mesh)
    {
        Vector3 pos; Quaternion rot;
        collider.GetWorldPose(out pos, out rot);
        mesh.transform.position = pos;
        mesh.transform.rotation = rot;
    }

    void CarSpeedUI()
    {
        if (useUI && carSpeedText != null)
        {
            var speed = carRigidbody.velocity.magnitude * 3.6f;
            carSpeedText.text = Mathf.RoundToInt(Mathf.Abs(speed)).ToString();
        }
    }

    void CarSounds(CarInputHandler.State state)
    {
        if (!useSounds) return;
        if (carEngineSound != null) carEngineSound.pitch = initialCarEngineSoundPitch + (Mathf.Abs(carSpeed) / 25f);

        if ((state.IsDrifting || (state.IsTractionLocked && Mathf.Abs(carSpeed) > 12f)))
        {
            if (!tireScreechSound.isPlaying) tireScreechSound.Play();
        }
        else
        {
             if (tireScreechSound.isPlaying) tireScreechSound.Stop();
        }
    }

    private void SetMotorTorque(WheelCollider wc, float value) { if (Mathf.Abs(wc.motorTorque - value) > 0.1f) wc.motorTorque = value; }
    private void SetBrakeTorque(WheelCollider wc, float value) { if (Mathf.Abs(wc.brakeTorque - value) > 0.1f) wc.brakeTorque = value; }
    private void SetSteerAngle(WheelCollider wc, float value) { if (Mathf.Abs(wc.steerAngle - value) > 0.1f) wc.steerAngle = value; }
    private void SetSidewaysFriction(WheelCollider wc, WheelFrictionCurve curve) { if (Mathf.Abs(wc.sidewaysFriction.extremumSlip - curve.extremumSlip) > 0.01f) wc.sidewaysFriction = curve; }
}