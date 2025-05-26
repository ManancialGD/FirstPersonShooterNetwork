using UnityEngine;

struct InputEntry
{
    public uint tick;
    public Vector2 moveInput;
    public bool isJump;
    public Vector3 predictedPosition;
    public Vector3 velocity;
}