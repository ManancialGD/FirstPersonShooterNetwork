# Project First Person Shooter Network

Author : Breno Pinto 22308986

---
# First Person Controller

## Code Architecture

- **FirstPersonController**: Main entry point, manages camera, movement, and skin.
- **FirstPersonMovement**: Handles Quake-inspired movement, jumping, and ground detection.
- **FirstPersonCamera**: Manages camera rotation, sensitivity, and network synchronization.
- **FirstPersonSkin**: Handles mesh visibility, animation, and local/remote differentiation.

## Networked Movement System

Movement is inspired by Quake, with air-strafing and bunny hopping.  
See [Quake Source code](https://github.com/id-Software/Quake/blob/master/WinQuake/sv_user.c#L207) for reference.

### Plan
Movement should be server sided with client side prediction.
Also, this will come with an AntiCheat, checking if the movement is checking based on the input.

### Client-Side Prediction Implementation

What's Client-Side prediction?
Basicly, the movement of the character is server sided, meaning only the server can move it.
<br>So, in the client we send a message to the server "Hey, move me to this direction : x,y"
<br>Then the server moves it and send a message back to the client "Hey, I moved you, now you're position is x,y"

Problem: **LAG**

Since it takes time to the message to go to the server and back to the client,
the input lag is big.

Fix: **Client side prediction!**

We will send a message to the server, then move in the client!
Then the server will also move in the sever and send to other clients.

But then we have this problem: What if the position of the client is diferent from the server (maybe physics problem, maybe cheats?)

If the client is predicting, and the server is authoritative, then we need to make sure the client can “roll back” and reapply its unconfirmed inputs if there's a mismatch.

Let’s say:

- You predict moving right (→) for 3 frames.
- Server tells you: “You're actually at X, not where you thought.”
- You roll back to X, then replay those 3 inputs to catch up.

That’s where the InputBuffer comes in.

It's just a array that we access cricularly

```cs
private const int BUFFER_SIZE = 128;
private readonly InputEntry[] inputBuffer = new InputEntry[BUFFER_SIZE];
private int bufferHead = 0;  // Write position
private int bufferTail = 0;  // Read position
private int bufferCount = 0;
```

This "rollback" is actually reconciliation, or rubber banding.

To Reconcile we check if the difference on the current position and the server position is greater then a velue

```cs
if (Vector3.Distance(transform.position, serverPosition) > 0.1f)
{
    transform.position = serverPosition;

    // Replay all remaining inputs in order
    int current = bufferTail;
    for (int i = 0; i < bufferCount; i++)
    {
        ref InputEntry entry = ref inputBuffer[current];
        if (entry.isJump)
        {
            movementController.Jump();
        }
        else
        {
            movementController.UpdateMovement(entry.moveInput, entry.deltaTime);
        }
        current = (current + 1) % BUFFER_SIZE;
    }
}
```

So now we have another problem...
it's too jittery.
It's jittery because of: The server doesn't predict the character movement,
making it move in the client but stop and come back to the start when the server starts moving...

to make this jitter more consistent, we should move in a fixed time.
So we will move in `FixedUpdate` rather then `Update`.
We will also add interpolation on the client for the rigid body.

Another thing I noticed is that we shouldn't just set the client position to the servers.
we should take the last known position to be verified correct, then move it. Like this:

```cs
int rollbackIndex = FindInputIndexByTick(serverTick);
if (rollbackIndex >= 0)
    transform.position = inputBuffer[rollbackIndex].predictedPosition;
else
    transform.position = serverPosition;
```

then, apply the inputs again, to get to where we should be.

```cs
int current = bufferTail;
for (int i = 0; i < bufferCount; i++)
{
    ref InputEntry entry = ref inputBuffer[current];

    entry.predictedPosition = transform.position;

    if (entry.isJump)
        movementController.Jump();
    else
        movementController.UpdateMovement(entry.moveInput, Time.fixedDeltaTime);

    current = (current + 1) % BUFFER_SIZE;
}
```

After a while of testing, like this the client needs to reconcile several times per second, like 30 times.
Wich is reeeally jittery.

Now, when adding interpolation for only the clients,
It reconciles a loot less then whithout it!
Of course, the movement now isn't as snappy, but it's smoothier.

```cs
private void Start()
{
    ...
    if (IsClient)
    {
        if (TryGetComponent<Rigidbody>(out var rb))
            rb.interpolation = RigidbodyInterpolation.Interpolate;
    }
}
```

## Camera

### Plan
Camera is controlled by the local player.
Local player has control over the rotation and transmit it to the other clients.
Server will have a Anti-Cheat system, that will validate the rotation of the camera based on the mouse delta.

### Input Handling

The `FirstPersonCamera` class have a method named `UpdateView`.
Called by the controller when the player is allowed to look around.
In this Method, we will rotate the camera target based on the delta of the mouse.

```cs
float mouseX = input.x * senseX * 0.15f;
float mouseY = input.y * senseY * 0.15f * (invertMouseY ? 1f : -1f);
```

### Remote & Client

Now, to transmit the rotation to other clients we can't just make the other clients calculate it.
This is because the rotation is calculated by the local player's input.
So we use a Network Variable instead:

```cs
private readonly NetworkVariable<Vector2> networkRotation = new(
    Vector2.zero,
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Owner
    );
```

We utilize a Vector2 to symbolize pitch and yaw instead of a Quaternion,
because a Quaternion has more data that we're not using to send to the server and other clients.
More specificly, the z and w.

Then, in `OnNetworkSpawn` we can register the `OnValueChanged` event, to get the rotation for remote clients.

```cs
public override void OnNetworkSpawn()
{
    base.OnNetworkSpawn();

    // If not the owner, update rotation from network variable
    if (!IsOwner)
    {
        networkRotation.OnValueChanged += OnRotationChanged;
    }
}
```

Of course, we need to unregister the even on `OnNetworkDespawn`.

### AntiCheat
Not implemented.

## Shooting

### Plan
Shooting will be **instant** and server-authoritative.
When a player shoots, the client sends a request to the server via a ServerRpc.
The server performs the hit detection, spawns any visual effects or damage results, and notifies clients of the outcome.

For lag compensation,
the server should rewind time slightly to account for the network delay between the shooter and the target.
When a shot is received,
the server must check where each target player was positioned when the shooter fired (from the shooter's perspective).
This is commonly known as lag compensation or server-side rewind.

Great video that talks about this: 
[What goes into making a multiplayer FPS game?](https://www.youtube.com/watch?v=JOH5NEErS4Y&ab_channel=RiftDivision) by Rift Division

## Remote and Local meshes (skins)

## IK

### Local IK

### Remote IK

---

# References

## Movement
- [Okay, but how does airstrafing ACTUALLY work?](https://www.youtube.com/watch?v=gRqoXy-0d84&ab_channel=zweek)
by zweek on YouTube
- [Quake Source code](https://github.com/id-Software/Quake/blob/master/WinQuake/sv_user.c#L207)

## Networking
- [What goes into making a multiplayer FPS game?](https://www.youtube.com/watch?v=JOH5NEErS4Y&ab_channel=RiftDivision)
by Rift Division
- [Gabriel Gambetta's Prediction Guide](https://www.gabrielgambetta.com/client-side-prediction-server-reconciliation.html)