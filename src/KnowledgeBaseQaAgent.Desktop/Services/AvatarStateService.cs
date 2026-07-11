using KnowledgeBaseQaAgent.Desktop.Models;

namespace KnowledgeBaseQaAgent.Desktop.Services;

public sealed class AvatarStateService
{
    private AvatarState _state = AvatarState.Idle;

    public event EventHandler<AvatarState>? StateChanged;

    public AvatarState State
    {
        get => _state;
        private set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            StateChanged?.Invoke(this, value);
        }
    }

    public void Set(AvatarState state) => State = state;
}
