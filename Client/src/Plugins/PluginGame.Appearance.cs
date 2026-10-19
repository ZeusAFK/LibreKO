using Godot;

namespace LibreKO.Plugins;

public enum AppearancePart
{
    Race,
    Face,
    Hair,
}

public readonly record struct AppearanceRaceChoice(string Race, string Class, bool Current);

public interface IGameAppearance
{
    bool IsOpen { get; }
    string CharacterName { get; }
    Texture2D? Preview { get; }
    string RaceName { get; }
    int Face { get; }
    int HairStyle { get; }
    Color HairColour { get; }
    bool CanChangeRace { get; }
    IReadOnlyList<AppearanceRaceChoice> RaceChoices { get; }
    bool Busy { get; }

    void Step(AppearancePart part, int delta);
    void Turn(float degrees);
    void ToggleZoom();
    void SetHairColour(Color colour);
    void OpenHairColour();
    void Confirm();
    void Cancel();

    event Action? Changed;
}

public sealed partial class PluginGame
{
    public IGameAppearance BeautyShop => beautyShop;
    public IGameAppearance GenderChange => genderChange;

    private readonly AppearanceProxy beautyShop = new();
    private readonly AppearanceProxy genderChange = new();

    internal void AttachAppearance(IGameAppearance beautyShopSource, IGameAppearance genderChangeSource)
    {
        beautyShop.Source = beautyShopSource;
        genderChange.Source = genderChangeSource;
        RaiseBeautyShop();
        RaiseGenderChange();
    }

    internal void DetachAppearance()
    {
        beautyShop.Source = null;
        genderChange.Source = null;
    }

    internal void RaiseBeautyShop() => beautyShop.Raise();
    internal void RaiseGenderChange() => genderChange.Raise();

    private sealed class AppearanceProxy : IGameAppearance
    {
        public IGameAppearance? Source;
        public event Action? Changed;
        internal void Raise() => Changed?.Invoke();

        public bool IsOpen => Source?.IsOpen ?? false;
        public string CharacterName => Source?.CharacterName ?? "";
        public Texture2D? Preview => Source?.Preview;
        public string RaceName => Source?.RaceName ?? "";
        public int Face => Source?.Face ?? 0;
        public int HairStyle => Source?.HairStyle ?? 0;
        public Color HairColour => Source?.HairColour ?? Colors.White;
        public bool CanChangeRace => Source?.CanChangeRace ?? false;
        public IReadOnlyList<AppearanceRaceChoice> RaceChoices => Source?.RaceChoices ?? Array.Empty<AppearanceRaceChoice>();
        public bool Busy => Source?.Busy ?? false;
        public void Step(AppearancePart part, int delta) => Source?.Step(part, delta);
        public void Turn(float degrees) => Source?.Turn(degrees);
        public void ToggleZoom() => Source?.ToggleZoom();
        public void SetHairColour(Color colour) => Source?.SetHairColour(colour);
        public void OpenHairColour() => Source?.OpenHairColour();
        public void Confirm() => Source?.Confirm();
        public void Cancel() => Source?.Cancel();
    }
}
