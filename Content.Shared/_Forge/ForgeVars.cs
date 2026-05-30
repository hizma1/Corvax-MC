using Robust.Shared.Configuration;

namespace Content.Shared._Forge;

/// <summary>
///     Forge module console variables (ported from Monolith).
/// </summary>
[CVarDefs]
// ReSharper disable once InconsistentNaming
public sealed class ForgeVars
{
    /// <summary>
    ///     URL of the Discord auth remote service.
    /// </summary>
    public static readonly CVarDef<string> DiscordApiUrl =
        CVarDef.Create("jerry.discord_api_url", "", CVar.CONFIDENTIAL | CVar.SERVERONLY);

    /// <summary>
    ///     Toggles the Discord auth gate before letting players into the server.
    /// </summary>
    public static readonly CVarDef<bool> DiscordAuthEnabled =
        CVarDef.Create("jerry.discord_auth_enabled", false, CVar.CONFIDENTIAL | CVar.SERVERONLY);

    /// <summary>
    ///     Discord guild ID used when resolving sponsor roles.
    /// </summary>
    public static readonly CVarDef<string> DiscordGuildID =
        CVarDef.Create("jerry.discord_guildId", "1222332535628103750", CVar.CONFIDENTIAL | CVar.SERVERONLY);

    /// <summary>
    ///     Bearer key used to authenticate with the Discord auth API.
    /// </summary>
    public static readonly CVarDef<string> ApiKey =
        CVarDef.Create("jerry.discord_apikey", "", CVar.CONFIDENTIAL | CVar.SERVERONLY);

    /// <summary>
    ///     Controls if the connections queue is enabled.
    ///     If enabled players will be added to a queue instead of being kicked after SoftMaxPlayers is reached.
    /// </summary>
    public static readonly CVarDef<bool> QueueEnabled =
        CVarDef.Create("queue.enabled", false, CVar.SERVERONLY);
}
