using System;
using UnityEngine;

public class CameraLook : MonoBehaviour
{
    #region Public Properties

    public bool IsLeaning { get; private set; }

    #endregion

    #region Inspector Variables

    [Header("Camera Look Settings")]
    [SerializeField] private float lookSensitivity = 3f;
    [SerializeField] private float smoothAmount = 0f;
    [SerializeField] private float upDownRange = 85f;

    [Header("Lean Settings")]
    [SerializeField] private float leanSpeed = 10f;
    [SerializeField] private float leanMovementAmount = 1;
    [SerializeField] private float leanRotationAmount = 20f;

    [Header("Headbob Settings")]
    [SerializeField] private float generalHeadbobSpeedMultiplier = 1f;
    [SerializeField] private float crouchHeadbobSpeedMultiplier = 2.0f;
    [SerializeField] private float normalHeadbobSpeedMultiplier = 1.4f;
    [SerializeField] private float sprintHeadbobSpeedMultiplier = 1.8f;
    [SerializeField] private float transitionSpeed = 20f;

    [Header("Crouch Bob Amount")]
    [SerializeField] private float crouchXBobAmount = 0.1f;
    [SerializeField] private float crouchYBobAmount = 0.2f;
    
    [Header("Normal Bob Amount")]
    [SerializeField] private float normalXBobAmount = 0.25f;
    [SerializeField] private float normalYBobAmount = 0.3f;

    [Header("Sprint Bob Amount")]
    [SerializeField] private float sprintXBobAmount = 0.3f;
    [SerializeField] private float sprintYBobAmount = 0.35f;
    
    [Header("References")]
    [SerializeField] private Transform playerObject;
    [SerializeField] private Transform leanObject;
    [SerializeField] private Transform headbobObject;

    [Header("Toggle Settings")]
    [SerializeField] private bool canLean = true;
    [SerializeField] private bool canHeadbob = true;

    #endregion

    #region Private Variables

    //Camera Look
    private float xAxisRaw = 0f;
    private float xAxisVelocity = 0f;
    private float xAxis = 0f;
    private float yAxisRaw = 0f;
    private float yAxisVelocity = 0f;
    private float yAxis = 0f;
    private float verticalRotation = 0f;

    //Leaning
    private Vector3 initialPosition = Vector3.zero;
    private Vector3 targetPosition = Vector3.zero;
    private Quaternion initialRotation = Quaternion.identity;
    private Quaternion targetRotation = Quaternion.identity;

    //Headbob
    private float timer = 0f;
    private float bobSpeed = 0f;
    private float xBobAmount = 0.5f;
    private float yBobAmount = 0.3f;
    private Vector3 headbobRestPosition = Vector3.zero;
    private Vector3 headbobObjectPosition = Vector3.zero;
    private bool isCycleFinished = false;
    
    //Component References
    private MovementController movementController;

    #endregion

    #region Events

    public event Action OnFootstepLeft;
    public event Action OnFootstepRight;
    public event Action OnLean;

    #endregion

    private void Awake()
    {
        movementController = playerObject.GetComponent<MovementController>();
    }

    private void Start()
    {
        #region Initialise Variables
        
        initialPosition = leanObject.localPosition;
        targetPosition = Vector3.zero;
        initialRotation = leanObject.localRotation;
        targetRotation = Quaternion.identity;
        IsLeaning = false;

        headbobRestPosition = headbobObject.localPosition;
        headbobObjectPosition = headbobObject.localPosition;
        isCycleFinished = false;
        
        #endregion
    }

    private void Update()
    {
        LookRotation();
        Leaning();
        Headbob();
    }

    private void LateUpdate()
    {
        ApplyMovement();
    }

    private void Headbob()
    {
        if(!canHeadbob) return;
        
        if(movementController.IsMoving && movementController.JumpAllowTimeTrack >= 0.1f)
        {
            bobSpeed = Mathf.Abs((((movementController.HorizontalSpeed + movementController.VerticalSpeed) / 2f) * normalHeadbobSpeedMultiplier * generalHeadbobSpeedMultiplier)
            * Mathf.Clamp((Mathf.Abs(movementController.HorizontalInput) + Mathf.Abs(movementController.VerticalInput)), 0f, 1f));

            xBobAmount = normalXBobAmount;
            yBobAmount = normalYBobAmount;

            if(movementController.IsCrouching)
            {
                bobSpeed = Mathf.Abs((((movementController.HorizontalSpeed + movementController.VerticalSpeed) / 2f) * crouchHeadbobSpeedMultiplier * generalHeadbobSpeedMultiplier)
                * Mathf.Clamp((Mathf.Abs(movementController.HorizontalInput) + Mathf.Abs(movementController.VerticalInput)), 0f, 1f));

                xBobAmount = crouchXBobAmount;
                yBobAmount = crouchYBobAmount;
            }

            if(movementController.IsObjectAboveHead)
            {
                bobSpeed = Mathf.Abs((((movementController.HorizontalSpeed + movementController.VerticalSpeed) / 2f) * crouchHeadbobSpeedMultiplier * generalHeadbobSpeedMultiplier)
                * Mathf.Clamp((Mathf.Abs(movementController.HorizontalInput) + Mathf.Abs(movementController.VerticalInput)), 0f, 1f));

                xBobAmount = crouchXBobAmount;

                if(normalYBobAmount == 0f && crouchYBobAmount == 0f && sprintYBobAmount == 0f)
                    yBobAmount = 0f;
                else
                    yBobAmount = 0.05f;
            }

            if(movementController.IsSprinting)
            {
                bobSpeed = Mathf.Abs((((movementController.HorizontalSpeed + movementController.VerticalSpeed) / 2f) * sprintHeadbobSpeedMultiplier * generalHeadbobSpeedMultiplier)
                * Mathf.Clamp((Mathf.Abs(movementController.HorizontalInput) + Mathf.Abs(movementController.VerticalInput)), 0f, 1f));

                xBobAmount = sprintXBobAmount;
                yBobAmount = sprintYBobAmount;
            }

            //Is Moving
            timer += bobSpeed * Time.deltaTime;

            Vector3 newPosition = new Vector3 // -> below
            (Mathf.Cos(timer) * xBobAmount * Mathf.Clamp((Mathf.Abs(movementController.HorizontalInput) + Mathf.Abs(movementController.VerticalInput)), 0f, 1f),                                        //x
            headbobRestPosition.y + Mathf.Abs((Mathf.Sin(timer) * (yBobAmount * Mathf.Clamp((Mathf.Abs(movementController.HorizontalInput) + Mathf.Abs(movementController.VerticalInput)), 0f, 1f)))),  //y
            headbobRestPosition.z);                                                                                                                                                                     //z

            headbobObjectPosition = newPosition;
        }
        else
        {
            //Isn't Moving
            timer = Mathf.PI / 2f;

            Vector3 newPosition = new Vector3
            (Mathf.Lerp(headbobObjectPosition.x, headbobRestPosition.x, transitionSpeed * Time.deltaTime),   //x
            Mathf.Lerp(headbobObjectPosition.y, headbobRestPosition.y, transitionSpeed * Time.deltaTime),    //y
            Mathf.Lerp(headbobObjectPosition.z, headbobRestPosition.z, transitionSpeed * Time.deltaTime));   //z

            headbobObjectPosition = newPosition;
        }

        //Reset Timer
        if(timer > Mathf.PI * 2f) timer = 0f;

        //Footstep Sounds
        if(timer == 0f)
        {
            isCycleFinished = true;
            if(OnFootstepRight != null) OnFootstepRight();

        }
        else if(isCycleFinished && timer >= Mathf.PI - 0.1f && timer <= Mathf.PI + 0.1f)
        {
            isCycleFinished = false;
            if(OnFootstepLeft != null) OnFootstepLeft();
        }
    }

    private void Leaning()
    {
        if(!canLean) return;
        
        if(movementController.JumpAllowTimeTrack >= 0.1f && movementController.IsSprinting == false)
        {
            if(Input.GetButton("Lean_Left"))
            {
                targetPosition = new Vector3(-leanMovementAmount, initialPosition.y, initialPosition.z);
                targetRotation = Quaternion.Euler(0f, 0f, leanRotationAmount);
                IsLeaning = true;
                if(OnLean != null) OnLean();
            }
            
            else if(Input.GetButton("Lean_Right"))
            {
                targetPosition = new Vector3(leanMovementAmount, initialPosition.y, initialPosition.z);
                targetRotation = Quaternion.Euler(0f, 0f, -leanRotationAmount);
                IsLeaning = true;
                if(OnLean != null) OnLean();
            }

            else if(Input.GetButton("Lean_Left") == false && Input.GetButton("Lean_Right") == false)
            {
                targetPosition = initialPosition;
                targetRotation = initialRotation;
                IsLeaning = false;
            }
        }
        else
        {
            targetPosition = initialPosition;
            targetRotation = initialRotation;
            IsLeaning = false;
        }
    }

    private void LookRotation()
    {
        xAxisRaw = Input.GetAxisRaw("Mouse X");
        yAxisRaw = Input.GetAxisRaw("Mouse Y");

        xAxis = Mathf.SmoothDamp(xAxis, xAxisRaw, ref xAxisVelocity, smoothAmount);
        yAxis = Mathf.SmoothDamp(yAxis, yAxisRaw, ref yAxisVelocity, smoothAmount);
        
        verticalRotation -= yAxis * lookSensitivity;
        verticalRotation = Mathf.Clamp(verticalRotation, -upDownRange, upDownRange);
    }

    private void ApplyMovement()
    {
        playerObject.Rotate(0f, xAxis * lookSensitivity, 0f);
        transform.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f);

        if(canLean)
        {
            leanObject.localPosition = Vector3.Lerp(leanObject.localPosition, targetPosition, leanSpeed * Time.deltaTime);
            leanObject.localRotation = Quaternion.Lerp(leanObject.localRotation, targetRotation, leanSpeed * Time.deltaTime);
        }

        if(canHeadbob) headbobObject.localPosition = Vector3.Lerp(headbobObject.localPosition, headbobObjectPosition, 5f * Time.deltaTime);
    }
}