namespace AutoDuty.Windows;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Configurations;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using Helpers;
using Managers;

/// <summary>
/// 导随（导师随机任务）配置页。
/// </summary>
internal static class MentorTab
{
    private static string search = string.Empty;

    private static MentorRouletteConfig Config => Configuration.MentorRoulette;

    public static void Draw()
    {
        MainWindow.CurrentTabName = Loc.Get("MentorTab.Title");

        MentorRouletteConfig cfg = Config;

        DrawHeader(cfg);
        ImGui.Separator();
        DrawMode(cfg);
        ImGui.Separator();
        DrawWhitelist(cfg);
        ImGui.Separator();
        DrawSettings(cfg);
    }

    private static void DrawHeader(MentorRouletteConfig cfg)
    {
        ImGui.TextColored(new Vector4(0.6f, 0.85f, 1f, 1f), Loc.Get("MentorTab.Title"));
        ImGui.SameLine();
        ImGui.TextDisabled($"({Loc.Get("MentorTab.State")}: {MentorRouletteManager.StateText})");

        ImGui.TextWrapped(MentorRouletteManager.StatusText);

        bool enabled = cfg.Enabled;
        if (ImGui.Checkbox(Loc.Get("MentorTab.Enable"), ref enabled))
        {
            if (enabled)
                MentorRouletteManager.Start();
            else
                MentorRouletteManager.Stop(Loc.Get("MentorTab.StoppedByUser"));

            ConfigurationProfileV2.Save();
        }

        ImGui.SameLine();

        using (ImRaii.Disabled(cfg.Enabled))
        {
            if (ImGui.Button(Loc.Get("MentorTab.Start")))
            {
                MentorRouletteManager.Start();
                ConfigurationProfileV2.Save();
            }
        }

        ImGui.SameLine();

        using (ImRaii.Disabled(!cfg.Enabled))
        {
            if (ImGui.Button(Loc.Get("MentorTab.Stop")))
            {
                MentorRouletteManager.Stop(Loc.Get("MentorTab.StoppedByUser"));
                ConfigurationProfileV2.Save();
            }
        }

        if (MentorRouletteManager.State == MentorRouletteState.Grace)
        {
            ImGui.SameLine();

            if (ImGui.Button(Loc.Get("MentorTab.CancelLeave")))
                MentorRouletteManager.CancelLeave();
        }

        if (MentorRouletteManager.State == MentorRouletteState.Cooldown && MentorRouletteManager.CooldownUntil > DateTime.UtcNow)
        {
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f),
                              Loc.Get("MentorTab.PenaltyWaitUntil", MentorRouletteManager.CooldownUntil.ToString("HH:mm:ss")));
        }

        ImGui.TextDisabled(Loc.Get("MentorTab.Stats",
                                   cfg.QueuedCount,
                                   cfg.JoinedCount,
                                   cfg.CompletedCount,
                                   cfg.LeftCount,
                                   MentorRouletteManager.ConsecutiveLeaves));
    }

    private static void DrawMode(MentorRouletteConfig cfg)
    {
        ImGui.Text(Loc.Get("MentorTab.Mode"));

        bool all = cfg.Mode == MentorRouletteMode.AllSupported;
        if (ImGui.RadioButton(Loc.Get("MentorTab.Mode.AllSupported"), all))
        {
            cfg.Mode = MentorRouletteMode.AllSupported;
            ConfigurationProfileV2.Save();
        }

        ImGui.TextDisabled(Loc.Get("MentorTab.Mode.AllSupportedHelp"));

        bool specified = cfg.Mode == MentorRouletteMode.Specified;
        if (ImGui.RadioButton(Loc.Get("MentorTab.Mode.Specified"), specified))
        {
            cfg.Mode = MentorRouletteMode.Specified;
            ConfigurationProfileV2.Save();
        }

        ImGui.TextDisabled(Loc.Get("MentorTab.Mode.SpecifiedHelp"));

        ImGui.NewLine();
        ImGui.Text(Loc.Get("MentorTab.UnsupportedAction"));

        bool leave = cfg.UnsupportedAction == MentorUnsupportedAction.LeaveDuty;
        if (ImGui.RadioButton(Loc.Get("MentorTab.UnsupportedAction.Leave"), leave))
        {
            cfg.UnsupportedAction = MentorUnsupportedAction.LeaveDuty;
            ConfigurationProfileV2.Save();
        }

        ImGui.SameLine();

        bool keep = cfg.UnsupportedAction == MentorUnsupportedAction.KeepDuty;
        if (ImGui.RadioButton(Loc.Get("MentorTab.UnsupportedAction.Keep"), keep))
        {
            cfg.UnsupportedAction = MentorUnsupportedAction.KeepDuty;
            ConfigurationProfileV2.Save();
        }

        ImGui.TextDisabled(Loc.Get("MentorTab.UnsupportedAction.Help"));
    }

    private static void DrawWhitelist(MentorRouletteConfig cfg)
    {
        bool expanded = ImGui.CollapsingHeader(Loc.Get("MentorTab.Whitelist", cfg.Whitelist.Count));

        if (!expanded)
            return;

        List<ContentPathsManager.ContentPathContainer> duties = ContentPathsManager.DictionaryPaths.Values
                                                                                     .Where(c => c.Paths.Count > 0)
                                                                                     .OrderBy(c => c.Content.ClassJobLevelRequired)
                                                                                     .ThenBy(c => c.Content.Name)
                                                                                     .ToList();

        ImGui.SetNextItemWidth(250);
        ImGui.InputTextWithHint("##MentorWhitelistSearch", Loc.Get("MentorTab.Whitelist.Search"), ref search, 128);

        ImGui.SameLine();

        if (ImGui.Button(Loc.Get("MentorTab.Whitelist.AddAll")))
        {
            cfg.Whitelist = duties.Select(d => d.ID).Distinct().ToList();
            ConfigurationProfileV2.Save();
        }

        ImGui.SameLine();

        if (ImGui.Button(Loc.Get("MentorTab.Whitelist.Clear")))
        {
            cfg.Whitelist.Clear();
            ConfigurationProfileV2.Save();
        }

        ImGui.TextDisabled(Loc.Get("MentorTab.Whitelist.Count", cfg.Whitelist.Count, duties.Count));

        using ImRaii.DisabledDisposable whitelistDisabled = ImRaii.Disabled(cfg.Mode != MentorRouletteMode.Specified);

        using (ImRaii.Child("##MentorWhitelistChild", new Vector2(0, 220), true))
        {
            foreach (ContentPathsManager.ContentPathContainer duty in duties)
            {
                string name = duty.Content.Name ?? duty.ID.ToString();

                if (search.Length > 0 && !name.Contains(search, StringComparison.OrdinalIgnoreCase))
                    continue;

                bool selected = cfg.Whitelist.Contains(duty.ID);

                if (ImGui.Checkbox($"##MentorWhitelist{duty.ID}", ref selected))
                {
                    if (selected)
                    {
                        if (!cfg.Whitelist.Contains(duty.ID))
                            cfg.Whitelist.Add(duty.ID);
                    }
                    else
                        cfg.Whitelist.Remove(duty.ID);

                    ConfigurationProfileV2.Save();
                }

                ImGui.SameLine();
                ImGui.TextUnformatted(name);
                ImGui.SameLine();
                ImGui.TextDisabled($"Lv.{duty.Content.ClassJobLevelRequired}  ({duty.ID})");
            }
        }
    }

    private static void DrawSettings(MentorRouletteConfig cfg)
    {
        if (!ImGui.CollapsingHeader(Loc.Get("MentorTab.Settings")))
            return;

        int judgeMs = cfg.DelayBeforeJudgeMs;
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputInt(Loc.Get("MentorTab.Settings.DelayBeforeJudge"), ref judgeMs))
        {
            cfg.DelayBeforeJudgeMs = Math.Clamp(judgeMs, 0, 60000);
            ConfigurationProfileV2.Save();
        }

        int grace = cfg.LeaveGraceSec;
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputInt(Loc.Get("MentorTab.Settings.LeaveGrace"), ref grace))
        {
            cfg.LeaveGraceSec = Math.Clamp(grace, 0, 60);
            ConfigurationProfileV2.Save();
        }

        int combatTimeout = cfg.LeaveCombatTimeoutSec;
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputInt(Loc.Get("MentorTab.Settings.LeaveCombatTimeout"), ref combatTimeout))
        {
            cfg.LeaveCombatTimeoutSec = Math.Clamp(combatTimeout, 0, 300);
            ConfigurationProfileV2.Save();
        }

        int leaveTimeout = cfg.LeaveTimeoutSec;
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputInt(Loc.Get("MentorTab.Settings.LeaveTimeout"), ref leaveTimeout))
        {
            cfg.LeaveTimeoutSec = Math.Clamp(leaveTimeout, 10, 600);
            ConfigurationProfileV2.Save();
        }

        int requeue = cfg.RequeueDelaySec;
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputInt(Loc.Get("MentorTab.Settings.RequeueDelay"), ref requeue))
        {
            cfg.RequeueDelaySec = Math.Clamp(requeue, 0, 600);
            ConfigurationProfileV2.Save();
        }

        int maxLeaves = cfg.MaxLeavesBeforeStop;
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputInt(Loc.Get("MentorTab.Settings.MaxLeaves"), ref maxLeaves))
        {
            cfg.MaxLeavesBeforeStop = Math.Max(0, maxLeaves);
            ConfigurationProfileV2.Save();
        }

        bool penaltyGuard = cfg.PenaltyGuard;
        if (ImGui.Checkbox(Loc.Get("MentorTab.Settings.PenaltyGuard"), ref penaltyGuard))
        {
            cfg.PenaltyGuard = penaltyGuard;
            ConfigurationProfileV2.Save();
        }

        int penaltyMinutes = cfg.PenaltyWaitMinutes;
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputInt(Loc.Get("MentorTab.Settings.PenaltyWait"), ref penaltyMinutes))
        {
            cfg.PenaltyWaitMinutes = Math.Clamp(penaltyMinutes, 1, 120);
            ConfigurationProfileV2.Save();
        }

        bool useLoopActions = cfg.UseLoopActions;
        if (ImGui.Checkbox(Loc.Get("MentorTab.Settings.UseLoopActions"), ref useLoopActions))
        {
            cfg.UseLoopActions = useLoopActions;
            ConfigurationProfileV2.Save();
        }

        ImGui.TextDisabled(Loc.Get("MentorTab.Settings.UseLoopActionsHelp"));

        bool chatNotify = cfg.ChatNotify;
        if (ImGui.Checkbox(Loc.Get("MentorTab.Settings.ChatNotify"), ref chatNotify))
        {
            cfg.ChatNotify = chatNotify;
            ConfigurationProfileV2.Save();
        }

        int roulette = (int)cfg.RouletteRowId;
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputInt(Loc.Get("MentorTab.Settings.RouletteRow"), ref roulette))
        {
            cfg.RouletteRowId = (uint)Math.Clamp(roulette, 0, 100);
            ConfigurationProfileV2.Save();
        }

        ImGui.TextDisabled(Loc.Get("MentorTab.Settings.RouletteRowHelp", RouletteName(cfg.RouletteRowId)));
    }

    private static string RouletteName(uint rowId)
    {
        rowId = RouletteQueueHelper.ResolveRouletteId(rowId);

        try
        {
            Lumina.Excel.Sheets.ContentRoulette? row = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.ContentRoulette>().GetRowOrDefault(rowId);
            if (row != null)
                return row.Value.Name.ToString();
        }
        catch
        {
            // ignored
        }

        return rowId.ToString();
    }
}
