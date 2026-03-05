using System.Diagnostics;
using GlassToKey;

namespace GlassToKey.Linux.Runtime;

internal sealed class LinuxRuntimeDiagnosticsMonitor
{
    private static readonly long HeartbeatIntervalTicks = SecondsToTicks(5.0);
    private static readonly long StallThresholdTicks = SecondsToTicks(1.5);
    private static readonly long StallLogThrottleTicks = SecondsToTicks(2.0);
    private static readonly long TouchWithoutDispatchThresholdTicks = SecondsToTicks(1.2);

    private readonly string _source;
    private readonly Action<string>? _logger;
    private TouchProcessorRuntimeSnapshot _lastSnapshot;
    private bool _hasSnapshot;
    private long _lastHeartbeatTicks;
    private long _stallSinceTicks;
    private long _lastStallLogTicks;
    private long _touchWithoutDispatchSinceTicks;
    private long _lastTouchWithoutDispatchLogTicks;
    private long _lastHoverOnlyLogTicks;
    private long _lastFrameTicksLeft;
    private long _lastFrameTicksRight;

    public LinuxRuntimeDiagnosticsMonitor(string source, Action<string>? logger = null)
    {
        _source = string.IsNullOrWhiteSpace(source) ? "runtime" : source;
        _logger = logger;
    }

    public void Observe(in TouchProcessorRuntimeSnapshot snapshot)
    {
        long nowTicks = Stopwatch.GetTimestamp();
        if (!_hasSnapshot)
        {
            _lastSnapshot = snapshot;
            _hasSnapshot = true;
            _lastHeartbeatTicks = nowTicks;
            Emit($"diag baseline {DescribeSnapshot(snapshot)}");
            return;
        }

        TouchProcessorRuntimeSnapshot previous = _lastSnapshot;

        if (snapshot.TypingEnabled != previous.TypingEnabled)
        {
            Emit(
                $"typing {(snapshot.TypingEnabled ? "enabled" : "disabled")} intent={snapshot.IntentMode} contacts={snapshot.ContactCount} queue={snapshot.DispatchQueueCount} enqueued={snapshot.DispatchEnqueued} sendFailures={snapshot.DispatcherSendFailures}");
        }

        if (snapshot.KeyboardModeEnabled != previous.KeyboardModeEnabled)
        {
            Emit($"keyboard mode {(snapshot.KeyboardModeEnabled ? "enabled" : "disabled")} layer={snapshot.ActiveLayer} typing={(snapshot.TypingEnabled ? "on" : "off")}");
        }

        if (snapshot.ActiveLayer != previous.ActiveLayer)
        {
            Emit($"active layer changed {previous.ActiveLayer}->{snapshot.ActiveLayer} momentary={(snapshot.MomentaryLayerActive ? "on" : "off")} keyboardMode={(snapshot.KeyboardModeEnabled ? "on" : "off")} rawTips=({snapshot.LastRawLeftContacts},{snapshot.LastRawRightContacts})");
        }

        if (snapshot.ChordShiftLeft != previous.ChordShiftLeft || snapshot.ChordShiftRight != previous.ChordShiftRight)
        {
            Emit($"chord-shift changed left={(snapshot.ChordShiftLeft ? "on" : "off")} right={(snapshot.ChordShiftRight ? "on" : "off")}");
        }

        long ringSuppressedDelta = PositiveDelta(snapshot.DispatchSuppressedRingFull, previous.DispatchSuppressedRingFull);
        long dispatchQueueDropDelta = PositiveDelta(snapshot.DispatchQueueDrops, previous.DispatchQueueDrops);
        long frameQueueDropDelta = PositiveDelta(snapshot.QueueDrops, previous.QueueDrops);
        if (ringSuppressedDelta > 0 || dispatchQueueDropDelta > 0 || frameQueueDropDelta > 0)
        {
            Emit(
                $"backpressure ringSuppressed+{ringSuppressedDelta} dispatchQueueDrops+{dispatchQueueDropDelta} frameQueueDrops+{frameQueueDropDelta} queue={snapshot.DispatchQueueCount} pumpAlive={snapshot.DispatchPumpAlive}");
        }

        long sendFailureDelta = PositiveDelta(snapshot.DispatcherSendFailures, previous.DispatcherSendFailures);
        if (sendFailureDelta > 0)
        {
            Emit(
                $"uinput send failures +{sendFailureDelta} total={snapshot.DispatcherSendFailures} lastError={Trim(snapshot.DispatcherLastError)}");
        }

        long releaseDroppedDelta = PositiveDelta(snapshot.ReleaseDroppedTotal, previous.ReleaseDroppedTotal);
        long releaseDroppedGestureDelta = PositiveDelta(snapshot.ReleaseDroppedGesturePriority, previous.ReleaseDroppedGesturePriority);
        if (releaseDroppedDelta > 0 || releaseDroppedGestureDelta > 0)
        {
            Emit(
                $"release-dropped +{releaseDroppedDelta} gesture+{releaseDroppedGestureDelta} total={snapshot.ReleaseDroppedTotal} gestureTotal={snapshot.ReleaseDroppedGesturePriority} lastReason={Trim(snapshot.LastReleaseDroppedReason)} gesturePriority=({(snapshot.GesturePriorityLeft ? "on" : "off")},{(snapshot.GesturePriorityRight ? "on" : "off")}) states=({snapshot.TouchStateCount},{snapshot.IntentTouchStateCount})");
        }

        if (snapshot.DispatchPumpLastFaultTicks != previous.DispatchPumpLastFaultTicks && snapshot.DispatchPumpLastFaultTicks > 0)
        {
            Emit($"dispatch pump fault: {Trim(snapshot.DispatchPumpLastFault)}");
        }

        bool pumpAdvanced = snapshot.DispatchPumpDispatchCalls > previous.DispatchPumpDispatchCalls;
        bool likelyStalled = snapshot.TypingEnabled &&
                             snapshot.DispatchQueueCount > 0 &&
                             snapshot.DispatchEnqueued > previous.DispatchEnqueued &&
                             !pumpAdvanced;
        if (likelyStalled)
        {
            if (_stallSinceTicks == 0)
            {
                _stallSinceTicks = nowTicks;
            }

            if (nowTicks - _stallSinceTicks >= StallThresholdTicks &&
                nowTicks - _lastStallLogTicks >= StallLogThrottleTicks)
            {
                _lastStallLogTicks = nowTicks;
                Emit(
                    $"dispatch stall suspected typing=on queue={snapshot.DispatchQueueCount} enqueued={snapshot.DispatchEnqueued} pumpDispatch={snapshot.DispatchPumpDispatchCalls} pumpTick={snapshot.DispatchPumpTickCalls} sendFailures={snapshot.DispatcherSendFailures}");
            }
        }
        else
        {
            _stallSinceTicks = 0;
        }

        bool touchPresent = snapshot.LastRawLeftContacts > 0 ||
                            snapshot.LastRawRightContacts > 0 ||
                            snapshot.LastFrameLeftContacts > 0 ||
                            snapshot.LastFrameRightContacts > 0;
        bool onKeyPresent = snapshot.LastOnKeyLeftContacts > 0 || snapshot.LastOnKeyRightContacts > 0;
        bool hoverOnlyContacts = (snapshot.LastFrameLeftContacts > snapshot.LastRawLeftContacts) ||
                                 (snapshot.LastFrameRightContacts > snapshot.LastRawRightContacts);
        if (hoverOnlyContacts && nowTicks - _lastHoverOnlyLogTicks >= StallLogThrottleTicks)
        {
            _lastHoverOnlyLogTicks = nowTicks;
            Emit(
                $"hover-only contacts frameContacts=({snapshot.LastFrameLeftContacts},{snapshot.LastFrameRightContacts}) rawTips=({snapshot.LastRawLeftContacts},{snapshot.LastRawRightContacts}) intent={snapshot.IntentMode} enqueued={snapshot.DispatchEnqueued}");
        }

        bool dispatchAdvanced = snapshot.DispatchEnqueued > previous.DispatchEnqueued;
        if (snapshot.TypingEnabled && touchPresent && !dispatchAdvanced)
        {
            if (_touchWithoutDispatchSinceTicks == 0)
            {
                _touchWithoutDispatchSinceTicks = nowTicks;
            }

            if (nowTicks - _touchWithoutDispatchSinceTicks >= TouchWithoutDispatchThresholdTicks &&
                nowTicks - _lastTouchWithoutDispatchLogTicks >= StallLogThrottleTicks)
            {
                _lastTouchWithoutDispatchLogTicks = nowTicks;
                Emit(
                    $"touch-without-dispatch typing=on keyboardMode={(snapshot.KeyboardModeEnabled ? "on" : "off")} layer={snapshot.ActiveLayer} momentary={(snapshot.MomentaryLayerActive ? "on" : "off")} intent={snapshot.IntentMode} frameContacts=({snapshot.LastFrameLeftContacts},{snapshot.LastFrameRightContacts}) rawTips=({snapshot.LastRawLeftContacts},{snapshot.LastRawRightContacts}) onKey=({snapshot.LastOnKeyLeftContacts},{snapshot.LastOnKeyRightContacts}) chordSuppressed=({(snapshot.LastChordSuppressedLeft ? "on" : "off")},{(snapshot.LastChordSuppressedRight ? "on" : "off")}) chordShift=({(snapshot.ChordShiftLeft ? "on" : "off")},{(snapshot.ChordShiftRight ? "on" : "off")}) gesturePriority=({(snapshot.GesturePriorityLeft ? "on" : "off")},{(snapshot.GesturePriorityRight ? "on" : "off")}) states=({snapshot.TouchStateCount},{snapshot.IntentTouchStateCount}) staleExp={snapshot.StaleTouchExpirations} releaseDrops={snapshot.ReleaseDroppedTotal}/{snapshot.ReleaseDroppedGesturePriority} enqueued={snapshot.DispatchEnqueued}");
            }
        }
        else
        {
            _touchWithoutDispatchSinceTicks = 0;
        }

        if (snapshot.TypingEnabled && touchPresent && !onKeyPresent && nowTicks - _lastHoverOnlyLogTicks >= StallLogThrottleTicks)
        {
            _lastHoverOnlyLogTicks = nowTicks;
            Emit(
                $"touch-off-key typing=on intent={snapshot.IntentMode} frameContacts=({snapshot.LastFrameLeftContacts},{snapshot.LastFrameRightContacts}) rawTips=({snapshot.LastRawLeftContacts},{snapshot.LastRawRightContacts}) onKey=({snapshot.LastOnKeyLeftContacts},{snapshot.LastOnKeyRightContacts}) chordSuppressed=({(snapshot.LastChordSuppressedLeft ? "on" : "off")},{(snapshot.LastChordSuppressedRight ? "on" : "off")})");
        }

        if (nowTicks - _lastHeartbeatTicks >= HeartbeatIntervalTicks)
        {
            _lastHeartbeatTicks = nowTicks;
            Emit($"diag heartbeat {DescribeSnapshot(snapshot)}");
        }

        _lastSnapshot = snapshot;
    }

    public void ObserveEngineDiagnostics(ReadOnlySpan<TouchProcessorTraceEvent> events)
    {
        for (int i = 0; i < events.Length; i++)
        {
            ref readonly TouchProcessorTraceEvent evt = ref events[i];
            switch (evt.Kind)
            {
                case TouchProcessorTraceEventKind.Frame:
                {
                    if (evt.Side == TrackpadSide.Left)
                    {
                        _lastFrameTicksLeft = evt.TimestampTicks;
                    }
                    else
                    {
                        _lastFrameTicksRight = evt.TimestampTicks;
                    }

                    Emit(
                        $"trace frame ts={evt.TimestampTicks} side={evt.Side} contacts={evt.ContactCount} tips={evt.TipContactCount} raw=({evt.LeftRawContacts},{evt.RightRawContacts}) typing={(evt.TypingEnabled ? "on" : "off")} intent={evt.IntentMode} reason={Trim(evt.Reason)}");
                    break;
                }
                case TouchProcessorTraceEventKind.DispatchEnqueued:
                {
                    long sourceFrameTicks = evt.Side == TrackpadSide.Left ? _lastFrameTicksLeft : _lastFrameTicksRight;
                    long deltaTicks = sourceFrameTicks > 0 ? evt.TimestampTicks - sourceFrameTicks : 0;
                    Emit(
                        $"trace dispatch ts={evt.TimestampTicks} side={evt.Side} kind={evt.DispatchKind} label={Trim(evt.DispatchLabel)} vk={evt.VirtualKey} mouse={evt.MouseButton} frameTs={sourceFrameTicks} frameDeltaTicks={deltaTicks}");
                    break;
                }
                case TouchProcessorTraceEventKind.DispatchSuppressed:
                {
                    long sourceFrameTicks = evt.Side == TrackpadSide.Left ? _lastFrameTicksLeft : _lastFrameTicksRight;
                    long deltaTicks = sourceFrameTicks > 0 ? evt.TimestampTicks - sourceFrameTicks : 0;
                    Emit(
                        $"trace suppressed ts={evt.TimestampTicks} side={evt.Side} kind={evt.DispatchKind} reason={Trim(evt.Reason)} label={Trim(evt.DispatchLabel)} vk={evt.VirtualKey} mouse={evt.MouseButton} frameTs={sourceFrameTicks} frameDeltaTicks={deltaTicks} typing={(evt.TypingEnabled ? "on" : "off")} intent={evt.IntentMode}");
                    break;
                }
                case TouchProcessorTraceEventKind.IntentTransition:
                {
                    Emit(
                        $"trace intent ts={evt.TimestampTicks} intent={evt.IntentMode} reason={Trim(evt.Reason)} raw=({evt.LeftRawContacts},{evt.RightRawContacts}) contacts={evt.ContactCount}");
                    break;
                }
                case TouchProcessorTraceEventKind.ReleaseDropped:
                {
                    Emit(
                        $"trace release-dropped ts={evt.TimestampTicks} side={evt.Side} reason={Trim(evt.Reason)} label={Trim(evt.DispatchLabel)} vk={evt.VirtualKey} mouse={evt.MouseButton} intent={evt.IntentMode} typing={(evt.TypingEnabled ? "on" : "off")}");
                    break;
                }
                case TouchProcessorTraceEventKind.ChordShiftState:
                {
                    Emit(
                        $"trace chord-shift ts={evt.TimestampTicks} reason={Trim(evt.Reason)} raw=({evt.LeftRawContacts},{evt.RightRawContacts}) intent={evt.IntentMode}");
                    break;
                }
                case TouchProcessorTraceEventKind.TypingToggle:
                {
                    Emit(
                        $"trace typing-toggle ts={evt.TimestampTicks} reason={Trim(evt.Reason)} typing={(evt.TypingEnabled ? "on" : "off")} intent={evt.IntentMode}");
                    break;
                }
                default:
                    break;
            }
        }
    }

    public void Reset()
    {
        _hasSnapshot = false;
        _stallSinceTicks = 0;
        _lastStallLogTicks = 0;
        _lastHeartbeatTicks = 0;
        _touchWithoutDispatchSinceTicks = 0;
        _lastTouchWithoutDispatchLogTicks = 0;
        _lastHoverOnlyLogTicks = 0;
        _lastFrameTicksLeft = 0;
        _lastFrameTicksRight = 0;
    }

    public void EmitLifecycle(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Emit(message);
    }

    private void Emit(string message)
    {
        _logger?.Invoke(message);
        LinuxRuntimeDiagnosticsLog.Write(_source, message);
    }

    private static string DescribeSnapshot(in TouchProcessorRuntimeSnapshot snapshot)
    {
        return $"typing={(snapshot.TypingEnabled ? "on" : "off")} keyboardMode={(snapshot.KeyboardModeEnabled ? "on" : "off")} layer={snapshot.ActiveLayer} momentary={(snapshot.MomentaryLayerActive ? "on" : "off")} intent={snapshot.IntentMode} contacts={snapshot.ContactCount} frameContacts=({snapshot.LastFrameLeftContacts},{snapshot.LastFrameRightContacts}) rawTips=({snapshot.LastRawLeftContacts},{snapshot.LastRawRightContacts}) onKey=({snapshot.LastOnKeyLeftContacts},{snapshot.LastOnKeyRightContacts}) chordSuppressed=({(snapshot.LastChordSuppressedLeft ? "on" : "off")},{(snapshot.LastChordSuppressedRight ? "on" : "off")}) chordShift=({(snapshot.ChordShiftLeft ? "on" : "off")},{(snapshot.ChordShiftRight ? "on" : "off")}) gesturePriority=({(snapshot.GesturePriorityLeft ? "on" : "off")},{(snapshot.GesturePriorityRight ? "on" : "off")}) states=({snapshot.TouchStateCount},{snapshot.IntentTouchStateCount}) frames={snapshot.FramesProcessed} staleExp={snapshot.StaleTouchExpirations} releaseDrops={snapshot.ReleaseDroppedTotal}/{snapshot.ReleaseDroppedGesturePriority} enqueued={snapshot.DispatchEnqueued} queue={snapshot.DispatchQueueCount} queueDrops={snapshot.DispatchQueueDrops} ringSuppressed={snapshot.DispatchSuppressedRingFull} pumpAlive={snapshot.DispatchPumpAlive} pumpDispatch={snapshot.DispatchPumpDispatchCalls} sendFailures={snapshot.DispatcherSendFailures}";
    }

    private static long PositiveDelta(long current, long previous)
    {
        return current > previous ? current - previous : 0;
    }

    private static long SecondsToTicks(double seconds)
    {
        return (long)Math.Round(seconds * Stopwatch.Frequency);
    }

    private static string Trim(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(none)";
        }

        string trimmed = text.Trim();
        return trimmed.Length <= 220 ? trimmed : $"{trimmed[..220]}...";
    }
}
