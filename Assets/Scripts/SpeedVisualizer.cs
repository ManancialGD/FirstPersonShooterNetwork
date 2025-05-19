using System.Linq;
using TMPro;
using UnityEngine;

public class SpeedVisualizer : MonoBehaviour
{
    private Rigidbody localPlayerRigidbody;
    private TextMeshProUGUI speedText;

    public bool sourceUnit = false;

    private void Awake()
    {
        speedText = GetComponentInChildren<TextMeshProUGUI>();
    }

    private void Start()
    {
        var players = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);

        localPlayerRigidbody = players.First(p => p.IsLocalPlayer)?.GetComponent<Rigidbody>() ?? null;
    }

    private void Update()
    {
        if (localPlayerRigidbody != null && speedText != null)
        {
            float speed = new Vector3(localPlayerRigidbody.linearVelocity.x, 0, localPlayerRigidbody.linearVelocity.z).magnitude;

            string speedS = "";

            if (sourceUnit)
            {
                speedS = $"{speed * 39.3701:0.00}i/s";
            }
            else
            {
                speedS = $"{speed:0.00}m/s";
            }

            speedText.text = speedS;
        }
    }
}
