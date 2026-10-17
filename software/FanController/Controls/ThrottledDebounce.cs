////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// ThrottledDebounce.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;


namespace FanController.Controls;

/// <summary>
/// Throttles calls to an async action: executes immediately on first call,
/// then no more than once per <paramref name="interval"/> during rapid changes,
/// and always executes once more after the last call (debounce tail).
/// Each <see cref="Signal"/> replaces the pending state snapshot; 
/// the action is invoked with the most recent snapshot either
/// immediately (if the interval has elapsed) or after the interval, 
/// and at most once per interval.
/// </summary>
internal sealed class ThrottledDebounce<T> : IDisposable
{
    private readonly Func<T, CancellationToken, Task> _action;
    private readonly long _intervalMs;

    private CancellationTokenSource? _pendingCts;
    private CancellationToken _externalToken;
    private long _lastExecutionTick = 0;   // Environment.TickCount64
    private bool _isRunning;

    // Latest snapshot not yet applied. _hasPending distinguishes "no snapshot"
    // from a legitimately default/null snapshot value.
    private T? _pendingSnapshot;
    private bool _hasPending; 

    private readonly object _gate = new();
    private bool _disposed;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction & disposal

    /// <summary>
    /// Initializes a new instance of <see cref="ThrottledDebounce"/>
    /// by wrapping the provided raw byte span.
    /// </summary>
    /// <param name="action"></param>
    /// <param name="interval"></param>
    public ThrottledDebounce(Func<T, CancellationToken, Task> action, TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(action);
        _action = action;
        _intervalMs = (long)interval.TotalMilliseconds;
        _externalToken = CancellationToken.None;
    }


    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            OnError = null;
            CancelPending();
            ClearSnapshot();
        }
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Public API
    
    /// <summary>
    /// Updates the cancellation token used for running actions.
    /// Call this whenever the active device changes (i.e. DeviceCts changes).
    /// Does NOT cancel any currently running action — the old token does that.
    /// Any snapshot pending for the previous device is discarded.
    /// </summary>
    public void UpdateToken(CancellationToken token)
    {
        lock (_gate)
        {
            _externalToken = token;

            // Cancel any pending debounce that was scheduled with the old token context
            CancelPending();
            ClearSnapshot();
        }
    }


    /// <summary>
    /// Stores <paramref name="snapshot"/> as the desired state and applies it
    /// now or after the throttle interval, replacing any not-yet-applied snapshot.
    /// </summary>
    public void Signal(T snapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            _pendingSnapshot = snapshot;
            _hasPending = true;

            // The running loop will pick the new snapshot up after its interval wait.
            if (_isRunning) return;

            CancelPending();

            long elapsedMs = Environment.TickCount64 - _lastExecutionTick;
            if (elapsedMs >= _intervalMs)
            {
                if (_externalToken.IsCancellationRequested) return;   // nothing to do
                _isRunning = true;   // claim the loop while still under the lock
                _ = RunLoopAsync();
            }
            else
            {
                _pendingCts = new CancellationTokenSource();
                _ = ScheduleAsync(_pendingCts.Token);
            }
        }
    }


    /// <summary>
    /// Optionally subscribe to handle errors thrown by the action.
    /// </summary>
    public event Action<Exception>? OnError;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Internals

    private void CancelPending()
    {
        _pendingCts?.Cancel();
        _pendingCts?.Dispose();
        _pendingCts = null;
    }


    private void ClearSnapshot()
    {
        _pendingSnapshot = default;
        _hasPending = false;
    }


    // Takes the pending snapshot out of the slot. Must be called under _gate.
    private bool TryTakeSnapshot([MaybeNullWhen(false)] out T snapshot)
    {
        if (!_hasPending)
        {
            snapshot = default;
            return false;
        }

        snapshot = _pendingSnapshot!;
        ClearSnapshot();
        return true;
    }


    private async Task ScheduleAsync(CancellationToken pendingToken)
    {
        try
        {
            await Task.Delay((int)_intervalMs, pendingToken);
        }
        catch (OperationCanceledException) { return; }

        lock (_gate)
        {
            if (_disposed || _isRunning || !_hasPending || _externalToken.IsCancellationRequested)
                return;
            CancelPending();
            _isRunning = true;   // claim the loop atomically with the checks above
        }

        await RunLoopAsync();
    }


    // Loop-based execution: runs the action, then waits the full interval
    // before checking if another execution is needed.
    // Caller must have set _isRunning = true under _gate before invoking.
    private async Task RunLoopAsync()
    {
        try
        {
            while (true)
            {
                // Take the latest snapshot
                T snapshot;
                CancellationToken token;
                lock (_gate)
                {
                    if (_disposed || !TryTakeSnapshot(out snapshot!))
                        break;

                    token = _externalToken;   // always use the latest token
                    if (token.IsCancellationRequested) break;

                    _lastExecutionTick = Environment.TickCount64;
                }

                // Execute
                try
                {
                    await _action(snapshot, token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    if (!_disposed) OnError?.Invoke(ex);
                }

                // Wait the full interval before checking for a newer snapshot
                try
                {
                    await Task.Delay((int)_intervalMs, token);
                }
                catch (OperationCanceledException) { break; }

                // Loop continues only if Signal() stored a new snapshot meanwhile;
                // TryTakeSnapshot at the top of the loop handles that.
            }
        }
        finally
        {
            lock (_gate)
            {
                _isRunning = false;
            }
        }
    }

}

// END-OF-FILE
