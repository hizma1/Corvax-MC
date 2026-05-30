using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Content.Shared._Forge.Sponsor;
using JetBrains.Annotations;
using Robust.Shared.Network;

namespace Content.Server._Forge.Sponsor;

[UsedImplicitly]
public sealed class SponsorManager : ISharedSponsorManager
{
    public readonly Dictionary<NetUserId, SponsorLevel> Sponsors = new();

    public event Action<NetUserId>? SponsorChanged;

    public void Initialize() { }

    public bool TryGetSponsor(NetUserId user, [NotNullWhen(true)] out SponsorLevel level)
    {
        return Sponsors.TryGetValue(user, out level);
    }

    public bool TryGetSponsorColor(SponsorLevel level, [NotNullWhen(true)] out string? color)
    {
        return SponsorData.SponsorColor.TryGetValue(level, out color);
    }

    public bool TryGetSponsorGhost(SponsorLevel level, [NotNullWhen(true)] out string? ghost)
    {
        return SponsorData.SponsorGhost.TryGetValue(level, out ghost);
    }

    public void SetSponsor(NetUserId user, SponsorLevel level)
    {
        if (level == SponsorLevel.None)
        {
            if (Sponsors.Remove(user))
                SponsorChanged?.Invoke(user);
            return;
        }

        Sponsors[user] = level;
        SponsorChanged?.Invoke(user);
    }

    public void RemoveSponsor(NetUserId user)
    {
        if (Sponsors.Remove(user))
            SponsorChanged?.Invoke(user);
    }
}
