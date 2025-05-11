using System.Collections;
using TMPro;
using Unity.Cinemachine;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// This class is heighly inspired by the Quake movement system.
/// You can find the original Quake source code here: 
/// https://github.com/id-Software/Quake/blob/master/WinQuake/sv_user.c#L207
/// 
/// This class is designed to be used with a Rigidbody component.
/// It handles player movement, jumping, and ground detection.
/// 
/// Quake had this two main exploits:
/// Air strafe speed gain exploit and gaining speed when spamming 'A' and 'D' while going forward.
/// For design purposes, we will mantain only air strafe speed gain.
/// 
/// This also has Bunny Hopping and will not have a cooldown for jumping. (Or feel the cooldown)
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class FirstPersonMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float maxSpeed = 320f * 0.0254f;    // Quake default is 320 (converted to meters/s)
    [SerializeField] private float accelerate = 10f;             // Ground acceleration - Quake default is 10f
    [SerializeField] private float airAccelerate = 10f;          // Air acceleration - Quake default is 10f
    [SerializeField] private float friction = 4f;                // Ground friction
    [SerializeField] private float stopSpeed = 100f * 0.0254f;   // Stop speed threshold (converted to meters/s)
    [SerializeField] private float height = 2f;                // Player height (used for ground check)
    [SerializeField] private float jumpForce = 4f;
    [SerializeField] private float groundCheckDistance = 0.2f;
    [SerializeField] private LayerMask groundMask;
    [SerializeField] private CinemachineCamera cam;

    [Header("Input Settings")]
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference jumpAction;

    [Header("UI Settings")]
    [SerializeField]
    private TextMeshProUGUI speedText;

    private Rigidbody rb;
    private Vector2 playerInput;
    private bool isGrounded;
    private Vector3 groundNormal;
    private Vector3 force;

    public bool IsGrounded => isGrounded;
    public float MaxSpeed => maxSpeed;
    public Vector2 MovementInput => moveAction.action.ReadValue<Vector2>();

    private bool jumpCooldown = false;
    private Coroutine waitForGround;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    public void UpdateMovement()
    {
        force = Vector3.zero;

        MovePlayer();
        ApplyVelocity();

        if (speedText != null)
        {
            Vector3 horizontalVelocity = new(rb.linearVelocity.x, 0, rb.linearVelocity.z);
            speedText.text = $"{horizontalVelocity.magnitude:F2}m/s";
        }
    }

    /// <summary>
    /// Facade for the movement system.
    /// This will handle the player movement and friction.
    /// </summary>
    private void MovePlayer()
    {
        if (moveAction != null)
            playerInput = moveAction.action.ReadValue<Vector2>().normalized;

        GroundCheck();

        if (jumpAction.action.triggered)
        {
            if (isGrounded)
            {
                PerformJump();
            }
            else
            {
                if (waitForGround != null)
                {
                    StopCoroutine(waitForGround);
                }
                waitForGround = StartCoroutine(WaitForGroundJump());
            }
        }

        Vector3 wishDir = GetWishDirection();
        float wishSpeed = playerInput.magnitude * maxSpeed;

        if (isGrounded && !jumpCooldown)
        {
            Accelerate(wishDir, wishSpeed, accelerate);
        }
        else
        {
            AirAccelerate(wishDir, wishSpeed, airAccelerate);
        }
    }

    private void Update()
    {
        if (isGrounded && !jumpCooldown)
            ApplyFriction();
    }

    // //           // //
    //    MOVEMENT     //
    // //           // //

    private Vector3 GetWishDirection()
    {
        // Convert input to world space relative to camera
        Transform camTransform = cam.transform;

        Vector3 forward = Vector3.ProjectOnPlane(camTransform.forward, groundNormal).normalized;
        Vector3 right = Vector3.ProjectOnPlane(camTransform.right, groundNormal).normalized;

        return (forward * playerInput.y + right * playerInput.x).normalized;
    }

    private void ApplyFriction()
    {
        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, groundNormal);

        Vector3 velocity = horizontalVelocity;
        float speed = horizontalVelocity.magnitude;

        if (speed < 0.001f) return;

        float control = speed < stopSpeed ? stopSpeed : speed;
        float drop = control * friction * Time.deltaTime;
        float newSpeed = Mathf.Max(speed - drop, 0);
        float scale = newSpeed / speed;

        velocity *= scale;

        rb.linearVelocity = velocity;
    }

    private void Accelerate(Vector3 wishDir, float wishSpeed, float accel)
    {
        // Quake does Vector3.Dot(rb.linearVelocity, wishDir);
        // But this cause a speed gain when spamming 'A' and 'D' while going forward.
        // We will fix this by using the real current speed.
        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, groundNormal);
        float currentSpeed = horizontalVelocity.magnitude;

        float addSpeed = wishSpeed - currentSpeed;

        if (addSpeed <= 0) return;

        float accelSpeed = accel * Time.deltaTime * wishSpeed;
        accelSpeed = Mathf.Min(accelSpeed, addSpeed);

        force += wishDir * accelSpeed;
    }

    private void AirAccelerate(Vector3 wishDir, float wishSpeed, float accel)
    {
        float wishSpd = Mathf.Min(wishSpeed, 30f * 0.0254f); // Quake air speed cap is 30, we convert to meters/s

        // This is not the actual currentSpeed.
        // Using the Dot product will add a little bit of speed when going nearly 90 degrees to current velocity.
        // Making when holding 'A' and 'D' while rotating the camera to that direction gaining a little bit of speed.
        // This is what brings the Quake's Air Strafe speed gain exploit.
        // We will mantain this as a design choice.2
        float currentSpeed = Vector3.Dot(rb.linearVelocity, wishDir);

        float addSpeed = wishSpd - currentSpeed;

        if (addSpeed <= 0) return;

        float accelSpeed = accel * Time.deltaTime * wishSpd;
        accelSpeed = Mathf.Min(accelSpeed, addSpeed);

        force += wishDir * accelSpeed;
    }

    // //           // //
    //      JUMP       //
    // //           // //
    private void PerformJump()
    {
        if (isGrounded && !jumpCooldown)
        {
            jumpCooldown = true;
            Invoke(nameof(ResetJumpCooldown), 0.1f);

            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
        }
    }

    private IEnumerator WaitForGroundJump()
    {
        float elapsedTime = 0f;

        while (elapsedTime < 0.05f)
        {
            elapsedTime += Time.deltaTime;
            if (isGrounded)
            {
                PerformJump();
            }
            yield return null;
        }
    }

    private void ResetJumpCooldown()
    {
        jumpCooldown = false;
    }

    // //             // //
    //   HELPER METHODS  //
    // //             // //

    private void GroundCheck()
    {
        Vector3 start = transform.position - new Vector3(0, (height / 2) - .1f, 0);

        if (Physics.Raycast(start, Vector3.down, out RaycastHit hit, groundCheckDistance, groundMask))
        {
            isGrounded = true;
            groundNormal = hit.normal;
        }
        else
        {
            isGrounded = false;
            groundNormal = Vector3.up;
        }
    }



    private void ApplyVelocity()
    {
        if (force.magnitude > 1e-4)
        {
            rb.AddForce(force, ForceMode.Impulse);
        }
    }
}