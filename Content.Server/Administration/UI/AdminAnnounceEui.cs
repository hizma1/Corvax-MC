using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Chat;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.EUI;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Eui;
using Robust.Shared.Audio;
using Robust.Shared.ContentPack;

namespace Content.Server.Administration.UI
{
    public sealed class AdminAnnounceEui : BaseEui
    {
        [Dependency] private readonly IAdminManager _adminManager = default!;
        [Dependency] private readonly IChatManager _chatManager = default!;
        [Dependency] private readonly IResourceManager _resourceManager = default!;
        [Dependency] private readonly IAdminLogManager _adminLogger = default!;
        
        private readonly ChatSystem _chatSystem;

        public AdminAnnounceEui()
        {
            IoCManager.InjectDependencies(this);
            _chatSystem = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<ChatSystem>();
        }

        public override void Opened()
        {
            StateDirty();
        }

        public override EuiStateBase GetNewState()
        {
            return new AdminAnnounceEuiState();
        }

        public override void HandleMessage(EuiMessageBase msg)
        {
            base.HandleMessage(msg);

            switch (msg)
            {
                case AdminAnnounceEuiMsg.DoAnnounce doAnnounce:
                    if (!_adminManager.HasAdminFlag(Player, AdminFlags.Admin))
                    {
                        Close();
                        break;
                    }
                    // CCM14-start
                    var hex = "#" + (doAnnounce.ColorHex?.Trim().TrimStart('#') ?? "1d8bad");
                    Color color;
                    try
                    {
                        color = Color.FromHex(hex);
                    }
                    catch
                    {
                        color = Color.FromHex("#1d8bad");
                    }

                    SoundSpecifier? sound = null;
                    if (!string.IsNullOrWhiteSpace(doAnnounce.SoundPath))
                    {
                        var path = doAnnounce.SoundPath.Trim();
                        if (_resourceManager.ContentFileExists(path))
                        {
                            sound = new SoundPathSpecifier(path)
                            {
                                Params = AudioParams.Default.WithVolume(-8f)
                            };
                        }
                    }
                    // CCM14-end
                    switch (doAnnounce.AnnounceType)
                    {
                        case AdminAnnounceType.Server:
                            _chatManager.DispatchServerAnnouncement(doAnnounce.Announcement, color); // CCM14
                            break;
                        // TODO: Per-station announcement support
                        case AdminAnnounceType.Station:
                            // CCM14-start
                            var sender = string.IsNullOrWhiteSpace(doAnnounce.Announcer)
                                ? Loc.GetString("chat-manager-sender-announcement")
                                : doAnnounce.Announcer;

                            var announcementWithSender = doAnnounce.Announcement;
                            if (!string.IsNullOrWhiteSpace(doAnnounce.Sender))
                            {
                                announcementWithSender +=
                                    $"\n{Loc.GetString("comms-console-announcement-sent-by")} {doAnnounce.Sender}";
                            }

                            _chatSystem.DispatchGlobalAnnouncement(
                                message: announcementWithSender,
                                sender: sender,
                                colorOverride: color,
                                playSound: true,
                                announcementSound: sound
                            );
                            // CCM14-end
                            break;
                    }
                    // CCM14-start
                    _adminLogger.Add(
                        LogType.Chat,
                        LogImpact.Low,
                        $"{Player.Name} sent admin announcement: [type={doAnnounce.AnnounceType}] [color={hex}] [sound={doAnnounce.SoundPath ?? "none"}] : {doAnnounce.Announcement}"
                    );
                    // CCM14-end

                    StateDirty();

                    if (doAnnounce.CloseAfter)
                        Close();

                    break;
            }
        }
    }
}
