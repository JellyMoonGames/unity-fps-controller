using System;
using UnityEngine;
using UnityEngine.InputSystem;

public enum State { Standing, Crouching, Sprinting }

public class MovementControllerNew : MonoBehaviour
{
    #region Public Properties

    public State CurrentState       { get; private set; }
    public State PreviousState      { get; private set; }

    public float HorizontalSpeed    { get; private set; }
    public float VerticalSpeed      { get; private set; }
    public float HorizontalInput    { get; private set; }
    public float VerticalInput      { get; private set; }
    public float JumpAllowTimeTrack { get; private set; }
    public float JumpInputTrack     { get; private set; }

    public bool ObjectIsAboveHead   { get; private set; }
    public bool IsGrounded          { get { return JumpAllowTimeTrack >= 0f; } }
    public bool TryingToMove        { get { return (inputVector.x == 0f && inputVector.y == 0f) ? false : true; } }
    public bool TryingToSprint      { get { return Controls.Movement.Sprint.ReadValue<float>() > 0.1f; } }
    public bool SprintingIsValid    { get { return VerticalInput > 0.1f && (HorizontalInput <= 0.3f && HorizontalInput >= -0.3f); } }
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
        Controls.Movement.Sprint.canceled += delegate
        { 
            if(CurrentState == State.Sprinting)
            {
                initiateSprint = false;
                SetState(State.Standing);
            }
        };
    }

    private void Start()
    {
        #region Initialising Variables

        PreviousState = CurrentState;

        JumpAllowTimeTrack = jumpAllowTime;
        inputSmoothAmount = groundSmoothAmount;
        currentAmountOfJumps = 0;

        initialCrouchObjectHeight = crouchObject.localPosition.y;
        initialHeight = CC.height;
        initialCenter = CC.center;
        
        ObjectIsAboveHead = false;

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
        if(canJump == false || ObjectIsAboveHead) return;

        // If the player tries to jump and they are currently crouched, un-crouch.
        if(CurrentState == State.Crouching)
        {
            SetState(State.Standing);
            if(OnCrouch != null) OnCrouch();
            return;
        }

        if(maxAmountOfJumps > 0 && JumpInputTrack <= 0f)
        {
            if(IsGrounded || IsGrounded == false && currentAmountOfJumps < maxAmountOfJumps && canAirJump)
            {
                initiateJump = true;
            }
        }
    }

    private void Crouch(InputAction.CallbackContext context)
    {
        if(canCrouch == false || ObjectIsAboveHead) return;

        if(CurrentState != State.Sprinting && IsGrounded && JumpInputTrack <= 0f)
        {
            SetState((CurrentState == State.Crouching) ? State.Standing : State.Crouching);
            if(OnCrouch != null) OnCrouch();
        }
    }

    private void Sprint(InputAction.CallbackContext context)
    {
        if(canSprint == false || ObjectIsAboveHead) return;

        // If the player tries to sprint and they are currently crouched, un-crouch.
        if(CurrentState == State.Crouching)
        {
            if(VerticalInput > 0f) initiateSprint = true;
            else SetState(State.Standing);

            if(OnCrouch != null) OnCrouch();

            return;
        }

        if(IsMoving && IsGrounded && SprintingIsValid && JumpInputTrack <= 0f)
        {
            initiateSprint = true;
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
            JumpInputTrack = 0;
        }
        else
        {
            inputSmoothAmount = airSmoothAmount;
            JumpAllowTimeTrack -= Time.deltaTime;
            waitToLandTrack = 0.1f;
            verticalVelocity -= gravity * Time.deltaTime;
        }

        JumpInputTrack -= Time.deltaTime;
        
        if(waitToLandTrack <= 0f) verticalVelocity = 0f;
        if(maxAmountOfJumps <= 0) maxAmountOfJumps = 0;

        if(initiateJump)
        {
            verticalVelocity = jumpSpeed;
            JumpInputTrack = jumpInputDelayTime;
            currentAmountOfJumps++;
            if(OnJump != null) OnJump();

            initiateJump = false;
        }
    }

    private void UpdateCrouchSystem()
    {        
        ObjectIsAboveHead = Physics.Raycast(transform.position + new Vector3(0f, CC.height, 0f), transform.up, (initialHeight - CC.height) + 0.025f);

        float targetHeight = (CurrentState == State.Crouching) ? crouchHeight : initialHeight;
        CC.height = Mathf.Lerp(CC.height, targetHeight, crouchSpeed * Time.deltaTime);
        CC.center = (CurrentState == State.Crouching) ? new Vector3(initialCenter.x, initialCenter.y - ((initialHeight - CC.height) / 2), initialCenter.z) : initialCenter;

        float targetCrouchObjectHeight = (CurrentState == State.Crouching) ? initialCrouchObjectHeight - (initialHeight - CC.height) : initialCrouchObjectHeight;
        crouchObject.localPosition = Vector3.Lerp(crouchObject.localPosition, new Vector3(0f, targetCrouchObjectHeight, 0f), crouchSpeed * Time.deltaTime);
    }
    
    private void UpdateSprintSystem()
    {
        // Continous sprint check.
        if(IsMoving && IsGrounded && TryingToSprint && SprintingIsValid && !ObjectIsAboveHead && JumpInputTrack <= 0f)
        {
            initiateSprint = true;
        }
        
        if(initiateSprint)
        {            
            if(IsMoving && IsGrounded && SprintingIsValid)
            {
                if(CurrentState != State.Sprinting) SetState(State.Sprinting);
            }
            else if(IsMoving == false || SprintingIsValid == false || IsGrounded == false)
            {
                initiateSprint = false;
                SetState(State.Standing);
            }
        }
    }

    private void UpdateMovementSpeed()
    {
        switch(CurrentState)
        {
            case State.Standing:
            {
                targetHorizontalSpeed = horizontalWalkSpeed;
                targetVerticalSpeed = (VerticalInput >= 0f) ? forwardWalkSpeed : backwardWalkSpeed;
                break;
            }
            
            case State.Crouching:
            {
                targetHorizontalSpeed = horizontalCrouchSpeed;
                targetVerticalSpeed = (VerticalInput >= 0f) ? forwardCrouchSpeed : backwardCrouchSpeed;
                break;
            }

            case State.Sprinting:
            {
                targetHorizontalSpeed = horizontalWalkSpeed;
                targetVerticalSpeed = forwardSprintSpeed;
                break;
            }
        }


        HorizontalSpeed = Mathf.Lerp(HorizontalSpeed, targetHorizontalSpeed, movementTransitionSpeed * Time.deltaTime);
        VerticalSpeed = Mathf.Lerp(VerticalSpeed, targetVerticalSpeed, movementTransitionSpeed * Time.deltaTime);
        
        movementVector = new Vector3(HorizontalInput * HorizontalSpeed, verticalVelocity, VerticalInput * VerticalSpeed);
        movementVector = transform.rotation * movementVector;
    }

    private void SetState(State state)
    {
        PreviousState = CurrentState;
        CurrentState = state;
    }
    
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        #region Physics Interaction

        hitRigidbody = hit.collider.attachedRigidbody;
        if(hitRigidbody == null || hitRigidbody.isKinematic || hit.moveDirection.y < -0.3f) return;

        float finalPushPower;
        float speedContributionRatio;

        if(CurrentState == State.Crouching)         { finalPushPower = crouchPushPower; speedContributionRatio = 0.1f; }
        else if(CurrentState == State.Sprinting)    { finalPushPower = sprintPushPower; speedContributionRatio = 0.8f; }
        else                                        { finalPushPower = normalPushPower; speedContributionRatio = 0.3f; }

        Vector3 pushDirection = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
        float pushVelocity = finalPushPower * Mathf.Clamp(Mathf.Abs(HorizontalInput) + Mathf.Abs(VerticalInput), 0, 1) + ((CC.velocity.x + CC.velocity.y + CC.velocity.z) / 3f) * speedContributionRatio;
        hitRigidbody.velocity = pushDirection * pushVelocity;
        if(OnHitPhysicsObject != null) OnHitPhysicsObject();

        #endregion
    }

    private void OnEnable() => Controls.Enable();
    private void OnDisable() => Controls.Disable();
}