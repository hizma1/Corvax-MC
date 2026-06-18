using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Shared._CCM14.Weapons.Ranged.Mortar;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(GrenaderMortarSystem))]
public sealed partial class MortarModeComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Activated;

    [DataField, AutoNetworkedField]
    public TimeSpan DoAfterDuration = TimeSpan.FromSeconds(3);

    [DataField, AutoNetworkedField]
    public SoundSpecifier? ToggleSound = new SoundPathSpecifier("/Audio/Weapons/click.ogg");

    [AutoNetworkedField]
    public EntityCoordinates? TargetCoordinates;

    [DataField, AutoNetworkedField]
    public LocId Examine = "ccm-gun-mortar-examine";

    [DataField, AutoNetworkedField]
    public float MaxRange = 6f;

    [DataField, AutoNetworkedField]
    public float Scatter = 1.5f;
}
