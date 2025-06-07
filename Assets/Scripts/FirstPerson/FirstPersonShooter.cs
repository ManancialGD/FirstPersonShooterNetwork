using Unity.Netcode;
using UnityEngine;

public class FirstPersonShooter : NetworkBehaviour
{
    [SerializeField] private float cooldown = 0.15f;
    [SerializeField] private bool showDebugShots = true;
    [SerializeField] private GameObject clientHitDebug;
    [SerializeField] private GameObject serverHitDebug;
    [SerializeField] private AudioSource shootSound;

    private GunAnimator gunAnimator;
    private HealthModule healthModule;

    private float lastShotTime = 0;

    private void Awake()
    {
        gunAnimator = GetComponent<GunAnimator>();
        healthModule = GetComponent<HealthModule>();
    }

    public void Shoot()
    {
        float time = NetworkManager.Singleton.ServerTime.TimeAsFloat;

        if (time - lastShotTime > cooldown)
        {
            ShootServerRpc(time);
            if (shootSound != null && shootSound.clip != null)
                shootSound.Play();

            gunAnimator.Shoot();
            lastShotTime = time;

            var cameraController = GetComponent<FirstPersonCamera>();

            Vector3 origin = cameraController.CameraTarget.transform.position;
            Vector3 direction = cameraController.CameraTarget.forward;

            if (Physics.Raycast(origin, direction, out RaycastHit hit, 500))
            {
                // Debug.Log($"[FirstPersonShooter] Shot hit: {hit.collider.gameObject.name} at {hit.point}");

                if (clientHitDebug != null && showDebugShots)
                {
                    var hitMarker = Instantiate(clientHitDebug, hit.point, Quaternion.identity);
                    Destroy(hitMarker, 2f);
                }
            }
        }
    }

    [ServerRpc]
    private void ShootServerRpc(float time, ServerRpcParams serverRpcParams = default)
    {
        // if (healthModule?.IsDead == true)
        //     return;

        ulong senderClientId = serverRpcParams.Receive.SenderClientId;

        // if (time > NetworkManager.Singleton.ServerTime.TimeAsFloat)
        //     return;
        
        // Debug.Log($"[FirstPersonShooter] ShootRpc called. Time: {time}, LastShotTime: {lastShotTime}, SenderClientId: {senderClientId}");
        if (time - lastShotTime > cooldown)
        {
            // Debug.Log("[FirstPersonShooter] Cooldown passed, calculating shot.");
            gunAnimator.ShootClientRpc();
            ShootClientRpc();
            Vector3 hitPos = ShotsManager.Instance.CalculateShoot(time, senderClientId);
            lastShotTime = time;
            CreateDebugHitClientRpc(hitPos);
        }
        else
        {
            // Debug.Log("[FirstPersonShooter] Cooldown not passed, shot ignored.");
        }
    }

    [ClientRpc]
    private void ShootClientRpc()
    {
        if (IsOwner) return;
        if (shootSound != null && shootSound.clip != null)
            shootSound.Play();
    }

    [ClientRpc]
    private void CreateDebugHitClientRpc(Vector3 position)
    {
        if (serverHitDebug != null && showDebugShots)
        {
            // Debug.Log($"[FirstPersonShooter] Server hit at position: {position}");
            var hitMarker = Instantiate(serverHitDebug, position, Quaternion.identity);
            Destroy(hitMarker, 2f);
        }
    }
}
