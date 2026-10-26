using System;
using System.Text.Json.Serialization;
using Funke;

namespace Funke.InputActions;

public enum InputActionType { Button, Axis, Vector2 }

public enum InputBindingKind
{
    Key, MouseButton, GamepadButton, GamepadAxis, KeysAxis, Keys2D,
    GamepadStick, GamepadDPad, MouseDelta, MouseWheel
}

public sealed record InputBinding
{
    public InputBindingKind Kind { get; init; }
    public int Code { get; init; }
    public int Negative { get; init; }
    public int Positive { get; init; }
    public int Up { get; init; }
    public int Down { get; init; }
    public int Left { get; init; }
    public int Right { get; init; }
    public float Scale { get; init; } = 1f;
    public float Deadzone { get; init; } = 0.2f;
    public bool InvertY { get; init; }

    public static InputBinding Key(KeyCode key) => new() { Kind = InputBindingKind.Key, Code = (int)key };
    public static InputBinding Mouse(MouseButton button) => new() { Kind = InputBindingKind.MouseButton, Code = (int)button };
    public static InputBinding Button(GamepadButton button) => new() { Kind = InputBindingKind.GamepadButton, Code = (int)button };
    public static InputBinding Axis(GamepadAxis axis, float deadzone = 0.2f, float scale = 1f)
        => new() { Kind = InputBindingKind.GamepadAxis, Code = (int)axis, Deadzone = deadzone, Scale = scale };
    public static InputBinding Keys(KeyCode negative, KeyCode positive)
        => new() { Kind = InputBindingKind.KeysAxis, Negative = (int)negative, Positive = (int)positive };
    public static InputBinding Keys2D(KeyCode up, KeyCode down, KeyCode left, KeyCode right)
        => new() { Kind = InputBindingKind.Keys2D, Up = (int)up, Down = (int)down, Left = (int)left, Right = (int)right };
    public static InputBinding WASD() => Keys2D(KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D);
    public static InputBinding Arrows() => Keys2D(KeyCode.Up, KeyCode.Down, KeyCode.Left, KeyCode.Right);
    public static InputBinding LeftStick(float deadzone = 0.2f)
        => new() { Kind = InputBindingKind.GamepadStick, Code = (int)GamepadAxis.LeftX, Deadzone = deadzone };
    public static InputBinding RightStick(float deadzone = 0.2f)
        => new() { Kind = InputBindingKind.GamepadStick, Code = (int)GamepadAxis.RightX, Deadzone = deadzone };
    public static InputBinding DPad() => new() { Kind = InputBindingKind.GamepadDPad };
    public static InputBinding MouseDelta(float sensitivity = 1f, bool invertY = false)
        => new() { Kind = InputBindingKind.MouseDelta, Scale = sensitivity, InvertY = invertY };
    public static InputBinding MouseWheel(float scale = 1f)
        => new() { Kind = InputBindingKind.MouseWheel, Scale = scale };

    [JsonIgnore]
    public string DisplayName => Kind switch
    {
        InputBindingKind.Key => KeyName(Code),
        InputBindingKind.MouseButton => $"Mouse {(MouseButton)Code}",
        InputBindingKind.GamepadButton => $"Gamepad {(GamepadButton)Code}",
        InputBindingKind.GamepadAxis => $"Gamepad {(GamepadAxis)Code}",
        InputBindingKind.KeysAxis => $"{KeyName(Negative)} / {KeyName(Positive)}",
        InputBindingKind.Keys2D => $"{KeyName(Up)} / {KeyName(Down)} / {KeyName(Left)} / {KeyName(Right)}",
        InputBindingKind.GamepadStick => Code == (int)GamepadAxis.LeftX ? "Left Stick" : "Right Stick",
        InputBindingKind.GamepadDPad => "D-Pad",
        InputBindingKind.MouseDelta => "Mouse Movement",
        InputBindingKind.MouseWheel => "Mouse Wheel",
        _ => "Unknown"
    };

    internal void Validate(InputActionType type)
    {
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        if (!Enum.IsDefined(Kind)) throw new ArgumentOutOfRangeException(nameof(Kind));
        if (!float.IsFinite(Scale)) throw new ArgumentOutOfRangeException(nameof(Scale), "Scale must be finite.");
        if (!float.IsFinite(Deadzone) || Deadzone < 0f || Deadzone >= 1f)
            throw new ArgumentOutOfRangeException(nameof(Deadzone), "Deadzone must be in [0, 1).");

        bool vector = Kind is InputBindingKind.Keys2D or InputBindingKind.GamepadStick
            or InputBindingKind.GamepadDPad or InputBindingKind.MouseDelta;
        if (vector != (type == InputActionType.Vector2)
            || (type == InputActionType.Button && Kind is InputBindingKind.KeysAxis or InputBindingKind.MouseWheel))
            throw new ArgumentException($"{Kind} cannot bind a {type} action.");

        switch (Kind)
        {
            case InputBindingKind.Key:
                ValidateKey(Code);
                break;
            case InputBindingKind.MouseButton:
                if (!Enum.IsDefined((MouseButton)Code)) throw new ArgumentOutOfRangeException(nameof(Code));
                break;
            case InputBindingKind.GamepadButton:
                if (!Enum.IsDefined((GamepadButton)Code)) throw new ArgumentOutOfRangeException(nameof(Code));
                break;
            case InputBindingKind.GamepadAxis:
                if (!Enum.IsDefined((GamepadAxis)Code)) throw new ArgumentOutOfRangeException(nameof(Code));
                break;
            case InputBindingKind.KeysAxis:
                ValidateKey(Negative);
                ValidateKey(Positive);
                break;
            case InputBindingKind.Keys2D:
                ValidateKey(Up);
                ValidateKey(Down);
                ValidateKey(Left);
                ValidateKey(Right);
                break;
            case InputBindingKind.GamepadStick:
                if (Code != (int)GamepadAxis.LeftX && Code != (int)GamepadAxis.RightX)
                    throw new ArgumentOutOfRangeException(nameof(Code), "A stick must start at LeftX or RightX.");
                break;
        }
    }

    internal Vector2 Read(IInputSource source, int gamepad, bool keyboardMouseEnabled)
    {
        Vector2 value;
        switch (Kind)
        {
            case InputBindingKind.Key:
                value = new(keyboardMouseEnabled && source.GetKey((KeyCode)Code) ? 1f : 0f, 0f);
                break;
            case InputBindingKind.MouseButton:
                value = new(keyboardMouseEnabled && source.GetMouseButton((MouseButton)Code) ? 1f : 0f, 0f);
                break;
            case InputBindingKind.GamepadButton:
                value = new(source.IsGamepadConnected(gamepad) && source.GetGamepadButton((GamepadButton)Code, gamepad) ? 1f : 0f, 0f);
                break;
            case InputBindingKind.GamepadAxis:
                if (!source.IsGamepadConnected(gamepad)) return Vector2.Zero;
                float raw = source.GetGamepadAxis((GamepadAxis)Code, gamepad);
                if (!float.IsFinite(raw)) return Vector2.Zero;
                raw = Math.Clamp(raw, -1f, 1f);
                if (Code is (int)GamepadAxis.LeftTrigger or (int)GamepadAxis.RightTrigger) raw = (raw + 1f) * 0.5f;
                value = new(ApplyDeadzone(raw, Deadzone), 0f);
                break;
            case InputBindingKind.KeysAxis:
                if (!keyboardMouseEnabled) return Vector2.Zero;
                value = new(KeyValue(source, Positive) - KeyValue(source, Negative), 0f);
                break;
            case InputBindingKind.Keys2D:
                if (!keyboardMouseEnabled) return Vector2.Zero;
                value = NormalizeDigital(KeyValue(source, Right) - KeyValue(source, Left), KeyValue(source, Up) - KeyValue(source, Down));
                break;
            case InputBindingKind.GamepadStick:
                if (!source.IsGamepadConnected(gamepad)) return Vector2.Zero;
                value = RadialDeadzone(source.GetGamepadAxis((GamepadAxis)Code, gamepad),
                    -source.GetGamepadAxis((GamepadAxis)(Code + 1), gamepad), Deadzone);
                break;
            case InputBindingKind.GamepadDPad:
                if (!source.IsGamepadConnected(gamepad)) return Vector2.Zero;
                value = NormalizeDigital(ButtonValue(source, GamepadButton.DPadRight, gamepad) - ButtonValue(source, GamepadButton.DPadLeft, gamepad),
                    ButtonValue(source, GamepadButton.DPadUp, gamepad) - ButtonValue(source, GamepadButton.DPadDown, gamepad));
                break;
            case InputBindingKind.MouseDelta:
                if (!keyboardMouseEnabled) return Vector2.Zero;
                value = source.MouseDelta;
                break;
            case InputBindingKind.MouseWheel:
                if (!keyboardMouseEnabled) return Vector2.Zero;
                value = new(source.MouseWheel, 0f);
                break;
            default:
                return Vector2.Zero;
        }

        return new(Sanitize(value.X * Scale), Sanitize(value.Y * (InvertY ? -Scale : Scale)));
    }

    private static void ValidateKey(int code)
    {
        if (!Enum.IsDefined((KeyCode)code)) throw new ArgumentOutOfRangeException(nameof(code), "Unknown key code.");
    }

    private static string KeyName(int code)
        => code >= (int)KeyCode.D0 && code <= (int)KeyCode.D9 ? ((char)code).ToString() : ((KeyCode)code).ToString();

    private static float KeyValue(IInputSource source, int key) => source.GetKey((KeyCode)key) ? 1f : 0f;
    private static float ButtonValue(IInputSource source, GamepadButton button, int gamepad) => source.GetGamepadButton(button, gamepad) ? 1f : 0f;
    private static float Sanitize(float value) => float.IsFinite(value) ? value : 0f;

    private static float ApplyDeadzone(float value, float deadzone)
        => MathF.Abs(value) <= deadzone ? 0f : MathF.CopySign((MathF.Abs(value) - deadzone) / (1f - deadzone), value);

    private static Vector2 NormalizeDigital(float x, float y)
    {
        float length = MathF.Sqrt(x * x + y * y);
        return length > 1f ? new(x / length, y / length) : new(x, y);
    }

    private static Vector2 RadialDeadzone(float x, float y, float deadzone)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y)) return Vector2.Zero;
        x = Math.Clamp(x, -1f, 1f);
        y = Math.Clamp(y, -1f, 1f);
        float length = MathF.Sqrt(x * x + y * y);
        if (length <= deadzone) return Vector2.Zero;
        float scale = Math.Min(1f, (length - deadzone) / (1f - deadzone)) / length;
        return new(x * scale, y * scale);
    }
}
