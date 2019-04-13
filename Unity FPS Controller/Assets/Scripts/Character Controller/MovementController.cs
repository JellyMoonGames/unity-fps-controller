using System;
using UnityEngine;

public class MovementController : MonoBehaviour
{
    #region Public Properties

    public bool IsMoving { get; private set; }
    public bool IsCrouching { get; private set; }
    public bool IsSprinting { get; private set; }
    public bool IsGrounded { get { return JumpAllowTimeTrack >= 0f; } }
    public bool IsObjectAboveHead { get; private set; }
    public float HorizontalSpeed { get; private set; }
    public float VerticalSpeed { get; private set; }
    public float HorizontalInput { get; private set; }
    public float VerticalInput { get; private set; }
    public float JumpAllowTimeTrack { get; private set; }
    public CharacterController CC { get; private set; }

    #endregion

    #region Inspector Variables
    
    [Header("Crouch Speed")]
    [SerializeField] private float forwardCrouchSpeed = 5f;
    [SerializeField] private float backwardCrouchSpeed = 5f;
    [SerializeField] private float horizontalCrouchSpeed = 5f;

    [Header("Walk Speed")]
    [SerializeField] private float forwardWalkSpeed = 5f;
    [SerializeField] private float backwardWalkSpeed = 5f;
    [SerializeField] private float horizontalWalkSpeed = 5f;

    [Header("Sprint Speed")]
    [SerializeField] private float forwardSprintSpeed = 5f;

    [Header("Movement Settings")]
    [SerializeField] private float movementSpeedSmoothAmount = 0.15f;
    [SerializeField] private float groundSmoothAmount = 0.15f;
    [SerializeField] private float airSmoothAmount = 0.5f;

    [Header("Jump Settings")]
    [SerializeField] private float jumpSpeed = 5f;
    [SerializeField] private float gravity = 9.81f;
    [SerializeField] private int maxAmountOfJumps = 1;
    [SerializeField] private bool canJumpInMidAir = false;

    [Header("Crouch Settings")]
    [SerializeField] private float crouchHeight = 1.1f;
    [SerializeField] private float crouchSpeed = 15f;

    [Header("Physics Interaction")]
    [SerializeField] private float crouchPushPower = 2f;
    [SerializeField] private float normalPushPower = 2f;
    [SerializeField] private float sprintPushPower = 2f;
    
    [Header("References")]
    [SerializeField] private Transform crouchObject;

    [Header("Toggle Settings")]
    [SerializeField] private bool canMove = true;
    [SerializeField] private bool canJump = true;
    [SerializeField] private bool canSprint = true;
    [SerializeField] private bool canCrouch = true;

    #endregion

    #region Private Variables

    //Input variables
    private float horizontalInputRaw = 0f;
    private float horizontalInputVelocity = 0f;
    private float verticalInputRaw = 0f;
    private float verticalInputVelocity = 0f;
    private Vector3 inputVector = Vector3.zero;
    private Vector3 movementVector = Vector3.zero;

    //Jump Variables
    private float verticalVelocity = 0f;
    private int currentAmountOfJumps = 0;
    private float inputSmoothAmount = 0f;
    private float waitToLandTrack = 0f;
    private float jumpAllowTime = 0.2f;
    private float jumpInputDelayTime = 0.21f; // Must be greater than or equal to 'jumpAllowTime'
    private float jumpInputTrack = 0f;

    //Crouch Variables
    private float initialHeight = 0f;
    private Vector3 initialCenter = Vector3.zero;
    private float initialCrouchObjectHeight = 0f;
    
    //Component References
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
        Jumping();
        Movement();
        Crouching();

        //Apply The Movement
        CC.Move(movementVector * Time.deltaTime);
    }

    private void HandleInput()
    {
        horizontalInputRaw = Input.GetAxisRaw("Horizontal");
        verticalInputRaw = Input.GetAxisRaw("Vertical");

        HorizontalInput = (canMove) ? Mathf.SmoothDamp(HorizontalInput, horizontalInputRaw, ref horizontalInputVelocity, inputSmoothAmount) : 0f;
        VerticalInput = (canMove) ? Mathf.SmoothDamp(VerticalInput, verticalInputRaw, ref verticalInputVelocity, inputSmoothAmount) : 0f;

        inputVector = new Vector3(HorizontalInput, 0f, VerticalInput);
        if(inputVector.magnitude >= 1f) inputVector.Normalize();

        IsMoving = (horizontalInputRaw == 0f && verticalInputRaw == 0f) ? false : true;
    }

    private void Jumping()
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

        if(canJump)
        {
            if(maxAmountOfJumps > 0 && jumpInputTrack <= 0f)
            {
                if(Input.GetButtonDown("Jump") && IsCrouching == false)
                {
                    if(IsGrounded)
                    {
                        verticalVelocity = jumpSpeed;
                        jumpInputTrack = jumpInputDelayTime;
                        currentAmountOfJumps++;
                        if(OnJump != null) OnJump();
                    }
                    else if(IsGrounded == false && currentAmountOfJumps < maxAmountOfJumps && canJumpInMidAir)
                    {
                        verticalVelocity = jumpSpeed;
                        jumpInputTrack = jumpInputDelayTime;
                        currentAmountOfJumps++;
                        if(OnJump != null) OnJump();
                    }
                }
            }
        }
    }

    private void Crouching()
    {
        IsObjectAboveHead = Physics.Raycast(transform.position + new Vector3(0f, CC.height, 0f), transform.up, (initialHeight - CC.height) + 0.025f);

        if(canCrouch)
        {
            if(IsSprinting == false && IsGrounded && jumpInputTrack <= 0f)
            {
                if(Input.GetButtonDown("Crouch") && IsObjectAboveHead)
                {
                    IsCrouching = true;
                }

                if(Input.GetButtonDown("Crouch") && IsObjectAboveHead == false)
                {
                    IsCrouching = !IsCrouching;
                    if(OnCrouch != null) OnCrouch();
                }

                if((Input.GetButtonDown("Jump") || Input.GetButtonDown("Sprint")) && IsCrouching && IsObjectAboveHead == false)
                {
                    IsCrouching = false;
                    if(OnCrouch != null) OnCrouch();
                }
            }
        }
        else
        {
            IsCrouching = false;
        }
        
        float targetHeight = (IsCrouching) ? crouchHeight : initialHeight;
        CC.height = Mathf.Lerp(CC.height, targetHeight, crouchSpeed * Time.deltaTime);
        CC.center = (IsCrouching) ? new Vector3(initialCenter.x, initialCenter.y - ((initialHeight - CC.height) / 2), initialCenter.z) : initialCenter;

        float targetCrouchObjectHeight = (IsCrouching) ? initialCrouchObjectHeight - (initialHeight - CC.height) : initialCrouchObjectHeight;
        crouchObject.localPosition = Vector3.Lerp(crouchObject.localPosition, new Vector3(0f, targetCrouchObjectHeight, 0f), crouchSpeed * Time.deltaTime);
    }

    private void Movement()
    {   
        if(Input.GetButton("Sprint") && IsObjectAboveHead == false && canSprint)
        {
            IsCrouching = false;
            
            if(VerticalInput > 0f)
            {
                if(HorizontalInput <= 0.3f && HorizontalInput >= 0f || HorizontalInput >= -0.3f && HorizontalInput <= 0f)
                {
                    IsSprinting = true;
                }
                else
                {
                    IsSprinting = false;
                }
            }
        }

        if(Input.GetButton("Sprint") == false || IsGrounded == false || VerticalInput < 0f)
        {
            IsSprinting = false;
        }

        //Walking Speed
        if(IsCrouching == false && IsSprinting == false)
        {
            HorizontalSpeed = Mathf.Lerp(HorizontalSpeed, horizontalWalkSpeed, movementSpeedSmoothAmount * Time.deltaTime);

            if(VerticalInput > 0f) VerticalSpeed = Mathf.Lerp(VerticalSpeed, forwardWalkSpeed, movementSpeedSmoothAmount * Time.deltaTime);
            if(VerticalInput < 0f) VerticalSpeed = Mathf.Lerp(VerticalSpeed, backwardWalkSpeed, movementSpeedSmoothAmount * Time.deltaTime);
        }
        
        //Crouching Speed
        if(IsCrouching && IsSprinting == false)
        {
            HorizontalSpeed = Mathf.Lerp(HorizontalSpeed, horizontalCrouchSpeed, movementSpeedSmoothAmount * Time.deltaTime);

            if(VerticalInput > 0f) VerticalSpeed = Mathf.Lerp(VerticalSpeed, forwardCrouchSpeed, movementSpeedSmoothAmount * Time.deltaTime);
            if(VerticalInput < 0f) VerticalSpeed = Mathf.Lerp(VerticalSpeed, backwardCrouchSpeed, movementSpeedSmoothAmount * Time.deltaTime);
        }

        //Sprinting Speed
        if(IsSprinting && IsCrouching == false)
        {
            HorizontalSpeed = Mathf.Lerp(HorizontalSpeed, horizontalWalkSpeed, movementSpeedSmoothAmount * Time.deltaTime);
            VerticalSpeed = Mathf.Lerp(VerticalSpeed, forwardSprintSpeed, movementSpeedSmoothAmount * Time.deltaTime);
        }
        
        movementVector = new Vector3(inputVector.x * HorizontalSpeed, verticalVelocity, inputVector.z * VerticalSpeed);
        movementVector = transform.rotation * movementVector;
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        #region Physics Interaction

        hitRigidbody = hit.collider.attachedRigidbody;
        if(hitRigidbody == null || hitRigidbody.isKinematic || hit.moveDirection.y < -0.3f) return;

        float finalPushPower;
        
        if(IsCrouching) finalPushPower = crouchPushPower;
        else if(IsSprinting) finalPushPower = sprintPushPower;
        else finalPushPower = normalPushPower;

        Vector3 pushDirection = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
        hitRigidbody.velocity = pushDirection * (finalPushPower * Mathf.Clamp(Mathf.Abs(HorizontalInput) + Mathf.Abs(VerticalInput), 0, 1));
        if(OnHitPhysicsObject != null) OnHitPhysicsObject();

        #endregion
    }
}