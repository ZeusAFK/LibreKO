using Godot;

namespace LibreKO.Features.PluginBridge;

internal sealed class PluginAppearanceBridge(bool gender, GenderChangeFeature genderChange, ChangeHairFeature changeHair, Node root, SelfSession self) : IGameAppearance
{
    event Action? IGameAppearance.Changed { add { } remove { } }

    private LookEditor Editor => gender ? genderChange.Editor : changeHair.BeautyEditor;

    private LookPreview Look => gender ? genderChange.Preview : changeHair.BeautyPreview;

    public bool IsOpen => gender ? genderChange.IsOpen : changeHair.IsOpen;

    public string CharacterName => self.LastEnter.Name;

    public Texture2D? Preview => IsOpen ? Look.Texture : null;

    public string RaceName => StarterStats.RaceName(Editor.Race);

    public int Face => Editor.Face;

    public int HairStyle => Editor.HairStyle;

    public Color HairColour => Editor.HairColour;

    public bool CanChangeRace => gender;

    public IReadOnlyList<AppearanceRaceChoice> RaceChoices => gender ? GenderRaceChoices() : Array.Empty<AppearanceRaceChoice>();

    public bool Busy => gender ? genderChange.InFlight : changeHair.BeautyInFlight;

    public void Step(AppearancePart part, int delta)
    {
        switch (part)
        {
            case AppearancePart.Race when gender: Editor.StepRace(delta); break;
            case AppearancePart.Face: Editor.StepFace(delta); break;
            case AppearancePart.Hair: Editor.StepHair(delta); break;
        }
    }

    public void Turn(float degrees) => Look.Turn(degrees);

    public void ToggleZoom() => Look.ToggleZoom();

    public void SetHairColour(Color colour) => Editor.SetHairColour(colour);

    public void OpenHairColour() => changeHair.OpenHairPalette(Editor);

    public void Confirm()
    {
        if (gender) AskGenderChange();
        else changeHair.Submit();
    }

    public void Cancel()
    {
        if (gender) genderChange.Close();
        else changeHair.Close();
    }

    private const int GenderChangeConfirmText = 45100;

    internal IReadOnlyList<AppearanceRaceChoice> GenderRaceChoices()
    {
        var choices = new List<AppearanceRaceChoice>();
        string className = CharacterClassCatalog.DisplayName(self.Class);
        foreach (int race in genderChange.Editor.RaceOptions)
            choices.Add(new AppearanceRaceChoice(StarterStats.RaceName(race), className, race == self.LastEnter.Race));
        return choices;
    }

    internal void AskGenderChange() =>
        Notice.Confirm(root, ItemData.Text(GenderChangeConfirmText, "Once accepted, the connection will be terminated. Do you want to accept?"),
            "OK", "Cancel", genderChange.Submit);
}
