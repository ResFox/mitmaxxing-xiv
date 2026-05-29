using System;
using System.Collections.Generic;

using Dalamud.Game.Command;

using Dalamud.IoC;

using Dalamud.Plugin;

using Dalamud.Interface.Windowing;

using Dalamud.Plugin.Services;

using Dalamud.Game.ClientState.Objects.Types;

using MitStack.DeathRecap;

using MitStack.Windows;



namespace MitStack;



public sealed class Plugin : IDalamudPlugin

{

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService] internal static ICommandManager        CommandManager  { get; private set; } = null!;

    [PluginService] internal static IClientState           ClientState     { get; private set; } = null!;

    [PluginService] internal static IPlayerState           PlayerState     { get; private set; } = null!;

    [PluginService] internal static IObjectTable           ObjectTable     { get; private set; } = null!;

    [PluginService] internal static ITargetManager         TargetManager   { get; private set; } = null!;

    [PluginService] internal static IFramework             Framework       { get; private set; } = null!;

    [PluginService] internal static IDataManager           DataManager     { get; private set; } = null!;

    [PluginService] internal static ITextureProvider       TextureProvider { get; private set; } = null!;

    [PluginService] internal static IDutyState             DutyState       { get; private set; } = null!;

    [PluginService] internal static IPartyList              PartyList       { get; private set; } = null!;

    [PluginService] internal static IGameInteropProvider   GameInterop     { get; private set; } = null!;

    [PluginService] internal static ICondition             Condition       { get; private set; } = null!;

    [PluginService] internal static IPluginLog             Log             { get; private set; } = null!;



    private const string Cmd = "/mitstack";
    private const string CmdOpenSettingsA = "/mitmax";
    private const string CmdOpenSettingsB = "/mx";
    private const string CmdOpenDeathsA   = "/mxd";
    private const string CmdOpenDeathsB   = "/mitmaxxdeath";



    public Configuration         Configuration { get; private set; }

    public MitigationCalculator  Calculator   { get; init; }

    public CombatEventCapture    CombatCapture { get; init; }



    public readonly WindowSystem WindowSystem = new("MitStack");

    private OverlayWindow      OverlayWindow    { get; init; }

    private ConfigWindow       ConfigWindow     { get; init; }

    private MitListWindow      ListWindow       { get; init; }

    private DeathRecapWindow     DeathRecapWindow { get; init; }

    private DeathPopupWindow   DeathPopup       { get; init; }



    private DateTime _lastClean = DateTime.Now;



    public Plugin()

    {

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();



        if (Configuration.Version < 2)

        {

            Configuration.ListWindowOpen = true;

            Configuration.Version        = 2;

            Configuration.Save();

        }



        if (Configuration.Version < 3)

        {

            Configuration.Version = 3;

            Configuration.Save();

        }



        if (Configuration.Version < 4)

        {

            Configuration.Version = 4;

            Configuration.Save();

        }



        if (Configuration.Version < 5)

        {

            if (Configuration.OnlyInDuty)

                Configuration.ShowMitsWhen = MitVisibility.InDuty;

            Configuration.Version = 5;

            Configuration.Save();

        }



        Calculator    = new MitigationCalculator();

        CombatCapture = new CombatEventCapture(Configuration, GameInterop, Log);

        CombatCapture.OnDeath += OnDeathRecorded;



        OverlayWindow    = new OverlayWindow(this);

        ConfigWindow     = new ConfigWindow(this);

        ListWindow       = new MitListWindow(this) { IsOpen = Configuration.ListWindowOpen };

        DeathRecapWindow = new DeathRecapWindow(this) { IsOpen = Configuration.DeathRecapWindowOpen };

        DeathPopup       = new DeathPopupWindow(this);



        WindowSystem.AddWindow(OverlayWindow);

        WindowSystem.AddWindow(ConfigWindow);

        WindowSystem.AddWindow(ListWindow);

        WindowSystem.AddWindow(DeathRecapWindow);

        WindowSystem.AddWindow(DeathPopup);



        // Only /mx and /mxd are shown in the command list. Everything else is
        // still functional but hidden (ShowInHelp = false) to keep it clean.
        CommandManager.AddHandler(Cmd, new CommandInfo(OnCommand)
        {
            ShowInHelp = false,
        });

        CommandManager.AddHandler(CmdOpenSettingsB, new CommandInfo((_, _) => ToggleConfigUi())
        {
            HelpMessage = "Open settings",
        });

        CommandManager.AddHandler(CmdOpenDeathsA, new CommandInfo((_, _) => OpenDeathRecapWindow())
        {
            HelpMessage = "Opens death recap.",
        });

        CommandManager.AddHandler(CmdOpenSettingsA, new CommandInfo((_, _) => ToggleConfigUi())
        {
            ShowInHelp = false,
        });

        CommandManager.AddHandler(CmdOpenDeathsB, new CommandInfo((_, _) => OpenDeathRecapWindow())
        {
            ShowInHelp = false,
        });



        PluginInterface.UiBuilder.Draw         += WindowSystem.Draw;

        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

        PluginInterface.UiBuilder.OpenMainUi   += ToggleOverlay;



        Framework.Update += OnFrameworkUpdate;

    }



    public void Dispose()

    {

        Framework.Update -= OnFrameworkUpdate;

        CombatCapture.OnDeath -= OnDeathRecorded;

        CombatCapture.Dispose();



        PluginInterface.UiBuilder.Draw         -= WindowSystem.Draw;

        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;

        PluginInterface.UiBuilder.OpenMainUi   -= ToggleOverlay;



        WindowSystem.RemoveAllWindows();

        OverlayWindow.Dispose();

        ConfigWindow.Dispose();

        ListWindow.Dispose();

        DeathRecapWindow.Dispose();

        DeathPopup.Dispose();



        CommandManager.RemoveHandler(Cmd);
        CommandManager.RemoveHandler(CmdOpenSettingsA);
        CommandManager.RemoveHandler(CmdOpenSettingsB);
        CommandManager.RemoveHandler(CmdOpenDeathsA);
        CommandManager.RemoveHandler(CmdOpenDeathsB);

    }



    private void OnFrameworkUpdate(IFramework framework)

    {

        var localPlayer = ObjectTable.SearchById(PlayerState.EntityId) as IBattleChara;

        var target = Configuration.UseFocusTarget

            ? TargetManager.FocusTarget ?? TargetManager.Target

            : TargetManager.Target;

        IBattleChara? bossBc = target as IBattleChara;

        if (ShouldShowMits())
        {
            // Always feed the player's own status list so self/party buffs show
            // even when no boss is targeted (e.g. boss untargetable mid-mechanic).
            Calculator.Update(bossBc?.StatusList, localPlayer?.StatusList);
        }
        else
        {
            Calculator.Clear();
        }

        // ── Death recap (independent of the mit-display visibility filter) ──
        try { CombatCapture.Update(bossBc); }
        catch (Exception ex) { Log.Debug($"[MitStack] CombatCapture.Update error: {ex.Message}"); }

        if ((DateTime.Now - _lastClean).TotalSeconds >= 5)
        {
            CombatCapture.CleanStaleEvents();
            _lastClean = DateTime.Now;
        }
    }

    public bool ShouldShowMits() => Configuration.ShowMitsWhen switch
    {
        MitVisibility.InCombat => Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat],
        MitVisibility.InDuty   => DutyState.IsDutyStarted,
        _                      => true,
    };



    private void OnDeathRecorded(DeathRecord record)

    {

        if (!Configuration.DeathRecapAutoOpen) return;



        if (Configuration.DeathPopupEnabled)

        {

            DeathPopup.Show(record);

            return;

        }



        DeathRecapWindow.IsOpen            = true;

        Configuration.DeathRecapWindowOpen = true;

        Configuration.Save();

    }



    public void OpenDeathRecapWindow()

    {

        DeathRecapWindow.IsOpen            = true;

        Configuration.DeathRecapWindowOpen = true;

        Configuration.Save();

    }



    private void OnCommand(string command, string args)

    {

        switch (args.Trim().ToLowerInvariant())

        {

            case "config":

                ConfigWindow.IsOpen = !ConfigWindow.IsOpen;

                break;

            case "list":

            case "l":

                ToggleListWindow();

                break;

            case "deaths":

            case "death":

            case "recap":

                ToggleDeathRecapWindow();

                break;

            case "debug":

                PrintDebugStatuses();

                break;

            case "testdeath":

            case "test":

                CombatCapture.InjectTestDeath();

                break;

            default:

                OverlayWindow.IsOpen = !OverlayWindow.IsOpen;

                break;

        }

    }



    private void PrintDebugStatuses()

    {

        Log.Information("[MitStack] ── Death recap hook status ──");
        Log.Information($"  DeathRecapEnabled = {Configuration.DeathRecapEnabled}");
        Log.Information($"  OnlyInDuty        = {Configuration.DeathRecapOnlyInDuty}  (IsDutyStarted={DutyState.IsDutyStarted})");
        Log.Information($"  ActionEffect hook = {CombatCapture.ActionEffectInstalled}");
        Log.Information($"  ActorControl hook = {CombatCapture.ActorControlInstalled}");
        Log.Information($"  EffectResult hook = {CombatCapture.EffectResultInstalled}");
        Log.Information($"  Tracked actors    = {CombatCapture.TrackedActorCount}");
        Log.Information($"  Captured events   = {CombatCapture.TotalCapturedEvents}");
        Log.Information($"  Recorded deaths   = {CombatCapture.Deaths.Count}");
        if (!string.IsNullOrEmpty(CombatCapture.InstallError))
            Log.Information($"  Install error     = {CombatCapture.InstallError}");

        var target = TargetManager.FocusTarget ?? TargetManager.Target;



        if (target is IBattleChara boss)

        {

            Log.Information($"[MitStack] ── Statuses on TARGET '{boss.Name}' ──");

            PrintStatusList(boss.StatusList);

        }

        else

        {

            Log.Information("[MitStack] No battle target selected.");

        }



        var local = ObjectTable.SearchById(PlayerState.EntityId) as IBattleChara;

        if (local != null)

        {

            Log.Information($"[MitStack] ── Statuses on LOCAL PLAYER '{local.Name}' ──");

            PrintStatusList(local.StatusList);

        }



        Log.Information("[MitStack] ── End — open /xllog ──");

    }



    private static void PrintStatusList(Dalamud.Game.ClientState.Statuses.StatusList list)

    {

        foreach (var status in list)

        {

            string name = status.GameData.IsValid

                ? status.GameData.Value.Name.ExtractText()

                : "Unknown";

            Log.Information(

                $"  StatusId={status.StatusId,-6}  Name={name,-24} " +

                $"Remaining={status.RemainingTime:F1}s  SourceId={status.SourceId}");

        }

    }



    public void ResetConfiguration()
    {
        var fresh = new Configuration { Version = Configuration.Version };
        // Preserve which windows are open so the UI doesn't vanish on reset.
        fresh.ListWindowOpen       = Configuration.ListWindowOpen;
        fresh.DeathRecapWindowOpen = Configuration.DeathRecapWindowOpen;
        Configuration = fresh;
        Configuration.Save();
    }

    public void ToggleConfigUi() => ConfigWindow.IsOpen  = !ConfigWindow.IsOpen;

    public void ToggleOverlay()  => OverlayWindow.IsOpen = !OverlayWindow.IsOpen;



    public void ToggleListWindow()

    {

        ListWindow.IsOpen = !ListWindow.IsOpen;

        Configuration.ListWindowOpen = ListWindow.IsOpen;

        Configuration.Save();

    }



    public void ToggleDeathRecapWindow()

    {

        DeathRecapWindow.IsOpen = !DeathRecapWindow.IsOpen;

        Configuration.DeathRecapWindowOpen = DeathRecapWindow.IsOpen;

        Configuration.Save();

    }

}


