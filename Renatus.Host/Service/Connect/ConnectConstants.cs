namespace Renatus.Host.Service.Connect;

public static class ConnectConstants
{
    public const string ProfileReadScope = "Yggdrasil.PlayerProfiles.Read";
    public const string ProfileSelectScope = "Yggdrasil.PlayerProfiles.Select";
    public const string ServerJoinScope = "Yggdrasil.Server.Join";
    public const string SelectedProfileClaim = "selectedProfile";
    public const string AvailableProfilesClaim = "availableProfiles";
    public const string SelectedProfileBindingClaim = "renatus:selected_profile_uuid";
    public const string InteractionParameter = "interaction";
}
