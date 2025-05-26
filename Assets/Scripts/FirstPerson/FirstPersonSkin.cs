using Unity.Cinemachine;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public class FirstPersonSkin : NetworkBehaviour
{
    [SerializeField]
    private Transform cam;

    [SerializeField]
    private GameObject mesh;
    [SerializeField]
    private Animator anim;
    [SerializeField]
    private float animLerpValue = 10f;

    [SerializeField]
    private Renderer[] clientMeshes;

    [SerializeField]
    private Renderer[] serverMeshes;

    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (anim == null)
        {
            Debug.LogError("Animator component is not assigned in FirstPersonSkin.");
        }
        if (cam == null)
        {
            Debug.LogError("Camera transform is not assigned in FirstPersonSkin.");
        }
    }

    public void InitiateLocal()
    {
        if (clientMeshes != null && clientMeshes.Length > 0)
        {
            foreach (var mesh in clientMeshes)
            {
                mesh.gameObject.SetActive(true);
                mesh.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        if (serverMeshes != null && serverMeshes.Length > 0)
        {
            foreach (var mesh in serverMeshes)
            {
                mesh.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }
        }
    }

    public void InitiateRemote()
    {
        if (clientMeshes != null && clientMeshes.Length > 0)
        {
            foreach (var mesh in clientMeshes)
            {
                mesh.gameObject.SetActive(false);
                mesh.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        if (serverMeshes != null && serverMeshes.Length > 0)
        {
            foreach (var mesh in serverMeshes)
            {
                mesh.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }
    }

    public void UpdateSkin()
    {
        if (cam != null && mesh != null)
        {
            Vector3 newEulerAngles = mesh.transform.rotation.eulerAngles;
            newEulerAngles.y = cam.transform.rotation.eulerAngles.y;
            mesh.transform.rotation = Quaternion.Euler(newEulerAngles);
        }
    }

    public void UpdateAnimation()
    {
        Vector3 flatForward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
        Vector3 flatRight = Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized;

        Vector3 flatVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up).normalized;

        float forwardValue = Vector3.Dot(flatVelocity, flatForward);
        float horizontalValue = Vector3.Dot(flatVelocity, flatRight);


        float currentXInput = anim.GetFloat("xInput");
        float currentYInput = anim.GetFloat("yInput");

        anim.SetFloat("xInput", Mathf.Lerp(currentXInput, horizontalValue, Time.deltaTime * animLerpValue));
        anim.SetFloat("yInput", Mathf.Lerp(currentYInput, forwardValue, Time.deltaTime * animLerpValue));
    }
}
