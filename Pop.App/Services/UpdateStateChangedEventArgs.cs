namespace Pop.App.Services;

internal sealed class UpdateStateChangedEventArgs(UpdateState state) : EventArgs
{
    public UpdateState State { get; } = state;
}
