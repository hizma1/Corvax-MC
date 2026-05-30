// CM14 rework: non-RMC edit marker.
// Forge port: this manager is now backed by the Monolith _Forge SponsorManager.
// Sponsor tier resolution comes from Discord roles (or a manual DB override) and is
// translated to a CCM-flavored snapshot so the rest of the CCM stack keeps working
// unchanged (chat color, OOC tags, ghost/xeno/armor camouflage, role timer bypass,
// queue bypass and round-end credits).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._Forge.Sponsor;
using Content.Server.Database;
using Content.Server.Station.Systems;
using Content.Shared._CCM.Sponsorship;
using Content.Shared._Forge.Sponsor;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._CCM.Sponsorship;

public sealed class CCMSponsorshipManager : IPostInjectInit
{
    private const string DonateUrl = "https://boosty.to/cmc14";

    private static readonly TimeSpan DefaultManualSponsorshipDuration = TimeSpan.FromDays(30);

    private readonly Dictionary<NetUserId, CCMSponsorshipStatusSnapshot> _status = new();
    private readonly Dictionary<NetUserId, CCMStoredSponsorshipRecord> _manualTierOverrides = new();

    [Dependency] private readonly UserDbDataManager _userDb = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly SponsorManager _sponsorManager = default!;
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    /// <summary>
    ///     Raised after a player's resolved sponsorship status changes so the EntitySystem
    ///     half (CCMSponsorshipSystem) can push the new state to the client and refresh
    ///     their customization.
    /// </summary>
    public event Action<NetUserId>? StatusChanged;

    private StationJobsSystem StationJobs => _entManager.System<StationJobsSystem>();

    public CCMSponsorshipStatusSnapshot GetStatus(NetUserId userId)
    {
        return _status.GetValueOrDefault(userId) ?? EmptySnapshot();
    }

    public bool HasRoleTimerBypass(NetUserId userId)
    {
        return GetStatus(userId).Tier >= CCMSponsorshipTier.SponsorII;
    }

    public bool HasRoleTimerBypass(ICommonSession session)
    {
        return HasRoleTimerBypass(session.UserId);
    }

    public void SetManualTierOverride(NetUserId userId, CCMSponsorshipTier tier, long? expirationUnixSeconds = null)
    {
        if (tier == CCMSponsorshipTier.None)
            _manualTierOverrides.Remove(userId);
        else
            _manualTierOverrides[userId] = new CCMStoredSponsorshipRecord(
                tier,
                expirationUnixSeconds ?? ResolveExpiration(tier, 0));

        RefreshResolvedStatus(userId);
    }

    public Task PersistTier(NetUserId userId, CCMSponsorshipTier tier, int days)
    {
        var expiration = tier == CCMSponsorshipTier.None
            ? 0
            : DateTimeOffset.UtcNow.AddDays(Math.Max(1, days)).ToUnixTimeSeconds();

        return _db.SaveCCMStoredSponsorship(userId.UserId, tier, expiration);
    }

    public async Task SetPersistentTier(NetUserId userId, CCMSponsorshipTier tier, int days)
    {
        var expiration = tier == CCMSponsorshipTier.None
            ? 0
            : DateTimeOffset.UtcNow.AddDays(Math.Max(1, days)).ToUnixTimeSeconds();

        await _db.SaveCCMStoredSponsorship(userId.UserId, tier, expiration);
        SetManualTierOverride(userId, tier, expiration);
    }

    public bool TryGetChatColorHex(NetUserId userId, bool looc, out string colorHex)
    {
        var status = GetStatus(userId);
        colorHex = looc ? status.LoocColorHex : status.OocColorHex;
        return !string.IsNullOrWhiteSpace(colorHex);
    }

    public IReadOnlyList<(string Ckey, CCMSponsorshipTier Tier)> GetConnectedSponsorsForCredits()
    {
        return _players.Sessions
            .Select(session => (session.Name, GetStatus(session.UserId).Tier))
            .Where(entry => entry.Tier != CCMSponsorshipTier.None)
            .OrderByDescending(entry => entry.Tier)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry => (entry.Name, entry.Tier))
            .ToList();
    }

    /// <summary>
    ///     Re-resolves a session's status (e.g. after a sponsor update came in from Discord)
    ///     and returns whether it actually changed.
    /// </summary>
    public bool Refresh(NetUserId userId)
    {
        var before = GetStatus(userId);
        RefreshResolvedStatus(userId);
        var after = GetStatus(userId);
        return !StatusEquals(before, after);
    }

    private async Task LoadData(ICommonSession session, CancellationToken cancel)
    {
        var stored = await _db.GetCCMStoredSponsorship(session.UserId.UserId);
        cancel.ThrowIfCancellationRequested();

        if (stored != null && stored.Tier != CCMSponsorshipTier.None
            && stored.ExpirationUnixSeconds > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            _manualTierOverrides[session.UserId] = stored;
        }

        RefreshResolvedStatus(session.UserId);
    }

    private void ClientDisconnected(ICommonSession session)
    {
        _status.Remove(session.UserId);
        _manualTierOverrides.Remove(session.UserId);
        StationJobs.ClearExternalWeightOverride(session.UserId);
    }

    private void RefreshResolvedStatus(NetUserId userId)
    {
        var snapshot = BuildSnapshot(userId);
        _status[userId] = snapshot;
        ApplyRoleWeightOverride(userId, snapshot);
        StatusChanged?.Invoke(userId);
    }

    private CCMSponsorshipStatusSnapshot BuildSnapshot(NetUserId userId)
    {
        if (_manualTierOverrides.TryGetValue(userId, out var manualOverride)
            && manualOverride.Tier != CCMSponsorshipTier.None
            && manualOverride.ExpirationUnixSeconds > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            return new CCMSponsorshipStatusSnapshot(
                manualOverride.Tier,
                DonateUrl,
                manualOverride.ExpirationUnixSeconds,
                GetDefaultColor(manualOverride.Tier, false),
                GetDefaultColor(manualOverride.Tier, true),
                GetRoleWeightBonus(manualOverride.Tier),
                manualOverride.Tier >= CCMSponsorshipTier.SponsorI,
                manualOverride.Tier >= CCMSponsorshipTier.SponsorII);
        }

        if (!_sponsorManager.TryGetSponsor(userId, out var level) || level == SponsorLevel.None)
            return EmptySnapshot();

        var tier = SponsorLevelToTier(level);
        var forgeColor = SponsorData.SponsorColor.GetValueOrDefault(level, GetDefaultColor(tier, false));

        return new CCMSponsorshipStatusSnapshot(
            tier,
            DonateUrl,
            ResolveExpiration(tier, 0),
            forgeColor,
            GetDefaultColor(tier, true),
            GetRoleWeightBonus(tier),
            tier >= CCMSponsorshipTier.SponsorI,
            tier >= CCMSponsorshipTier.SponsorII);
    }

    private void ApplyRoleWeightOverride(NetUserId userId, CCMSponsorshipStatusSnapshot snapshot)
    {
        if (snapshot.RoleWeightBonus <= 0f)
        {
            StationJobs.ClearExternalWeightOverride(userId);
            return;
        }

        StationJobs.SetExternalWeightOverride(userId, snapshot.RoleWeightBonus);
    }

    private CCMSponsorshipStatusSnapshot EmptySnapshot()
    {
        return new CCMSponsorshipStatusSnapshot(
            CCMSponsorshipTier.None,
            DonateUrl,
            0,
            string.Empty,
            string.Empty,
            0f,
            false,
            false);
    }

    public static CCMSponsorshipTier SponsorLevelToTier(SponsorLevel level)
    {
        // Перк-распределение (см. CCMSponsorshipWindow):
        //   L1     -> SponsorI   (приоритетный вход + цвет OOC + ckey в конце раунда)
        //   L2     -> SponsorII  (+ цвет LOOC + готовый OOC-тег + базовая кастомизация)
        //   L3..L6 -> SponsorIII (+ скин призрака + свой OOC-тег + расширенная кастомизация)
        return level switch
        {
            SponsorLevel.None => CCMSponsorshipTier.None,
            SponsorLevel.Level1 => CCMSponsorshipTier.SponsorI,
            SponsorLevel.Level2 => CCMSponsorshipTier.SponsorII,
            SponsorLevel.Level3 => CCMSponsorshipTier.SponsorIII,
            SponsorLevel.Level4 => CCMSponsorshipTier.SponsorIII,
            SponsorLevel.Level5 => CCMSponsorshipTier.SponsorIII,
            SponsorLevel.Level6 => CCMSponsorshipTier.SponsorIII,
            _ => CCMSponsorshipTier.None,
        };
    }

    private static float GetRoleWeightBonus(CCMSponsorshipTier tier)
    {
        return 0f;
    }

    private static string GetDefaultColor(CCMSponsorshipTier tier, bool looc)
    {
        return tier switch
        {
            CCMSponsorshipTier.SponsorI => looc ? "#7FD7FF" : "#61C9FF",
            CCMSponsorshipTier.SponsorII => looc ? "#F2A7FF" : "#D96CFF",
            CCMSponsorshipTier.SponsorIII => looc ? "#FFE082" : "#F6C453",
            _ => string.Empty,
        };
    }

    private static long ResolveExpiration(CCMSponsorshipTier tier, long expirationUnixSeconds)
    {
        if (tier == CCMSponsorshipTier.None)
            return 0;

        if (expirationUnixSeconds > 0)
            return expirationUnixSeconds;

        return DateTimeOffset.UtcNow.Add(DefaultManualSponsorshipDuration).ToUnixTimeSeconds();
    }

    private static bool StatusEquals(CCMSponsorshipStatusSnapshot left, CCMSponsorshipStatusSnapshot right)
    {
        return left.Tier == right.Tier &&
               left.DonateUrl == right.DonateUrl &&
               left.ExpirationUnixSeconds == right.ExpirationUnixSeconds &&
               left.OocColorHex == right.OocColorHex &&
               left.LoocColorHex == right.LoocColorHex &&
               Math.Abs(left.RoleWeightBonus - right.RoleWeightBonus) < 0.001f &&
               left.QueueBypass == right.QueueBypass &&
               left.CustomizationUnlocked == right.CustomizationUnlocked;
    }

    void IPostInjectInit.PostInject()
    {
        _userDb.AddOnLoadPlayer(LoadData);
        _userDb.AddOnPlayerDisconnect(ClientDisconnected);

        _sponsorManager.SponsorChanged += OnForgeSponsorChanged;
    }

    private void OnForgeSponsorChanged(NetUserId userId)
    {
        RefreshResolvedStatus(userId);
    }
}
