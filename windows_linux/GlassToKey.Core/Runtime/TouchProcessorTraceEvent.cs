namespace GlassToKey;

public enum TouchProcessorTraceEventKind : byte
{
    Other = 0,
    Frame = 1,
    DispatchEnqueued = 2,
    DispatchSuppressed = 3,
    TypingToggle = 4,
    FiveFingerState = 5,
    ChordShiftState = 6,
    IntentTransition = 7,
    ReleaseDropped = 8
}

public readonly record struct TouchProcessorTraceEvent(
    long TimestampTicks,
    TouchProcessorTraceEventKind Kind,
    TrackpadSide Side,
    string IntentMode,
    DispatchEventKind DispatchKind,
    ushort VirtualKey,
    DispatchMouseButton MouseButton,
    bool TypingEnabled,
    int ContactCount,
    int TipContactCount,
    int LeftRawContacts,
    int RightRawContacts,
    string DispatchLabel,
    string Reason);
