# Project First Person Shooter Network

Author : Breno Pinto 22308986

---
# First Person Controller

## Code Architecture

- **FirstPersonController**: Main entry point, manages camera, movement, and skin.
- **FirstPersonMovement**: Handles Quake-inspired movement, jumping, and ground detection.
- **FirstPersonCamera**: Manages camera rotation, sensitivity, and network synchronization.
- **FirstPersonSkin**: Handles mesh visibility, animation, and local/remote differentiation.

## Movement

Movement is inspired by Quake, with air-strafing and bunny hopping.  
See [Quake Source code](https://github.com/id-Software/Quake/blob/master/WinQuake/sv_user.c#L207) for reference.

### Plan
Movement should be server sided with client side prediction.
Also, this will come with an AntiCheat, checking if the movement is checking based on the input.

### Client side prediction
Not implemented yet.

### AntiCheat
Not implemented yet.

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