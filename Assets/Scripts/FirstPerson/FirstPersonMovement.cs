using System.Collections;
using TMPro;
using Unity.Cinemachine;
using Unity.Netcode;
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
    [SerializeField] private float height = 2f;                  // Player height (used for ground check)
    [SerializeField] private float jumpForce = 4f;
    [SerializeField] private float groundCheckDistance = 0.2f;
    [SerializeField] private LayerMask groundMask;
    [SerializeField] private CinemachineCamera cam;

    [Header("Debug Settings")]
    [SerializeField]
    private bool showSpeed = false;

    private Rigidbody rb;
    public bool IsGrounded { get; private set; }
    private Vector3 groundNormal;
    private Vector3 force;

    private bool jumpCooldown = false;
    private Coroutine waitForGround;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    public void UpdateMovement(Vector2 moveInput, float deltaTime)
    {
        force = Vector3.zero;

        CalculateMovement(moveInput, deltaTime);
        ApplyVelocity();
    }

    public void Jump()
    {
        if (IsGrounded)
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

    private void CalculateMovement(Vector2 moveInput, float deltaTime)
    {
        Vector3 wishDir = GetWishDirection(moveInput);

        float wishSpeed = moveInput.magnitude * maxSpeed;

        if (IsGrounded && !jumpCooldown)
        {
            ApplyFriction();
            Accelerate(wishDir, wishSpeed, accelerate, deltaTime);
        }
        else
        {
            AirAccelerate(wishDir, wishSpeed, airAccelerate, deltaTime);
        }
    }

    private void Update()
    {
        GroundCheck();
    }

    // //           // //
    //    MOVEMENT     //
    // //           // //

    private Vector3 GetWishDirection(Vector2 moveInput)
    {
        // Convert input to world space relative to camera
        Transform camTransform = cam.transform;

        Vector3 forward = Vector3.ProjectOnPlane(camTransform.forward, groundNormal).normalized;
        Vector3 right = Vector3.ProjectOnPlane(camTransform.right, groundNormal).normalized;

        return (forward * moveInput.y + right * moveInput.x).normalized;
    }

    private void ApplyFriction()
    {
        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, groundNormal);

        Vector3 velocity = horizontalVelocity;
        float speed = horizontalVelocity.magnitude;

        if (speed < 0.001f) return;

        float control = speed < stopSpeed ? stopSpeed : speed;
        float drop = control * friction * Time.fixedDeltaTime;
        float newSpeed = Mathf.Max(speed - drop, 0);
        float scale = newSpeed / speed;

        velocity *= scale;

        rb.linearVelocity = velocity;
    }

    private void Accelerate(Vector3 wishDir, float wishSpeed, float accel, float deltaTime)
    {
        // Quake does Vector3.Dot(rb.linearVelocity, wishDir);
        // But this cause a speed gain when spamming 'A' and 'D' while going forward.
        // We will fix this by using the real current speed.
        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, groundNormal);
        float currentSpeed = horizontalVelocity.magnitude;

        float addSpeed = wishSpeed - currentSpeed;

        if (addSpeed <= 0) return;

        float accelSpeed = accel * deltaTime * wishSpeed;
        accelSpeed = Mathf.Min(accelSpeed, addSpeed);

        force += wishDir * accelSpeed;
    }

    private void AirAccelerate(Vector3 wishDir, float wishSpeed, float accel, float deltaTime)
    {
        float wishSpd = Mathf.Min(wishSpeed, 30f * 0.0254f); // Quake air speed cap is 30, we convert to meters/s

        // This is not the actual currentSpeed.
        // Using the Dot product will add a little bit of speed when going nearly 90 degrees to current velocity.
        // Making when holding 'A' and 'D' while rotating the camera to that direction gaining a little bit of speed.
        // This is what brings the Quake's Air Strafe speed gain exploit.
        // We will mantain this as a design choice.
        float currentSpeed = Vector3.Dot(rb.linearVelocity, wishDir);

        float addSpeed = wishSpd - currentSpeed;

        if (addSpeed <= 0) return;

        float accelSpeed = accel * deltaTime * wishSpd;
        accelSpeed = Mathf.Min(accelSpeed, addSpeed);

        force += wishDir * accelSpeed;
    }

    // //           // //
    //      JUMP       //
    // //           // //
    private void PerformJump()
    {
        if (IsGrounded && !jumpCooldown)
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
            if (IsGrounded)
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
            IsGrounded = true;
            groundNormal = hit.normal;
        }
        else
        {
            IsGrounded = false;
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

    // //             // //
    //     Debugging     //
    // //             // //

    private void OnGUI()
    {
        if (showSpeed)
        {
            float speed = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z).magnitude;

            GUI.Label(new Rect(10, 10, 200, 20), $"Speed: {speed:F2} m/s");
        }
    }
}