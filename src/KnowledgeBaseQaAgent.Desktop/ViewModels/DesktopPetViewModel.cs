using CommunityToolkit.Mvvm.ComponentModel;
using KnowledgeBaseQaAgent.Desktop.Models;
using KnowledgeBaseQaAgent.Desktop.Services;
using System.Windows;
using System.Windows.Threading;

namespace KnowledgeBaseQaAgent.Desktop.ViewModels;

public sealed partial class DesktopPetViewModel : ObservableObject
{
    private readonly AvatarStateService _avatar;
    private AppSettings _settings;
    private readonly DispatcherTimer _frameTimer = new();
    private IReadOnlyList<string> _frames = [];
    private int _frameIndex;

    [ObservableProperty]
    private AvatarState state = AvatarState.Idle;

    [ObservableProperty]
    private string statusText = "";

    [ObservableProperty]
    private string petHintText = "";

    [ObservableProperty]
    private string avatarImagePath = "";

    [ObservableProperty]
    private Visibility customAvatarVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private Visibility builtInAvatarVisibility = Visibility.Visible;

    public DesktopPetViewModel(AvatarStateService avatar, AppSettings settings)
    {
        _avatar = avatar;
        _settings = settings;
        _frameTimer.Tick += (_, _) => AdvanceFrame();
        ApplySettings(settings);
        _avatar.StateChanged += (_, newState) =>
        {
            State = newState;
            StatusText = FormatStatusText(newState);
        };
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        PetHintText = settings.PetHintText;
        StatusText = FormatStatusText(State);
        ReloadAvatarAssets(settings);
    }

    private void ReloadAvatarAssets(AppSettings settings)
    {
        _frameTimer.Stop();
        _frames = LoadFrames(settings.PetFramesDirectory);
        _frameIndex = 0;

        if (_frames.Count > 0)
        {
            AvatarImagePath = _frames[0];
            _frameTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(120, settings.PetFrameIntervalMs));
            _frameTimer.Start();
        }
        else if (File.Exists(ExpandPath(settings.PetImagePath)))
        {
            AvatarImagePath = ExpandPath(settings.PetImagePath);
        }
        else
        {
            AvatarImagePath = "";
        }

        CustomAvatarVisibility = string.IsNullOrWhiteSpace(AvatarImagePath) ? Visibility.Collapsed : Visibility.Visible;
        BuiltInAvatarVisibility = string.IsNullOrWhiteSpace(AvatarImagePath) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AdvanceFrame()
    {
        if (_frames.Count == 0)
        {
            return;
        }

        _frameIndex = (_frameIndex + 1) % _frames.Count;
        AvatarImagePath = _frames[_frameIndex];
    }

    private static IReadOnlyList<string> LoadFrames(string directory)
    {
        var expanded = ExpandPath(directory);
        if (!Directory.Exists(expanded))
        {
            return [];
        }

        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".bmp"
        };

        return Directory.EnumerateFiles(expanded)
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ExpandPath(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path ?? "").Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(expanded) || Path.IsPathRooted(expanded))
        {
            return expanded;
        }

        return Path.Combine(AppContext.BaseDirectory, expanded);
    }

    private string FormatStatusText(AvatarState state) =>
        state switch
        {
            AvatarState.Listening => _settings.ListeningStatusText,
            AvatarState.Thinking => _settings.ThinkingStatusText,
            AvatarState.Speaking => _settings.SpeakingStatusText,
            AvatarState.Error => _settings.ErrorStatusText,
            _ => _settings.IdleStatusText
        };
}
