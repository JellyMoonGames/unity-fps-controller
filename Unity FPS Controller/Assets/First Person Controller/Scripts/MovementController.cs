using System;
using UnityEngine;
using UnityEngine.InputSystem;

public enum State { Standing, Crouching, Sprinting, Sliding }
public enum SlopeState { Flat, Up, Down }

public class MovementController : MonoBehaviour
{
    #region Public Properties

    public State CurrentState { get; private set; }
    public State PreviousState { get; private set; }

    public float HorizontalSpeed { get; private set; }
    public float VerticalSpeed { get; private set; }
    public float HorizontalInput { get; private set; }
    public float VerticalInput { get; private set; }
    public float SlopeAngle { get; private set; }
    public Vector3 ForwardDirection { get; private set; }
    public SlopeState SlopeDirection { get; private set; }

    public bool IsOnSlope => SlopeAngle > 5f;
    public bool IsMoving => Mathf.Abs(cc.velocity.x) >= 0.015f || Mathf.Abs(cc.velocity.y) >= 0.015f || Mathf.Abs(cc.velocity.z) >= 0.015f;
    public bool IsValidForwardInput => VerticalInput > 0.1f && (HorizontalInput <= 0.3f && HorizontalInput >= -0.3f);
    public bool TryingToMove => inputVector.x == 0f && inputVector.y == 0f ? false : true;
    public bool TryingToSprint => Controls.Movement.Sprint.ReadValue<float>() > 0.1f;
    public bool InActionState => CurrentState == State.Sliding;
    public bool IsGrounded => cc.isGrounded || raycastFoundGround;
    public bool IsJumping { get; private set; }
    public bool ObjectIsAboveHead { get; private set; }

    public PlayerControls Controls { get; private set; }

    #endregion

    #region Inspector Variables

    [Header("Crouch Speed")]
    [SerializeField] private float forwardCrouchSpeed = 1.5f;
    [SerializeField] private float backwardCrouchSpeed = 1.5f;
    [SerializeField] private float horizontalCrouchSpeed = 1.5f;

    [Header("Walk Speed")]
    [SerializeField] private float forwardWalkSpeed = 3.5f;
    [SerializeField] private float backwardWalkSpeed = 3.5f;
    [SerializeField] private float horizontalWalkSpeed = 3.5f;

    [Header("Sprint Speed")]
    [SerializeField] private float forwardSprintSpeed = 6f;

    [Header("Movement Settings")]
    [SerializeField] private float movementTransitionSpeed = 4f;
    [SerializeField] private float groundSmoothAmount = 0.1f;
    [SerializeField] private float airSmoothAmount = 0.5f;
    [SerializeField] private float gravity = 18f;
    [SerializeField] private float groundedGravity = 6f;

    [Header("Jump Settings")]
    [SerializeField] private float jumpSpeed = 7f;
    [SerializeField] private int maxAmountOfJumps = 1;
    [SerializeField] private bool canAirJump = false;
    [SerializeField] private float jumpAllowTime = 0.35f;
    [SerializeField] private Vector3[] groundCheckOriginOffsets;

    [Header("Crouch Settings")]
    [SerializeField] private float crouchHeight = 1.1f;
    [SerializeField] private float crouchSpeed = 20f;

    [Header("Slide Settings")]
    [SerializeField] private float slideDuration = 1.5f;
    [SerializeField] private float slideSpeedThreshold = 5f;

    [Header("Camera Shake Settings")]
    [SerializeField] private float slideShakeAmplitude = 1f;
    [SerializeField] private float slideShakeSpeed = 1f;

    [Header("Physics Interaction")]
    [SerializeField] private float crouchPushPower = 1f;
    [SerializeField] private float normalPushPower = 2f;
    [SerializeField] private float sprintPushPower = 3f;
    [SerializeField] private float slidePushPower = 4f;

    [Header("References")]
    [SerializeField] private CameraShake cameraShake;
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
    private float verticalVelocity = 0f;
    private float inputSmoothAmount = 0f;
    private bool initiateSprint = false;
    private bool wasGrounded = false;
    private bool ccWasGrounded = false;
    private bool raycastFoundGround = false;

    // Jump Variables
    private int currentAmountOfJumps = 0;
    private float jumpAllowTimeTrack = 0f;
    private bool initiateJump = false;

    // Crouch Variables
    private float initialHeight = 0f;
    private float initialCrouchObjectHeight = 0f;
    private Vector3 initialCenter = Vector3.zero;

    // Slide Variables
    private float currentSlideTimer = 0f;
    private bool initiateSlide = false;
    private bool continousSlide = false;

    // Component References
    private CharacterController cc;
    private Rigidbody hitRigidbody;

    #endregion

    #region Events

    public event Action OnJump;
    public event Action OnCrouch;
    public event Action OnSlide;
    public event Action OnFinishSlide;
    public event Action OnSprint;
    public event Action OnLand;
    public event Action OnLeaveGround;
    public event Action OnHitPhysicsObject;

    private event Action OnCCLeaveGround;

    #endregion

    private void Awake()
    {
        cc = GetComponent<CharacterController>();
        Controls = new PlayerControls();

        // Input Events
        Controls.Movement.Move.performed += context => inputVector = context.ReadValue<Vector2>();
        Controls.Movement.Crouch.performed += Crouch;
        Controls.Movement.Jump.performed += Jump;
        Controls.Movement.Sprint.performed += Sprint;
        Controls.Movement.Sprint.canceled += delegate
        {
            if (CurrentState == State.Sprinting)
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

        HorizontalSpeed = horizontalWalkSpeed;
        VerticalSpeed = forwardWalkSpeed;
        jumpAllowTimeTrack = jumpAllowTime;
        inputSmoothAmount = groundSmoothAmount;
        currentAmountOfJumps = 0;

        initialCrouchObjectHeight = crouchObject.localPosition.y;
        initialHeight = cc.height;
        initialCenter = cc.center;
        ObjectIsAboveHead = false;

        ForwardDirection = transform.forward;
        currentSlideTimer = slideDuration;

        RaycastGroundCheck();
        wasGrounded = IsGrounded;

        #endregion
    }

    private void Update()
    {
        CalculateSlopeAngles();
        HandleInput();
        UpdateJumpSystem();
        UpdateSprintSystem();
        UpdateCrouchSystem();
        UpdateSlideSystem();
        UpdateMovementSpeed();
        cc.Move(movementVector * Time.deltaTime); // Apply The Movement to the 'CharacterController'
        UpdateEventSystems();
    }

    private void HandleInput()
    {
        HorizontalInput = (canMove) ? Mathf.SmoothDamp(HorizontalInput, inputVector.x, ref horizontalInputVelocity, inputSmoothAmount) : 0f;
        VerticalInput = (canMove) ? Mathf.SmoothDamp(VerticalInput, inputVector.y, ref verticalInputVelocity, inputSmoothAmount) : 0f;
    }

    private void Jump(InputAction.CallbackContext context)
    {
        if (canJump == false || ObjectIsAboveHead) return;

        // If the player tries to jump and they are currently crouched, un-crouch.
        if (CurrentState == State.Crouching)
        {
            SetState(State.Standing);
            OnCrouch?.Invoke();

            return;
        }

        // If the player tries to jump whilst sliding, cancel the slide and set state to 'Standing'.
        if (CurrentState == State.Sliding && (IsGrounded || jumpAllowTimeTrack >= 0f))
        {
            StopSlide(State.Standing);
        }

        // If the player can jump, initiate it.
        if (maxAmountOfJumps > 0 && (IsGrounded || jumpAllowTimeTrack >= 0f || canAirJump))
        {
            if ((IsGrounded && SlopeAngle <= cc.slopeLimit) || (!IsGrounded && currentAmountOfJumps < maxAmountOfJumps && (canAirJump || jumpAllowTimeTrack >= 0f)))
            {
                IsJumping = true;
                initiateJump = true;
            }
        }
    }

    private void Crouch(InputAction.CallbackContext context)
    {
        if (canCrouch == false || ObjectIsAboveHead) return;

        // Crouch / Un-crouch
        if ((CurrentState == State.Standing || CurrentState == State.Crouching) && IsGrounded)
        {
            SetState((CurrentState == State.Crouching) ? State.Standing : State.Crouching);
            OnCrouch?.Invoke();
        }

        // Slide
        else if (canSlide && CurrentState == State.Sprinting && VerticalSpeed >= slideSpeedThreshold && IsGrounded && SlopeDirection != SlopeState.Up)
        {
            initiateSlide = true;
            ForwardDirection = transform.forward;
            OnSlide?.Invoke();
            cameraShake?.Shake(slideShakeAmplitude, slideShakeSpeed);
        }

        // Cancel Sliding
        else if (CurrentState == State.Sliding && IsGrounded)
        {
            StopSlide(State.Standing);
        }
    }

    private void Sprint(InputAction.CallbackContext context)
    {
        if (canSprint == false || ObjectIsAboveHead) return;

        // If the player tries to sprint and they are currently crouched, un-crouch.
        if (CurrentState == State.Crouching)
        {
            if (VerticalInput > 0f) initiateSprint = true;
            else SetState(State.Standing);

            OnCrouch?.Invoke();

            return;
        }

        // If the player is able to sprint, initiate it.
        if (IsMoving && IsGrounded && IsValidForwardInput && !InActionState)
        {
            initiateSprint = true;
            OnSprint?.Invoke();
        }
    }

    private void UpdateJumpSystem()
    {
        wasGrounded = IsGrounded;
        ccWasGrounded = cc.isGrounded;
        if (cc.isGrounded) raycastFoundGround = false;

        if (IsGrounded)
        {
            inputSmoothAmount = groundSmoothAmount;
            jumpAllowTimeTrack = jumpAllowTime;
            currentAmountOfJumps = 0;
            verticalVelocity = -groundedGravity;
        }
        else
        {
            inputSmoothAmount = airSmoothAmount;
            jumpAllowTimeTrack -= Time.deltaTime;
            verticalVelocity -= gravity * Time.deltaTime;
        }

        if (initiateJump)
        {
            verticalVelocity = jumpSpeed;
            currentAmountOfJumps++;
            OnJump?.Invoke();

            initiateJump = false;
        }
    }

    private void UpdateCrouchSystem()
    {
        ObjectIsAboveHead = Physics.Raycast(transform.position + new Vector3(0f, cc.height, 0f), transform.up, (initialHeight - cc.height) + 0.025f);

        float targetHeight = (CurrentState == State.Crouching || CurrentState == State.Sliding) ? crouchHeight : initialHeight;
        cc.height = Mathf.Lerp(cc.height, targetHeight, crouchSpeed * Time.deltaTime);
        cc.center = (CurrentState == State.Crouching || CurrentState == State.Sliding) ? new Vector3(initialCenter.x, initialCenter.y - ((initialHeight - cc.height) / 2), initialCenter.z) : initialCenter;

        float targetCrouchObjectHeight = (CurrentState == State.Crouching || CurrentState == State.Sliding) ? initialCrouchObjectHeight - (initialHeight - cc.height) : initialCrouchObjectHeight;
        crouchObject.localPosition = Vector3.Lerp(crouchObject.localPosition, new Vector3(0f, targetCrouchObjectHeight, 0f), crouchSpeed * Time.deltaTime);
    }

    private void UpdateSlideSystem()
    {
        if (initiateSlide)
        {
            // Is Sliding
            if ((IsMoving && IsValidForwardInput && SlopeDirection != SlopeState.Up) && (currentSlideTimer > 0 || continousSlide))
            {
                if (currentSlideTimer > 0) currentSlideTimer -= Time.deltaTime;
                if (CurrentState != State.Sliding) SetState(State.Sliding);

                continousSlide = SlopeDirection == SlopeState.Down;
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
        if (IsMoving && IsGrounded && TryingToSprint && IsValidForwardInput && !InActionState && !ObjectIsAboveHead)
        {
            initiateSprint = true;
        }

        if (initiateSprint)
        {
            if (IsMoving && IsGrounded && IsValidForwardInput && !InActionState)
            {
                if (CurrentState != State.Sprinting) SetState(State.Sprinting);
            }
            else
            {
                initiateSprint = false;
                if (CurrentState != State.Sliding) SetState(State.Standing);
            }
        }
    }

    private void UpdateMovementSpeed()
    {
        // Set movement Speeds for the different states
        switch (CurrentState)
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
                    if (!continousSlide)
                    {
                        targetHorizontalSpeed = Mathf.MoveTowards(targetHorizontalSpeed, horizontalCrouchSpeed, 1f * Time.deltaTime);
                        targetVerticalSpeed = Mathf.MoveTowards(targetVerticalSpeed, forwardCrouchSpeed, 1f * Time.deltaTime);
                    }

                    break;
                }
        }

        HorizontalSpeed = Mathf.MoveTowards(HorizontalSpeed, targetHorizontalSpeed, movementTransitionSpeed * Time.deltaTime);
        VerticalSpeed = Mathf.MoveTowards(VerticalSpeed, targetVerticalSpeed, movementTransitionSpeed * Time.deltaTime);
        movementVector = new Vector3(HorizontalInput * HorizontalSpeed, verticalVelocity, VerticalInput * VerticalSpeed);

        if (!InActionState) ForwardDirection = transform.forward;
        movementVector = Quaternion.LookRotation(ForwardDirection, transform.up) * movementVector;
    }

    private void CalculateSlopeAngles()
    {
        if (!IsGrounded) // Only calculate slope angles if we are grounded
        {
            SlopeAngle = 0f;
            SlopeDirection = SlopeState.Flat;
            return;
        }

        if (groundCheckOriginOffsets.Length == 0)
        {
            Debug.LogError("No ground check origin offsets were defined.");
            return;
        }

        for (int i = 0; i < groundCheckOriginOffsets.Length; i++)
        {
            //Debug.DrawRay(transform.position + groundCheckOriginOffsets[i], -transform.up * 0.75f);
            if (Physics.Raycast(transform.position + groundCheckOriginOffsets[i], -transform.up, out var hit, 0.75f, ~LayerMask.GetMask("Player")))
            {
                SlopeAngle = Vector3.Angle(transform.up, hit.normal);
                float slopeDirectionValue = Vector3.Angle(hit.normal, ForwardDirection);

                if (slopeDirectionValue >= 88 && slopeDirectionValue <= 92) SlopeDirection = SlopeState.Flat;
                else if (slopeDirectionValue < 88) SlopeDirection = SlopeState.Down;
                else if (slopeDirectionValue > 92) SlopeDirection = SlopeState.Up;

                break;
            }
        }
    }

    private void RaycastGroundCheck()
    {
        if (IsJumping || cc.isGrounded)
        {
            raycastFoundGround = false;
            return;
        }

        // NOTE: Ray length needs to be long enough to hit ground when on slope
        raycastFoundGround = Physics.Raycast(transform.position + groundCheckOriginOffsets[0], -transform.up, 0.7f, ~LayerMask.GetMask("Player"));
    }

    private void UpdateEventSystems()
    {
        //OnCCLeaveGround Event
        if (ccWasGrounded && !cc.isGrounded)
        {
            OnCCLeaveGround?.Invoke();
            ccWasGrounded = cc.isGrounded;
            RaycastGroundCheck();
        }

        // OnLeaveGround Event
        if (wasGrounded && !IsGrounded)
        {
            if (!IsJumping) verticalVelocity = 0f;
            OnLeaveGround?.Invoke();
            wasGrounded = IsGrounded;
        }

        // OnLand Event
        if (!wasGrounded && wasGrounded != IsGrounded)
        {
            OnLand?.Invoke();
            wasGrounded = IsGrounded;
            if (IsJumping) IsJumping = false;
        }
    }

    private void StopSlide(State transitionState)
    {
        if (CurrentState != State.Sliding) return;

        SetState(transitionState);
        currentSlideTimer = slideDuration;
        continousSlide = false;
        initiateSlide = false;

        OnFinishSlide?.Invoke();
        cameraShake?.StopShaking();
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
        if (hitRigidbody == null || hitRigidbody.isKinematic || hit.moveDirection.y < -0.3f) return;

        float finalPushPower;
        float speedContributionRatio;

        if (CurrentState == State.Crouching) { finalPushPower = crouchPushPower; speedContributionRatio = 0.1f; }
        else if (CurrentState == State.Sprinting) { finalPushPower = sprintPushPower; speedContributionRatio = 0.8f; }
        else if (CurrentState == State.Sliding) { finalPushPower = slidePushPower; speedContributionRatio = 1f; }
        else { finalPushPower = normalPushPower; speedContributionRatio = 0.3f; }

        Vector3 pushDirection = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
        float pushVelocity = finalPushPower * Mathf.Clamp(Mathf.Abs(HorizontalInput) + Mathf.Abs(VerticalInput), 0, 1) + ((cc.velocity.x + cc.velocity.y + cc.velocity.z) / 3f) * speedContributionRatio;
        hitRigidbody.velocity = pushDirection * pushVelocity;
        OnHitPhysicsObject?.Invoke();

        #endregion
    }

    private void OnEnable() => Controls.Enable();
    private void OnDisable() => Controls.Disable();
}