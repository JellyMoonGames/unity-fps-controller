using System;
using UnityEngine;
using UnityEngine.InputSystem;

public enum State { Standing, Crouching, Sprinting, Sliding }
public enum SlopeState { Flat, Up, Down }

public class MovementController : MonoBehaviour
{
    #region Public Properties

    public State CurrentState           { get; private set; }
    public State PreviousState          { get; private set; }

    public float HorizontalSpeed        { get; private set; }
    public float VerticalSpeed          { get; private set; }
    public float HorizontalInput        { get; private set; }
    public float VerticalInput          { get; private set; }
    public float JumpAllowTimeTrack     { get; private set; }
    public float JumpInputTrack         { get; private set; }
    public float SlopeAngle             { get; private set; }
    public SlopeState SlopeDirection    { get; private set; }

    public bool IsOnSlope           => SlopeAngle > 5f;
    public bool IsMoving            => Mathf.Abs(CC.velocity.x) >= 0.015f || Mathf.Abs(CC.velocity.y) >= 0.015f || Mathf.Abs(CC.velocity.z) >= 0.015f;
    public bool IsValidForwardInput => VerticalInput > 0.1f && (HorizontalInput <= 0.3f && HorizontalInput >= -0.3f);
    public bool TryingToMove        => inputVector.x == 0f && inputVector.y == 0f ? false : true;
    public bool TryingToSprint      => Controls.Movement.Sprint.ReadValue<float>() > 0.1f;
    public bool InActionState       => CurrentState == State.Sliding;
    public bool IsGrounded          => JumpAllowTimeTrack >= 0f;
    public bool ObjectIsAboveHead   { get; private set; }

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
    [SerializeField] private float gravity = 18f;

    [Header("Jump Settings")]
    [SerializeField] private float jumpSpeed = 15f;
    [SerializeField] private int maxAmountOfJumps = 1;
    [SerializeField] private bool canAirJump = false;

    [Header("Crouch Settings")]
    [SerializeField] private float crouchHeight = 0.85f;
    [SerializeField] private float crouchSpeed = 15f;

    [Header("Slide Settings")]
    [SerializeField] private float slideDuration = 2f;
    [SerializeField] private float slideSpeedThreshold = 5f;

    [Header("Physics Interaction")]
    [SerializeField] private float crouchPushPower = 1f;
    [SerializeField] private float normalPushPower = 2f;
    [SerializeField] private float sprintPushPower = 3f;
    [SerializeField] private float slidePushPower = 4f;
    
    [Header("References")]
    [SerializeField] private Transform crouchObject;

    [Header("Toggle Settings")]
    [SerializeField] private bool canMove = true;
    [SerializeField] private bool canJump = true;
    [SerializeField] private bool canSprint = true;
    [SerializeField] private bool canCrouch = true;
    [SerializeField] private bool canSlide = true;

    #endregion

    #region Private Variables

    // Input variables
    private Vector2 inputVector = Vector2.zero;
    private Vector3 movementVector = Vector3.zero;
    private float horizontalInputVelocity = 0f;
    private float verticalInputVelocity = 0f;

    // Movement Variables
    private float targetHorizontalSpeed = 0f;
    private float targetVerticalSpeed = 0f;
    private bool initiateSprint = false;
    private bool wasGrounded = false;
    private bool runOnceGrounded = false;

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

    // Slide Variables
    private float currentSlideTimer = 0f;
    private bool initiateSlide = false;
    
    // Component References
    private Rigidbody hitRigidbody;

    #endregion
    
    #region Events

    public event Action OnJump;
    public event Action OnCrouch;
    public event Action OnSlide;
    public event Action OnSprint;
    public event Action OnLand;
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

        currentSlideTimer = slideDuration;

        #endregion
    }

    private void Update()
    {
        HandleInput();
        CalculateSlopeAngles();
        UpdateJumpSystem();
        UpdateSprintSystem();
        UpdateCrouchSystem();
        UpdateSlideSystem();
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
            OnCrouch?.Invoke();
            return;
        }

        // If the player tries to jump whilst sliding, cancel the slide and set state to 'Standing'.
        if(CurrentState == State.Sliding && IsGrounded)
        {
            StopSlide(State.Standing);
        }

        // If the player can jump, initiate it.
        if(maxAmountOfJumps > 0 && JumpInputTrack <= 0f)
        {
            if(IsGrounded && SlopeAngle <= CC.slopeLimit || IsGrounded == false && currentAmountOfJumps < maxAmountOfJumps && canAirJump)
            {
                initiateJump = true;
            }
        }
    }

    private void Crouch(InputAction.CallbackContext context)
    {
        if(canCrouch == false || ObjectIsAboveHead) return;

        // Crouch / Un-crouch
        if((CurrentState == State.Standing || CurrentState == State.Crouching) && IsGrounded && JumpInputTrack <= 0f)
        {
            SetState((CurrentState == State.Crouching) ? State.Standing : State.Crouching);
            OnCrouch?.Invoke();
        }

        // Slide
        else if(canSlide && CurrentState == State.Sprinting && VerticalSpeed >= slideSpeedThreshold && IsGrounded && JumpInputTrack <= 0f)
        {
            initiateSlide = true;
            OnSlide?.Invoke();
        }

        // Cancel Sliding
        else if(CurrentState == State.Sliding && IsGrounded)
        {
            StopSlide(State.Standing);
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

            OnCrouch?.Invoke();

            return;
        }

        // If the player is able to sprint, initiate it.
        if(IsMoving && IsGrounded && IsValidForwardInput && !InActionState && JumpInputTrack <= 0f)
        {
            initiateSprint = true;
            OnSprint?.Invoke();
        }
    }

    private void UpdateJumpSystem()
    {        
        wasGrounded = IsGrounded;

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

        // OnLand Event
        if(!wasGrounded && wasGrounded != IsGrounded)
        {
            OnLand?.Invoke();
            verticalVelocity = -1f;
            wasGrounded = IsGrounded;
        }

        if(initiateJump)
        {
            verticalVelocity = jumpSpeed;
            JumpInputTrack = jumpInputDelayTime;
            currentAmountOfJumps++;
            OnJump?.Invoke();

            initiateJump = false;
        }
    }

    private void UpdateCrouchSystem()
    {        
        ObjectIsAboveHead = Physics.Raycast(transform.position + new Vector3(0f, CC.height, 0f), transform.up, (initialHeight - CC.height) + 0.025f);

        float targetHeight = (CurrentState == State.Crouching || CurrentState == State.Sliding) ? crouchHeight : initialHeight;
        CC.height = Mathf.Lerp(CC.height, targetHeight, crouchSpeed * Time.deltaTime);
        CC.center = (CurrentState == State.Crouching || CurrentState == State.Sliding) ? new Vector3(initialCenter.x, initialCenter.y - ((initialHeight - CC.height) / 2), initialCenter.z) : initialCenter;

        float targetCrouchObjectHeight = (CurrentState == State.Crouching || CurrentState == State.Sliding) ? initialCrouchObjectHeight - (initialHeight - CC.height) : initialCrouchObjectHeight;
        crouchObject.localPosition = Vector3.Lerp(crouchObject.localPosition, new Vector3(0f, targetCrouchObjectHeight, 0f), crouchSpeed * Time.deltaTime);
    }

    private void UpdateSlideSystem()
    {
        if(initiateSlide)
        {
            // Is Sliding
            if(IsMoving && currentSlideTimer > 0 && IsValidForwardInput && SlopeDirection != SlopeState.Up)
            {
                currentSlideTimer -= Time.deltaTime;
                if(CurrentState != State.Sliding) SetState(State.Sliding);
            }
            // Finished Sliding
            else
            {
                StopSlide(State.Crouching);
            }
        }
    }
    
    private void UpdateSprintSystem()
    {
        // Continous Sprint Check
        if(IsMoving && IsGrounded && TryingToSprint && IsValidForwardInput && !InActionState && !ObjectIsAboveHead && JumpInputTrack <= 0f)
        {
            initiateSprint = true;
        }
        
        if(initiateSprint)
        {            
            if(IsMoving && IsGrounded && IsValidForwardInput && !InActionState)
            {
                if(CurrentState != State.Sprinting) SetState(State.Sprinting);
            }
            else
            {
                initiateSprint = false;
                if(CurrentState != State.Sliding) SetState(State.Standing);
            }
        }
    }

    private void UpdateMovementSpeed()
    {
        // Set movement Speeds for the different states
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

            case State.Sliding:
            {
                targetHorizontalSpeed = Mathf.Lerp(targetHorizontalSpeed, horizontalCrouchSpeed, 1f * Time.deltaTime);
                targetVerticalSpeed = Mathf.Lerp(targetVerticalSpeed, forwardCrouchSpeed, 1f * Time.deltaTime);
                break;
            }
        }

        HorizontalSpeed = Mathf.Lerp(HorizontalSpeed, targetHorizontalSpeed, movementTransitionSpeed * Time.deltaTime);
        VerticalSpeed = Mathf.Lerp(VerticalSpeed, targetVerticalSpeed, movementTransitionSpeed * Time.deltaTime);
        
        movementVector = new Vector3(HorizontalInput * HorizontalSpeed, verticalVelocity, VerticalInput * VerticalSpeed);
        movementVector = transform.rotation * movementVector;
    }

    private void StopSlide(State transitionState)
    {
        if(CurrentState != State.Sliding) return;

        SetState(transitionState);
        currentSlideTimer = slideDuration;
        initiateSlide = false;
    }
    
    private void SetState(State state)
    {
        PreviousState = CurrentState;
        CurrentState = state;
    }

    private void CalculateSlopeAngles()
    {
        if(!IsGrounded) return;

        Vector3 origin = transform.position; origin.y += 0.1f;
        if(Physics.Raycast(origin, -transform.up, out var hit, CC.skinWidth + 0.5f, ~LayerMask.GetMask("Player")))
        {
            SlopeAngle = Vector3.Angle(Vector3.up, hit.normal);
            float slopeDirectionValue = Vector3.Angle(hit.normal, transform.forward);
            
            if (slopeDirectionValue >= 88 && slopeDirectionValue <= 92) SlopeDirection = SlopeState.Flat;
            else if(slopeDirectionValue < 88)                           SlopeDirection = SlopeState.Down;
            else if(slopeDirectionValue > 92)                           SlopeDirection = SlopeState.Up;
        }
        else
        {
            SlopeAngle = 0f;
        }
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
        else if(CurrentState == State.Sliding)      { finalPushPower = slidePushPower;  speedContributionRatio = 1f; }
        else                                        { finalPushPower = normalPushPower; speedContributionRatio = 0.3f; }

        Vector3 pushDirection = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
        float pushVelocity = finalPushPower * Mathf.Clamp(Mathf.Abs(HorizontalInput) + Mathf.Abs(VerticalInput), 0, 1) + ((CC.velocity.x + CC.velocity.y + CC.velocity.z) / 3f) * speedContributionRatio;
        hitRigidbody.velocity = pushDirection * pushVelocity;
        OnHitPhysicsObject?.Invoke();

        #endregion
    }

    private void OnEnable() => Controls.Enable();
    private void OnDisable() => Controls.Disable();
}