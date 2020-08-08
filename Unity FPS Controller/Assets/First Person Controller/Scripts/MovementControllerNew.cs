using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class MovementControllerNew : MonoBehaviour
{
    #region Public Properties

    public float HorizontalSpeed    { get; private set; }
    public float VerticalSpeed      { get; private set; }
    public float HorizontalInput    { get; private set; }
    public float VerticalInput      { get; private set; }
    public float JumpAllowTimeTrack { get; private set; }

    public bool IsCrouching         { get; private set; }
    public bool IsSprinting         { get; private set; }
    public bool IsObjectAboveHead   { get; private set; }
    public bool IsGrounded          { get { return JumpAllowTimeTrack >= 0f; } }
    public bool TryingToMove        { get { return (inputVector.x == 0f && inputVector.y == 0f) ? false : true; } }
    public bool IsMoving            { get { return Mathf.Abs(CC.velocity.x) >= 0.015f || Mathf.Abs(CC.velocity.y) >= 0.015f || Mathf.Abs(CC.velocity.z) >= 0.015f; } }

    public CharacterController CC   { get; private set; }
    public PlayerControls Controls  { get; private set; }

    #endregion

    #region Inspector Variables
    
    [Header("Crouch Speed")]
    [SerializeField] private float forwardCrouchSpeed = 2f;
    [SerializeField] private float backwardCrouchSpeed = 2f;
    [SerializeField] private float horizontalCrouchSpeed = 2f;

    [Header("Walk Speed")]
    [SerializeField] private float forwardWalkSpeed = 4f;
    [SerializeField] private float backwardWalkSpeed = 4f;
    [SerializeField] private float horizontalWalkSpeed = 4f;

    [Header("Sprint Speed")]
    [SerializeField] private float forwardSprintSpeed = 6f;

    [Header("Movement Settings")]
    [SerializeField] private float movementTransitionSpeed = 3f;
    [SerializeField] private float groundSmoothAmount = 0.1f;
    [SerializeField] private float airSmoothAmount = 0.5f;

    [Header("Jump Settings")]
    [SerializeField] private float jumpSpeed = 15f;
    [SerializeField] private float gravity = 18f;
    [SerializeField] private int maxAmountOfJumps = 1;
    [SerializeField] private bool canAirJump = false;

    [Header("Crouch Settings")]
    [SerializeField] private float crouchHeight = 0.85f;
    [SerializeField] private float crouchSpeed = 15f;

    [Header("Physics Interaction")]
    [SerializeField] private float crouchPushPower = 1f;
    [SerializeField] private float normalPushPower = 2f;
    [SerializeField] private float sprintPushPower = 3f;
    
    [Header("References")]
    [SerializeField] private Transform crouchObject;

    [Header("Toggle Settings")]
    [SerializeField] private bool canMove = true;
    [SerializeField] private bool canJump = true;
    [SerializeField] private bool canSprint = true;
    [SerializeField] private bool canCrouch = true;

    #endregion

    #region Private Variables

    // Input variables
    private Vector2 inputVector = Vector2.zero;
    private Vector3 movementVector = Vector3.zero;
    private float horizontalInputVelocity = 0f;
    private float verticalInputVelocity = 0f;

    // Jump Variables
    private int currentAmountOfJumps = 0;
    private float verticalVelocity = 0f;
    private float inputSmoothAmount = 0f;
    private float waitToLandTrack = 0f;
    private float jumpAllowTime = 0.2f;
    private float jumpInputDelayTime = 0.21f; // Must be greater than or equal to 'jumpAllowTime'
    private float jumpInputTrack = 0f;
    private bool initiateJump = false;

    // Crouch Variables
    private float initialHeight = 0f;
    private float initialCrouchObjectHeight = 0f;
    private Vector3 initialCenter = Vector3.zero;

    // Speed Variables
    private float targetHorizontalSpeed = 0f;
    private float targetVerticalSpeed = 0f;

    // Sprint Variables
    private bool initiateSprint = false;
    
    // Component References
    private Rigidbody hitRigidbody;

    #endregion
    
    #region Events

    public event Action OnJump;
    public event Action OnCrouch;
    public event Action OnHitPhysicsObject;
    
    #endregion

    private void Awake()
    {
        CC = GetComponent<CharacterController>();
        Controls = new PlayerControls();

        // Input Events
        Controls.Movement.Move.performed += context => inputVector = context.ReadValue<Vector2>();
        Controls.Movement.Crouch.performed += Crouch;
        Controls.Movement.Jump.performed += Jump;
        Controls.Movement.Sprint.performed += Sprint;
        Controls.Movement.Sprint.canceled += delegate{ if(IsSprinting) {initiateSprint = false;} };
    }

    private void Start()
    {
        #region Initialising Variables

        JumpAllowTimeTrack = jumpAllowTime;
        inputSmoothAmount = groundSmoothAmount;
        currentAmountOfJumps = 0;

        initialCrouchObjectHeight = crouchObject.localPosition.y;
        initialHeight = CC.height;
        initialCenter = CC.center;

        IsCrouching = false;
        IsSprinting = false;
        IsObjectAboveHead = false;

        #endregion
    }

    private void Update()
    {
        HandleInput();
        UpdateJumpSystem();
        UpdateSprintSystem();
        UpdateCrouchSystem();
        UpdateMovementSpeed();

        // Apply The Movement to the 'CharacterController'.
        CC.Move(movementVector * Time.deltaTime);
    }

    private void HandleInput()
    {
        HorizontalInput = (canMove) ? Mathf.SmoothDamp(HorizontalInput, inputVector.x, ref horizontalInputVelocity, inputSmoothAmount) : 0f;
        VerticalInput = (canMove) ? Mathf.SmoothDamp(VerticalInput, inputVector.y, ref verticalInputVelocity, inputSmoothAmount) : 0f;
    }
    
    private void Jump(InputAction.CallbackContext context)
    {
        if(canJump == false) return;

        // If the player tries to jump and they are currently crouched, un-crouch.
        if(IsCrouching && IsObjectAboveHead == false)
        {
            IsCrouching = false;
            if(OnCrouch != null) OnCrouch();
            return;
        }

        if(maxAmountOfJumps > 0 && jumpInputTrack <= 0f && IsCrouching == false)
        {
            if(IsGrounded || IsGrounded == false && currentAmountOfJumps < maxAmountOfJumps && canAirJump)
            {
                initiateJump = true;
            }
        }
    }

    private void Crouch(InputAction.CallbackContext context)
    {
        if(canCrouch == false)
        {
            IsCrouching = false;
            return;
        }

        if(IsSprinting == false && IsGrounded && jumpInputTrack <= 0f)
        {
            // If object is above head, crouch or stayed crouched.
            if(IsObjectAboveHead)
            {
                IsCrouching = true;
            }

            // If object isn't above head, toggle the current crouch status.
            if(IsObjectAboveHead == false)
            {
                IsCrouching = !IsCrouching;
                if(OnCrouch != null) OnCrouch();
            }
        }
    }

    private void Sprint(InputAction.CallbackContext context)
    {
        if(canSprint == false)
        {
            IsSprinting = false;
            return;
        }

        // If the player tries to sprint and they are currently crouched, un-crouch.
        if(IsCrouching && IsObjectAboveHead == false)
        {
            IsCrouching = false;
            if(OnCrouch != null) OnCrouch();
        }

        if(IsMoving && IsObjectAboveHead == false)
        {
            IsCrouching = false;
            
            if(VerticalInput > 0f)
            {
                initiateSprint = true;
            }
        }
    }

    private void UpdateJumpSystem()
    {
        if(CC.isGrounded)
        {
            inputSmoothAmount = groundSmoothAmount;
            JumpAllowTimeTrack = jumpAllowTime;
            waitToLandTrack -= Time.deltaTime;
            currentAmountOfJumps = 0;
            jumpInputTrack = 0;
        }
        else
        {
            inputSmoothAmount = airSmoothAmount;
            JumpAllowTimeTrack -= Time.deltaTime;
            waitToLandTrack = 0.1f;
            verticalVelocity -= gravity * Time.deltaTime;
        }

        jumpInputTrack -= Time.deltaTime;
        
        if(waitToLandTrack <= 0f) verticalVelocity = 0f;
        if(maxAmountOfJumps <= 0) maxAmountOfJumps = 0;

        if(initiateJump)
        {
            verticalVelocity = jumpSpeed;
            jumpInputTrack = jumpInputDelayTime;
            currentAmountOfJumps++;
            if(OnJump != null) OnJump();

            initiateJump = false;
        }
    }

    private void UpdateCrouchSystem()
    {        
        IsObjectAboveHead = Physics.Raycast(transform.position + new Vector3(0f, CC.height, 0f), transform.up, (initialHeight - CC.height) + 0.025f);

        float targetHeight = (IsCrouching) ? crouchHeight : initialHeight;
        CC.height = Mathf.Lerp(CC.height, targetHeight, crouchSpeed * Time.deltaTime);
        CC.center = (IsCrouching) ? new Vector3(initialCenter.x, initialCenter.y - ((initialHeight - CC.height) / 2), initialCenter.z) : initialCenter;

        float targetCrouchObjectHeight = (IsCrouching) ? initialCrouchObjectHeight - (initialHeight - CC.height) : initialCrouchObjectHeight;
        crouchObject.localPosition = Vector3.Lerp(crouchObject.localPosition, new Vector3(0f, targetCrouchObjectHeight, 0f), crouchSpeed * Time.deltaTime);
    }
    
    private void UpdateSprintSystem()
    {
        if(initiateSprint)
        {
            IsSprinting = IsMoving && (HorizontalInput <= 0.3f && HorizontalInput >= 0f || HorizontalInput >= -0.3f && HorizontalInput <= 0f);
            if(IsSprinting == false) initiateSprint = false;
        }

        if(Controls.Movement.Sprint.ReadValue<float>() > 0.1f == false || IsGrounded == false || VerticalInput <= 0f)
        {
            IsSprinting = false;
        }
    }

    private void UpdateMovementSpeed()
    {   
        //Walking Speed
        if(!IsCrouching && !IsSprinting)
        {
            targetHorizontalSpeed = horizontalWalkSpeed;
            targetVerticalSpeed = (VerticalInput >= 0f) ? forwardWalkSpeed : backwardWalkSpeed;
        }
        
        //Crouching Speed
        if(IsCrouching)
        {
            targetHorizontalSpeed = horizontalCrouchSpeed;
            targetVerticalSpeed = (VerticalInput >= 0f) ? forwardCrouchSpeed : backwardCrouchSpeed;
        }

        //Sprinting Speed
        if(IsSprinting)
        {
            targetHorizontalSpeed = horizontalWalkSpeed;
            targetVerticalSpeed = forwardSprintSpeed;
        }

        HorizontalSpeed = Mathf.Lerp(HorizontalSpeed, targetHorizontalSpeed, movementTransitionSpeed * Time.deltaTime);
        VerticalSpeed = Mathf.Lerp(VerticalSpeed, targetVerticalSpeed, movementTransitionSpeed * Time.deltaTime);
        
        movementVector = new Vector3(HorizontalInput * HorizontalSpeed, verticalVelocity, VerticalInput * VerticalSpeed);
        movementVector = transform.rotation * movementVector;
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        #region Physics Interaction

        hitRigidbody = hit.collider.attachedRigidbody;
        if(hitRigidbody == null || hitRigidbody.isKinematic || hit.moveDirection.y < -0.3f) return;

        float finalPushPower;
        float speedContributionRatio;

        if(IsCrouching)         { finalPushPower = crouchPushPower; speedContributionRatio = 0.1f; }
        else if(IsSprinting)    { finalPushPower = sprintPushPower; speedContributionRatio = 0.8f; }
        else                    { finalPushPower = normalPushPower; speedContributionRatio = 0.3f; }

        Vector3 pushDirection = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
        float pushVelocity = finalPushPower * Mathf.Clamp(Mathf.Abs(HorizontalInput) + Mathf.Abs(VerticalInput), 0, 1) + ((CC.velocity.x + CC.velocity.y + CC.velocity.z) / 3f) * speedContributionRatio;
        hitRigidbody.velocity = pushDirection * pushVelocity;
        if(OnHitPhysicsObject != null) OnHitPhysicsObject();

        #endregion
    }

    private void OnEnable() => Controls.Enable();
    private void OnDisable() => Controls.Disable();
}