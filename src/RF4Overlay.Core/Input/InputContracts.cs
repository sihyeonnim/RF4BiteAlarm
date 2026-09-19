using RF4Overlay.Core.Features;

namespace RF4Overlay.Core.Input;

[Flags]
public enum KeyModifiers { None = 0, Control = 1, Alt = 2, Shift = 4, Windows = 8 }

/// <summary>Windows virtual-key code plus required modifiers; no native API dependency.</summary>
public readonly record struct KeyStroke(byte VirtualKey, KeyModifiers Modifiers = KeyModifiers.None);

public enum AutomationInputKind { MouseRight, KeyboardKey }

/// <summary>A single input that automation may hold. Keyboard input never includes modifiers.</summary>
public readonly record struct AutomationInput(AutomationInputKind Kind, byte VirtualKey = 0)
{
    public static AutomationInput MouseRight => new(AutomationInputKind.MouseRight);
    public static AutomationInput Keyboard(byte virtualKey) => new(AutomationInputKind.KeyboardKey, virtualKey);

    public void Validate()
    {
        if (!Enum.IsDefined(Kind) ||
            Kind == AutomationInputKind.MouseRight && VirtualKey != 0 ||
            Kind == AutomationInputKind.KeyboardKey &&
            (VirtualKey is 0 or 255 || InputPolicy.IsModifier(VirtualKey)))
            throw new ArgumentException("자동 입력은 우클릭 또는 modifier가 아닌 단일 키여야 합니다.");
    }
}

/// <summary>Ordered key presses. Repeated strokes represent repeated presses, not OS auto-repeat.</summary>
public sealed class HotkeyBinding
{
    public IReadOnlyList<KeyStroke> Sequence { get; }
    public TimeSpan MaximumGap { get; }
    public FeatureCommand Command { get; }

    public HotkeyBinding(IEnumerable<KeyStroke> sequence, TimeSpan maximumGap, FeatureCommand command)
    {
        var strokes = sequence.ToArray();
        if (strokes.Length == 0 || strokes.Any(stroke => stroke.VirtualKey is 0 or 255 || InputPolicy.IsModifier(stroke.VirtualKey) ||
            (stroke.Modifiers & ~(KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Windows)) != 0))
            throw new ArgumentException("At least one valid key is required.", nameof(sequence));
        if (maximumGap <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maximumGap));
        Sequence = Array.AsReadOnly(strokes);
        MaximumGap = maximumGap;
        Command = command;
    }
}

public interface IGlobalHotkeyService : IAsyncDisposable
{
    // Implementations route recognized bindings through FeatureCommandDispatcher.
    void SetBindings(IEnumerable<HotkeyBinding> bindings);
    bool Suspended { get; set; }
}

public sealed record UserInput(DateTimeOffset Timestamp);
public enum MouseButton { Left }
public sealed record MouseButtonInput(MouseButton Button, bool IsDown, int X, int Y, TimeSpan Timestamp);

/// <summary>Physical user activity, excluding input injected by this application.</summary>
public interface IUserInputSource
{
    event EventHandler<UserInput>? InputReceived;
}

/// <summary>Physical mouse button events, excluding input injected by this application.</summary>
public interface IMouseInputSource
{
    event EventHandler<MouseButtonInput>? MouseButtonReceived;
    event EventHandler? InputReset;
}

/// <summary>Separate from user observation. Implementations must release held input when cancelled.</summary>
public interface IInputAutomation
{
    Task HoldAsync(AutomationInput input, TimeSpan duration, CancellationToken cancellationToken);
}

public interface ILeftButtonHoldAutomation
{
    ValueTask<IAsyncDisposable> HoldLeftButtonAsync(bool withShift, CancellationToken cancellationToken);
}
