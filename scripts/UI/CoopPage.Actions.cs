using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Meta;
using Wavebreak.Net;

namespace Wavebreak.UI;

public sealed partial class CoopPage
{
    // ------------------------------------------------------------ actions

    private void Retry()
    {
        _hint = null;
        if (SteamApp.TryInit(GetTree()))
        {
            Coop.Notice = null;
            SteamLobby.ConsumePendingJoin();
            _hostSteam.GrabFocus();
        }
        RefreshFriends();
        Refresh();
    }

    private void HostSteam()
    {
        if (!SteamApp.TryInit(GetTree())) { Refresh(); return; }
        Coop.Notice = null;
        _hint = null;
        _readyOn = false;
        SteamLobby.Host();
        Refresh();
    }

    private void Invite()
    {
        // The overlay may not hook a game Steam did not launch, so the
        // friends-list route is always shown alongside.
        SteamLobby.OpenInviteDialog();
        _hint = Loc.T("No Steam window? Invite from your Steam friends list: right-click a friend, Invite to Lobby.");
        Refresh();
    }

    private void JoinFriend(SteamLobby.FriendGame f)
    {
        _hint = null;
        _readyOn = false;
        SteamLobby.Join(f.Lobby, f.Name);
        Refresh();
    }

    private void HostDirect()
    {
        if (!int.TryParse(_port.Text.Trim(), out int port) || port is < 1 or > 65535)
        {
            Coop.Notice = Loc.T("The port must be a number from 1 to 65535.");
            Refresh();
            return;
        }
        Coop.Notice = null;
        SaveSettings();
        NetSession.Ensure(GetTree()).Host(port);
    }

    private void JoinDirect()
    {
        if (!NetSession.ParseAddress(_address.Text, out _, out _))
        {
            Coop.Notice = Loc.T("Enter an address like 192.168.1.20:7777.");
            Refresh();
            return;
        }
        Coop.Notice = null;
        _readyOn = false;
        SaveSettings();
        NetSession.Ensure(GetTree()).Join(_address.Text.Trim());
    }

    private void ToggleReady()
    {
        if (!Coop.IsClient) return;
        _readyOn = !_readyOn;
        NetSession.Instance?.SetReady(_readyOn);
        Refresh();
    }

    // ------------------------------------------------------------ view

}
