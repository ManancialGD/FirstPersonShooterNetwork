using UnityEngine.InputSystem;

public static class InputActionExtensions
{
    public static bool TryReadValue<TValue>(this InputAction action, out TValue value) where TValue : struct
    {
        try
        {
            value = action.ReadValue<TValue>();
            return true;
        }
        catch
        {
            value = default;
            return false;
        }
    }
}
