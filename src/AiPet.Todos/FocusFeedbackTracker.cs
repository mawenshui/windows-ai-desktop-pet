namespace AiPet.Todos;

public enum FocusFeedbackKind { None, Started, Paused, Resumed }

/// <summary>Emits one user feedback event per persisted focus status transition.</summary>
public sealed class FocusFeedbackTracker
{
    private bool _initialized;
    private Guid? _sessionId;
    private FocusSessionStatus? _status;

    public FocusFeedbackKind Observe(FocusSessionSnapshot snapshot)
    {
        var session = snapshot.Session;
        var result = FocusFeedbackKind.None;
        if (_initialized && session is not null)
        {
            if (session.Status == FocusSessionStatus.Running && _sessionId != session.Id)
                result = FocusFeedbackKind.Started;
            else if (_sessionId == session.Id && session.Status == FocusSessionStatus.Running && _status == FocusSessionStatus.Paused)
                result = FocusFeedbackKind.Resumed;
            else if (_sessionId == session.Id && session.Status == FocusSessionStatus.Paused && _status == FocusSessionStatus.Running)
                result = FocusFeedbackKind.Paused;
        }
        _initialized = true;
        _sessionId = session?.Id;
        _status = session?.Status;
        return result;
    }
}
