# Project First Person Shooter Network

Author : Breno Pinto 22308986

---

# First Person Controller

## Code Architecture

- **FirstPersonController**: Main entry point, manages camera, movement, and skin.
- **FirstPersonMovement**: Handles Quake-inspired movement, jumping, and ground detection.
- **FirstPersonCamera**: Manages camera rotation, sensitivity, and network synchronization.
- **FirstPersonSkin**: Handles mesh visibility, animation, and local/remote differentiation.

## Network Diagram

![Relay Diagram](Images/RelayDiagram.png)


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
We could add interpolation to the movement, or damping to the camera.
But both just make the movement laggy and not snappy.

First, let's fix this problem:
```cs
if (Vector3.Distance(transform.position, serverPosition) > 0.1f)
```
we're checking he current position to the server position that is delayed.
In the meantime, the movement updated in the client, we shouldn't check the current position,
but the position we were at the moment.
Aditionally we will check if the velocity is the same aswell.
```cs
InputEntry inputEntry = default;
bool found = false;
int currentIndex = bufferTail;

// Iterate through valid entries to find the serverTick
for (int i = 0; i < bufferCount; i++)
{
    if (inputBuffer[currentIndex].tick == serverTick)
    {
        inputEntry = inputBuffer[currentIndex];
        found = true;
        break;
    }
    currentIndex = (currentIndex + 1) % BUFFER_SIZE;
}

if (!found)
{
    Debug.LogWarning($"Reconcile failed: No input entry found for tick {serverTick}");
    return;
}

if (Vector3.Distance(inputEntry.predictedPosition, serverPosition) > 1f)
{
    Debug.Log("We need to reconcile state!");

    // Remove acknowledged inputs
    while (bufferCount > 0 && inputBuffer[bufferTail].tick <= serverTick)
    {
        bufferTail = (bufferTail + 1) % BUFFER_SIZE;
        bufferCount--;
    }

    // Restore state from the server's position
    transform.position = serverPosition;
    rb.linearVelocity = inputEntry.velocity;

    // Replay unacknowledged inputs
    int current = bufferTail;
    for (int i = 0; i < bufferCount; i++)
    {
        ref InputEntry entry = ref inputBuffer[current];
        rb.linearVelocity = entry.velocity;

        if (entry.isJump)
            movementController.Jump();
        else
            movementController.UpdateMovement(entry.moveInput, Time.fixedDeltaTime);

        // Update predicted state after applying each input
        entry.predictedPosition = transform.position;
        entry.velocity = rb.linearVelocity;

        current = (current + 1) % BUFFER_SIZE;
    }
}
```

We will also increase the distance check from 0.1 to 1.

Now, we're not reconciling as much!
Only when there is illegal movement.

We still have a bit of jitter in the movement, and it's not reconciliation.

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

Architecture plan:
    - FirstPersonShooter will be a class that will handle if it's possible to shoot and client-side shooting.
    - ShotsManager will be a singleton that will manage the player's position over time, calculating the shots based on the time it was shot.

### Rewinding
What is rewinding?
Well, rewinding is going to a given time in the past to calculate something.
We will use this to calculate where was the client looking when they shot.

#### Gathering Information:
I opt to use a tick based buffer.
The buffer has a size to match 1 second based on the tick rate.
**All of this is Server-side only.**

Initialized like this:
```cs
private void Start()
{
    // tick rate is default 128.
    // This will set the buffer to contain 1 second of information.
    bufferSize = tickRate; 
    tickInterval = 1f / tickRate;
}
```

Now, we update the tick like this:
```cs
private void Update()
{
    if (Time.time >= nextTickTime)
    {
        RecordWorldState();
        nextTickTime = Time.time + tickInterval;
    }
}
```

Okay, now how we store that information?

We create a class to store stuff called WorldData:
```cs
public class WorldData
{
    public float Time;
    public Vector3 Position;
    public Vector2 HeadRotation;
}
```

Now we create a buffer to every client there is connected.
And initialize it:
```cs
private Dictionary<ulong, WorldData>[] buffer;

private void Start()
{
    buffer = new Dictionary<ulong, WorldData>[bufferSize];

    for (int i = 0; i < bufferSize; i++)
    {
        buffer[i] = new Dictionary<ulong, WorldData>();
    }
}
```

Now we gather all clients information and store in the buffer:
```cs
private void RecordWorldState()
{
    // Clear before setting stuff up.
    buffer[currentIndex].Clear();

    foreach (ulong clientId in cachedClientIds)
    {
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            continue;

        if (!client.PlayerObject.TryGetComponent<FirstPersonCamera>(out var camera))
            continue;

        buffer[currentIndex][clientId] = new WorldData
        {
            Time = NetworkManager.Singleton.ServerTime.TimeAsFloat,
            Position = client.PlayerObject.transform.position,
            HeadRotation = camera.networkRotation.Value
        };
    }
    
    // Make it circle the buffer.
    currentIndex = (currentIndex + 1) % bufferSize;
}
```
#### Calculating the shot
When the client press the shoot button,
we will register the time and send it to the server.

```cs
public void Shoot()
{
    float time = NetworkManager.Singleton.ServerTime.TimeAsFloat;

    ShootServerRpc(time);
}
```

In the server, we will check if it's inside the cooldown  before shooting.

```cs
[ServerRpc]
private void ShootServerRpc(float time, ServerRpcParams serverRpcParams = default)
{
    ulong senderClientId = serverRpcParams.Receive.SenderClientId;

    if (time - lastShotTime > cooldown)
    {
        ShotsManager.Instance.CalculateShoot(time, senderClientId);
        lastShotTime = time;
    }
}
```

Now, to rewinding we have 4 steps.

Step 1: Finding the best snapshot.

To get the best snapshot we will go backwards in the buffer until we find a snapshot that was taken in a time closest to the target time.

```cs
private bool TryFindBestSnapshot(float targetTime, ulong shooterId, out Dictionary<ulong, WorldData> snapshot)
{
    snapshot = null;
    int newestIndex = (currentIndex - 1 + bufferSize) % bufferSize;

    int closestIndex = -1;
    float closestDiff = float.MaxValue;

    // Search backwards through buffer
    for (int i = 0; i < bufferSize; i++)
    {
        int bufferIndex = (newestIndex - i + bufferSize) % bufferSize;
        var currentSnapshot = buffer[bufferIndex];

        // Skip if shooter data doesn't exist in this snapshot
        if (!currentSnapshot.TryGetValue(shooterId, out var shooterData))
            continue;

        float timeDiff = Mathf.Abs(shooterData.Time - targetTime);

        // Found exact match (within 1 tick tolerance)
        if (timeDiff < tickInterval * 0.5f)
        {
            closestIndex = bufferIndex;
            break;
        }

        // Found closer match
        if (timeDiff < closestDiff)
        {
            closestDiff = timeDiff;
            closestIndex = bufferIndex;
        }
    }

    if (closestIndex == -1) return false;

    snapshot = buffer[closestIndex];
    return true;
}
```

Step 2: Saving the current data.

In order to rewind and go back, we need the information of the NOW so we can come back once we're done.

```cs
private Dictionary<ulong, WorldData> CaptureWorldState()
{
    var states = new Dictionary<ulong, WorldData>();

    foreach (ulong clientId in cachedClientIds)
    {
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            continue;

        if (!client.PlayerObject.TryGetComponent<FirstPersonCamera>(out var camera))
            continue;

        states[clientId] = new WorldData
        {
            Position = client.PlayerObject.transform.position,
            HeadRotation = camera.networkRotation.Value
        };
    }

    return states;
}
```

Step 3: Rewinding.

To rewind, first, let's stop Physics from running for a bit.
Then we will set the position and head rotation from every client to the best snapshot we captured.
Then we will make the physics simulate one step to update collisions.

```cs
private void RewindWorldToSnapshot(Dictionary<ulong, WorldData> snapshot)
{
    Physics.simulationMode = SimulationMode.Script;

    foreach (ulong clientId in cachedClientIds)
    {
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            continue;

        if (!snapshot.TryGetValue(clientId, out var data))
            continue;

        var playerObj = client.PlayerObject;
        playerObj.transform.position = data.Position;

        if (playerObj.TryGetComponent<FirstPersonCamera>(out var camera))
        {
            camera.CameraTarget.rotation = Quaternion.Euler(data.HeadRotation);
        }
    }

    Physics.Simulate(tickInterval);
}
```

Now we can perform the shot:

```cs
private Vector3 ProcessShot(ulong shooterId)
{
    if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(shooterId, out var shooter))
        return default;

    if (!shooter.PlayerObject.TryGetComponent<FirstPersonCamera>(out var camera))
        return default;

    Vector3 origin = camera.CameraTarget.transform.position;
    Vector3 direction = camera.CameraTarget.forward;

    if (Physics.Raycast(origin, direction, out RaycastHit hit, 500))
    {
        if (hit.collider.TryGetComponent<NetworkObject>(out var hitObject))
        {
            ProcessHit(hitObject, shooterId);
        }
    }

    return hit.point;
}
```

Step 4: Going back to the present.

Now we just set the position and head rotation back.
And set physics to roll again.

```cs
private void RestoreWorldState(Dictionary<ulong, WorldData> originalStates)
{
    foreach (ulong clientId in cachedClientIds)
    {
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            continue;

        if (!originalStates.TryGetValue(clientId, out var data))
            continue;

        var playerObj = client.PlayerObject;
        playerObj.transform.position = data.Position;

        if (playerObj.TryGetComponent<FirstPersonCamera>(out var camera))
        {
            camera.CameraTarget.rotation = Quaternion.Euler(data.HeadRotation);
        }
    }

    Physics.simulationMode = SimulationMode.FixedUpdate;
}
```

Now we will also make the shot in the client to predict what the server will get:

```cs
public void Shoot()
{
    float time = NetworkManager.Singleton.ServerTime.TimeAsFloat;

    if (time - lastShotTime > cooldown)
    {
        lastShotTime = time;

        var cameraController = GetComponent<FirstPersonCamera>();

        Vector3 origin = cameraController.CameraTarget.transform.position;
        Vector3 direction = cameraController.CameraTarget.forward;

        if (Physics.Raycast(origin, direction, out RaycastHit hit, 500))
        {
            if (clientHitDebug != null && showDebugShots)
            {
                var hitMarker = Instantiate(clientHitDebug, hit.point, Quaternion.identity);
                Destroy(hitMarker, 2f);
            }
        }
    }
}
```

Results:<br>
Walking left, no head movement<br>
Green client bullet<br>
Red server bullet<br>
![Rewind result example](Images/RewindResult1.png)

---

# Results

I made some tests with my friend, and here's what I got:<br>

## Movement

Very laggy, my try to make the client-side prediction has something that is not working well
the movement it jittery without reconciliation and feels laggy.
maybe the server should predict the client's position to move it, but the friction would need a rework.

## Shooting

Shooting rewind is not working correctly. Shots often don't hit when it should...<br>
Maybe it's because of the client position not being the server position when the shot calculation ends.<br>
Or because it's taking too much time rewinding, making the time pass and the tick register the rewinded position.
Also, when shooting the movement glitches and lags a lot for everyone and everyone reconciles.

## Analysis

I tried to get the bytes sent and received per second from the profiler, but couldn't get it.
per tick was average 

<br>
<br>
Lastly, I think I learned a lot and looking back to the stuff I wrote, I would do stuff completly different.

---

# References

## Models
- [lofi ordinary man](https://stephrobertgames.itch.io/lofi-ordinary-man)
- [Low Poly Desert Building](https://lowpolyassets.itch.io/low-poly-desert-buildings)
- [Guns Asset Pack](https://styloo.itch.io/guns-asset-pack)
- [Free VFX image sequences and flipbooks](https://unity.com/blog/engine-platform/free-vfx-image-sequences-flipbooks)

## Movement
- [Okay, but how does airstrafing ACTUALLY work?](https://www.youtube.com/watch?v=gRqoXy-0d84&ab_channel=zweek)
by zweek on YouTube
- [Quake Source code](https://github.com/id-Software/Quake/blob/master/WinQuake/sv_user.c#L207)

## Networking
- [What goes into making a multiplayer FPS game?](https://www.youtube.com/watch?v=JOH5NEErS4Y&ab_channel=RiftDivision)
by Rift Division
- [Gabriel Gambetta's Prediction Guide](https://www.gabrielgambetta.com/client-side-prediction-server-reconciliation.html)