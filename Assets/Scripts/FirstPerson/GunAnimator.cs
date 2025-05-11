using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class GunAnimator : MonoBehaviour
{
    [SerializeField] private float swayMultiplier = 0.15f;
    [SerializeField] private float recoilForce = 2.5f;
    [SerializeField] private float swaySmooth = 8;
    [SerializeField] private float recoilSmooth = 5f;
    [SerializeField] private float bobFrequency = 5f;
    [SerializeField] private float bobAmplitude = 0.05f;
    [SerializeField] private float bobSpeedMultiplier = 1f;
    [SerializeField] private Transform gunHolder;
    [SerializeField] private Transform gunBobTarget;
    [SerializeField] private Transform gunSwayRoot;
    [SerializeField] private InputActionReference lookAction;

    private float bobTimer;

    private Vector3 bobOriginalPosition;
    private Vector3 originalPosition;

    private Rigidbody rb;
    private FirstPersonMovement playerMovement;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        playerMovement = GetComponent<FirstPersonMovement>();

        originalPosition = gunHolder.transform.localPosition;
        bobOriginalPosition = gunBobTarget.localPosition;
    }

    private void Update()
    {
        MakeSway();
        MakeBob();

        if (Input.GetKeyDown(KeyCode.Mouse0))
        {
            Recoil();
        }

        if (Vector3.Distance(gunHolder.transform.localPosition, originalPosition) > 0.01f)
        {
            gunHolder.transform.localPosition = Vector3.Slerp(gunHolder.transform.localPosition, originalPosition, Time.deltaTime * recoilSmooth);
        }
        else if (Vector3.Distance(gunHolder.transform.localPosition, originalPosition) != 0)
        {
            gunHolder.transform.localPosition = originalPosition;
        }
    }

    private void MakeSway()
    {
        Vector2 lookInput = Vector2.ClampMagnitude(lookAction.action.ReadValue<Vector2>() * swayMultiplier, 1f);

        Quaternion rotarionX = Quaternion.AngleAxis(-lookInput.y, Vector3.right);
        Quaternion rotationY = Quaternion.AngleAxis(lookInput.x, Vector3.up);

        Quaternion targetRotation = rotationY * rotarionX;

        gunSwayRoot.localRotation = Quaternion.Slerp(gunSwayRoot.localRotation, targetRotation, Time.deltaTime * swaySmooth);
    }

    private void MakeBob()
    {
        // Get horizontal velocity only (no jumping impact)
        Vector3 flatVelocity = Vector3.ClampMagnitude(new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z), playerMovement.MaxSpeed);
        float speed = flatVelocity.magnitude;

        if (speed > 0.1f) // Only bob when moving
        {
            bobTimer += Time.deltaTime * bobFrequency * speed * bobSpeedMultiplier;

            float bobOffsetY = Mathf.Sin(bobTimer) * bobAmplitude;
            float bobOffsetX = Mathf.Cos(bobTimer * 0.5f) * bobAmplitude * 0.5f;

            Vector3 bobPosition = new Vector3(bobOffsetX, bobOffsetY, 0f);

            gunBobTarget.localPosition = Vector3.Lerp(
                gunBobTarget.localPosition,
                bobOriginalPosition + bobPosition,
                Time.deltaTime * 10f
            );
        }
        else
        {
            // Return to original position when idle
            gunBobTarget.localPosition = Vector3.Lerp(
                gunBobTarget.localPosition,
                bobOriginalPosition,
                Time.deltaTime * 5f
            );
        }
    }


    private void Recoil()
    {
        gunHolder.transform.localPosition += Vector3.back * recoilForce;
    }

}
