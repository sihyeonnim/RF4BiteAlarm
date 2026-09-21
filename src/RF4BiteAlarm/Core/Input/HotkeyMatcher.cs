using RF4Overlay.Core.Features;

namespace RF4Overlay.Core.Input;

public sealed record KeyInput(byte VirtualKey, bool IsDown, bool IsInjected, TimeSpan Timestamp);
public static class InputPolicy
{
    public static bool IsPhysical(bool isInjected) => !isInjected;
    public static bool IsModifier(byte key) => key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5;
}

/// <summary>Thread-safe physical key matcher. Duplicate and prefix bindings are rejected.</summary>
public sealed class HotkeyMatcher
{
    private readonly object _sync = new();
    private readonly HashSet<byte> _pressed = [];
    private readonly List<(KeyStroke Stroke, TimeSpan Time)> _history = [];
    private HotkeyBinding[] _bindings = [];
    public void SetBindings(IEnumerable<HotkeyBinding> bindings)
    {
        var copy = bindings.ToArray();
        for (var i = 0; i < copy.Length; i++)
        for (var j = i + 1; j < copy.Length; j++)
        {
            var length = Math.Min(copy[i].Sequence.Count, copy[j].Sequence.Count);
            if (copy[i].Sequence.Take(length).SequenceEqual(copy[j].Sequence.Take(length)))
                throw new ArgumentException("중복 또는 prefix가 겹치는 단축키입니다.");
        }
        lock (_sync) { _bindings = copy; _history.Clear(); _pressed.Clear(); }
    }
    public void Reset() { lock (_sync) { _history.Clear(); _pressed.Clear(); } }
    public FeatureCommand? Process(KeyInput input)
    {
        lock (_sync)
        {
            if (!InputPolicy.IsPhysical(input.IsInjected)) return null;
            if (!input.IsDown) { _pressed.Remove(input.VirtualKey); return null; }
            if (!_pressed.Add(input.VirtualKey) || InputPolicy.IsModifier(input.VirtualKey)) return null;
            var modifiers = KeyModifiers.None;
            if (_pressed.Overlaps([0x11, 0xA2, 0xA3])) modifiers |= KeyModifiers.Control;
            if (_pressed.Overlaps([0x10, 0xA0, 0xA1])) modifiers |= KeyModifiers.Shift;
            if (_pressed.Overlaps([0x12, 0xA4, 0xA5])) modifiers |= KeyModifiers.Alt;
            if (_pressed.Overlaps([0x5B, 0x5C])) modifiers |= KeyModifiers.Windows;
            if (_history.Count > 0 && input.Timestamp < _history[^1].Time) _history.Clear();
            _history.Add((new(input.VirtualKey, modifiers), input.Timestamp));
            var limit = _bindings.Length == 0 ? 1 : _bindings.Max(b => b.Sequence.Count);
            while (_history.Count > limit) _history.RemoveAt(0);
            // Longest matching suffix wins for non-prefix overlaps. A match consumes the history.
            foreach (var binding in _bindings.OrderByDescending(b => b.Sequence.Count))
            {
                if (_history.Count < binding.Sequence.Count) continue;
                var suffix = _history.TakeLast(binding.Sequence.Count).ToArray();
                if (!suffix.Select(s => s.Stroke).SequenceEqual(binding.Sequence)) continue;
                if (suffix.Zip(suffix.Skip(1), (a, b) => b.Time - a.Time).Any(g => g > binding.MaximumGap)) continue;
                _history.Clear();
                return binding.Command;
            }
            return null;
        }
    }
}
