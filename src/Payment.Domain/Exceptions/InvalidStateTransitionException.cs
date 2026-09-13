namespace Payment.Domain.Exceptions;

public class InvalidStateTransitionException : Exception
{
    public InvalidStateTransitionException(string entity, string currentState, string attemptedState)
        : base($"Invalid state transition for {entity}: cannot transition from '{currentState}' to '{attemptedState}'.")
    {
        Entity = entity;
        CurrentState = currentState;
        AttemptedState = attemptedState;
    }

    public string Entity { get; }
    public string CurrentState { get; }
    public string AttemptedState { get; }
}
