namespace AutoDuty.Helpers
{
    using System;
    using Configurations;
    using Dalamud.Plugin.Services;
    using ECommons;
    using ECommons.ChatMethods;
    using ECommons.DalamudServices;
    using ECommons.GameHelpers;
    using ECommons.Throttlers;
    using FFXIVClientStructs.FFXIV.Client.Game;
    using FFXIVClientStructs.FFXIV.Client.Game.UI;
    using IPC;
    using Managers;

    /// <summary>
    /// 导随（导师随机任务）自动化总控。
    ///
    /// 流程：排队 -> 进本判定 -> (支持) 交给 Plugin.Run 跑这一趟 / (不支持) 退本 -> 冷却 -> 排队…
    /// 判定“支持”的依据是 ContentPathsManager 里有没有该副本的路径，加上配置里的白名单模式。
    /// </summary>
    internal static class MentorRouletteManager
    {
        internal static MentorRouletteState State { get; private set; } = MentorRouletteState.Stopped;

        internal static string StatusText { get; private set; } = string.Empty;

        internal static DateTime CooldownUntil { get; private set; } = DateTime.MinValue;

        internal static bool GraceCancelRequested { get; set; }

        internal static bool PenaltySuspected { get; private set; }

        internal static int ConsecutiveLeaves { get; private set; }

        internal static DateTime StateEnteredAt { get; private set; } = DateTime.MinValue;

        internal static DateTime JudgeStartedAt { get; private set; }

        private static DateTime leaveStartedAt;
        private static DateTime? dutyCompletedAt;
        private static DateTime? stoppedInsideSince;
        private static DateTime queueAttemptStart;
        private static int      leaveStep;
        private static int      leaveAttempts;
        private static bool     lastLeaveWasAbandon;
        private static bool     joinedQueueThisAttempt;
        private static bool     hooksInstalled;

        private static MentorRouletteConfig Config => Configuration.MentorRoulette;

        internal static bool IsInsideDuty => InDungeon || Svc.DutyState.IsDutyStarted;

        internal static void Initialize()
        {
            if (hooksInstalled)
                return;

            hooksInstalled = true;
            Svc.Framework.Update += Update;

            // 插件重载后如果开关还是开着的，自动接上
            if (Config.Enabled)
                Start();
        }

        internal static void Dispose()
        {
            if (!hooksInstalled)
                return;

            hooksInstalled = false;
            Svc.Framework.Update -= Update;
            ForceStopHelpers();
        }

        internal static void Start()
        {
            MentorRouletteConfig cfg = Config;
            cfg.Enabled = true;
            cfg.ResetSessionStats();
            ConsecutiveLeaves = 0;
            PenaltySuspected  = false;
            lastLeaveWasAbandon = false;
            joinedQueueThisAttempt = false;
            GraceCancelRequested = false;

            if (IsInsideDuty)
            {
                JudgeStartedAt = DateTime.UtcNow;
                SetState(MentorRouletteState.Judging, "已在本内，直接开始判定");
            }
            else
            {
                BeginQueueing();
            }

            ConfigurationProfileV2.Save();
            Svc.Log.Info("[MentorRoulette] started");
        }

        internal static void Stop(string reason = "")
        {
            MentorRouletteConfig cfg = Config;
            cfg.Enabled = false;
            ForceStopHelpers();

            if (!IsInsideDuty && Plugin.Stage is not (Stage.Stopped or Stage.Paused))
                Plugin.Stage = Stage.Stopped;

            SetState(MentorRouletteState.Stopped, reason.Length > 0 ? reason : "已停止");
            ConfigurationProfileV2.Save();
            Svc.Log.Info($"[MentorRoulette] stopped: {reason}");
        }

        internal static void CancelLeave()
        {
            if (State != MentorRouletteState.Grace)
                return;

            GraceCancelRequested = true;
        }

        private static void ForceStopHelpers()
        {
            RouletteQueueHelper.Instance.StopIfRunning();
            ExitDutyHelper.Instance.StopIfRunning();
        }

        private static void SetState(MentorRouletteState state, string status)
        {
            State         = state;
            StatusText    = status;
            StateEnteredAt = DateTime.UtcNow;
            Svc.Log.Debug($"[MentorRoulette] => {state}: {status}");
        }

        private static void Notify(string message)
        {
            Svc.Log.Info($"[MentorRoulette] {message}");

            if (Config.ChatNotify)
                ChatPrinter.Yellow($"[AutoDuty] {message}");
        }

        internal static bool IsSupported(uint territoryType)
        {
            if (territoryType == 0)
                return false;

            if (!ContentPathsManager.DictionaryPaths.TryGetValue(territoryType, out ContentPathsManager.ContentPathContainer? container))
                return false;

            if (container.Paths.Count == 0)
                return false;

            MentorRouletteConfig cfg = Config;

            if (cfg.Mode == MentorRouletteMode.Specified)
                return cfg.Whitelist.Contains(territoryType);

            return true;
        }

        internal static string TerritoryName(uint territoryType)
        {
            if (ContentPathsManager.DictionaryPaths.TryGetValue(territoryType, out ContentPathsManager.ContentPathContainer? container))
                return container.Content.Name ?? territoryType.ToString();

            if (ContentHelper.DictionaryContent.TryGetValue(territoryType, out Data.Classes.Content? content))
                return content.Name ?? territoryType.ToString();

            return territoryType.ToString();
        }

        private static void Update(IFramework framework)
        {
            MentorRouletteConfig cfg = Config;

            if (!cfg.Enabled)
            {
                if (State != MentorRouletteState.Stopped)
                    SetState(MentorRouletteState.Stopped, string.Empty);
                return;
            }

            if (!EzThrottler.Throttle("MentorRouletteManager", 200) || !PlayerHelper.IsReadyFull)
                return;

            try
            {
                switch (State)
                {
                    case MentorRouletteState.Queueing:
                        UpdateQueueing(cfg);
                        break;
                    case MentorRouletteState.Judging:
                        UpdateJudging(cfg);
                        break;
                    case MentorRouletteState.Grace:
                        UpdateGrace(cfg);
                        break;
                    case MentorRouletteState.Leaving:
                        UpdateLeaving(cfg);
                        break;
                    case MentorRouletteState.Cooldown:
                        UpdateCooldown(cfg);
                        break;
                    case MentorRouletteState.Running:
                        UpdateRunning();
                        break;
                    case MentorRouletteState.Keeping:
                        UpdateKeeping();
                        break;
                }
            }
            catch (Exception ex)
            {
                Svc.Log.Error($"[MentorRoulette] update failed: {ex}");
            }
        }

        private static void BeginQueueing()
        {
            queueAttemptStart      = DateTime.UtcNow;
            joinedQueueThisAttempt = false;
            PenaltySuspected       = false;
            SetState(MentorRouletteState.Queueing, "排队中…");
        }

        private static unsafe void UpdateQueueing(MentorRouletteConfig cfg)
        {
            if (IsInsideDuty)
            {
                OnEnteredDuty();
                return;
            }

            if (Conditions.Instance()->InDutyQueue)
            {
                if (!joinedQueueThisAttempt)
                {
                    joinedQueueThisAttempt = true;
                    Config.QueuedCount++;

                    byte queuedRoulette = QueuedRouletteId();
                    Svc.Log.Info($"[MentorRoulette] entered queue (roulette id {(queuedRoulette == 0 ? "?" : queuedRoulette.ToString())})");

                    SetState(MentorRouletteState.Queueing, "已进入队列，等待弹出");
                }

                return;
            }

            if (RouletteQueueHelper.State != ActionState.Running)
                RouletteQueueHelper.Invoke(cfg.RouletteRowId);

            // 惩罚看门狗：试着排了 25 秒还没进队列，就假定处于“放弃任务”惩罚中
            if (cfg.PenaltyGuard && lastLeaveWasAbandon && RouletteQueueHelper.State == ActionState.Running &&
                (DateTime.UtcNow - queueAttemptStart).TotalSeconds > 25)
            {
                PenaltySuspected = true;
                CooldownUntil    = Config.LastLeaveAt.AddMinutes(cfg.PenaltyWaitMinutes);

                if (CooldownUntil > DateTime.UtcNow)
                {
                    ForceStopHelpers();
                    SetState(MentorRouletteState.Cooldown, $"检测到排本惩罚，等待至 {CooldownUntil:HH:mm:ss}");
                    Notify($"检测到排本惩罚（放弃任务的 30 分钟限制），等到 {CooldownUntil:HH:mm} 再排。");
                }
            }
        }

        private static unsafe byte QueuedRouletteId()
        {
            ContentsFinder* contentsFinder = ContentsFinder.Instance();
            if (contentsFinder == null)
                return 0;

            var queueInfo = contentsFinder->GetQueueInfo();
            return queueInfo == null ? (byte)0 : queueInfo->QueuedContentRouletteId;
        }

        private static void OnEnteredDuty()
        {
            RouletteQueueHelper.Instance.StopIfRunning();
            Config.JoinedCount++;
            JudgeStartedAt  = DateTime.UtcNow;
            dutyCompletedAt = null;
            SetState(MentorRouletteState.Judging, "进本判定中…");
        }

        private static void UpdateJudging(MentorRouletteConfig cfg)
        {
            if (!IsInsideDuty)
            {
                // 还没判定就出去了（被踢 / 队友退 / 手动退出）
                EnterCooldown("不在副本内，回到排队");
                return;
            }

            if ((DateTime.UtcNow - JudgeStartedAt).TotalMilliseconds < cfg.DelayBeforeJudgeMs)
                return;

            uint territory = Svc.ClientState.TerritoryType;
            string name    = TerritoryName(territory);

            if (IsSupported(territory))
            {
                Svc.Log.Info($"[MentorRoulette] supported duty: {name} ({territory})");
                Notify($"支持 {name}，开始自动攻略。");
                Plugin.Run(territory, 1, bareMode: !cfg.UseLoopActions);
                SetState(MentorRouletteState.Running, $"攻略中：{name}");
                return;
            }

            if (cfg.UnsupportedAction == MentorUnsupportedAction.KeepDuty)
            {
                Notify($"{name} 没有可用路径，原地挂机等队友完成。");
                if (Plugin.Stage is not (Stage.Stopped or Stage.Paused))
                    Plugin.Stage = Stage.Stopped;
                SetState(MentorRouletteState.Keeping, $"挂机中：{name}");
                return;
            }

            GraceCancelRequested = false;
            Notify($"{name} 没有可用路径，{cfg.LeaveGraceSec} 秒后退出副本（点“取消退出”可留下）。");
            SetState(MentorRouletteState.Grace, $"{name} 不支持，{cfg.LeaveGraceSec} 秒后退出");
        }

        private static void UpdateGrace(MentorRouletteConfig cfg)
        {
            if (!IsInsideDuty)
            {
                EnterCooldown("已经离开副本，回到排队");
                return;
            }

            if (GraceCancelRequested)
            {
                GraceCancelRequested = false;
                Notify("已取消退出，这个本交给你手动处理。");
                if (Plugin.Stage is not (Stage.Stopped or Stage.Paused))
                    Plugin.Stage = Stage.Stopped;
                SetState(MentorRouletteState.Keeping, "手动处理中（已取消退出）");
                return;
            }

            if ((DateTime.UtcNow - StateEnteredAt).TotalSeconds < cfg.LeaveGraceSec)
                return;

            leaveStartedAt = DateTime.UtcNow;
            leaveStep      = 0;
            leaveAttempts  = 0;
            VNavmesh_IPCSubscriber.Path_Stop();
            SetState(MentorRouletteState.Leaving, "退出副本中…");
        }

        private static void UpdateLeaving(MentorRouletteConfig cfg)
        {
            if (!IsInsideDuty)
            {
                FinishLeave(cfg);
                return;
            }

            if ((DateTime.UtcNow - leaveStartedAt).TotalSeconds > cfg.LeaveTimeoutSec && leaveStep >= 2)
            {
                Notify("退本超时，已停止导随。");
                Stop("退本超时");
                return;
            }

            switch (leaveStep)
            {
                case 0:
                    VNavmesh_IPCSubscriber.Path_Stop();

                    if (!PlayerHelper.InCombat || (DateTime.UtcNow - leaveStartedAt).TotalSeconds > cfg.LeaveCombatTimeoutSec)
                        leaveStep = 1;

                    break;
                case 1:
                    if (ExitDutyHelper.State != ActionState.Running)
                    {
                        leaveAttempts++;
                        leaveStep = 2;
                        Svc.Log.Debug($"[MentorRoulette] invoking ExitDutyHelper (attempt {leaveAttempts})");
                        ExitDutyHelper.Invoke();
                    }

                    break;
                case 2:
                    if (ExitDutyHelper.State == ActionState.Running)
                        return;

                    // ExitDutyHelper 结束了但还在本里，重试
                    if ((DateTime.UtcNow - leaveStartedAt).TotalSeconds > cfg.LeaveTimeoutSec)
                    {
                        Notify("多次尝试仍未退出副本，已停止导随。");
                        Stop("退本失败");
                        return;
                    }

                    leaveStep = 1;
                    break;
            }
        }

        private static void FinishLeave(MentorRouletteConfig cfg)
        {
            ExitDutyHelper.Instance.StopIfRunning();

            cfg.LeftCount++;
            ConsecutiveLeaves++;
            lastLeaveWasAbandon = true;
            Config.LastLeaveAt  = DateTime.UtcNow;

            if (Plugin.Stage is not (Stage.Stopped or Stage.Paused))
                Plugin.Stage = Stage.Stopped;

            if (cfg.MaxLeavesBeforeStop > 0 && ConsecutiveLeaves >= cfg.MaxLeavesBeforeStop)
            {
                Notify($"连续退本 {ConsecutiveLeaves} 次，达到上限，停止导随。");
                Stop("连续退本达到上限");
                return;
            }

            EnterCooldown("已退出不支持的副本，准备重新排队");
        }

        private static void EnterCooldown(string status)
        {
            CooldownUntil = DateTime.UtcNow.AddSeconds(Math.Max(0, Config.RequeueDelaySec));
            SetState(MentorRouletteState.Cooldown, status);
        }

        private static void UpdateCooldown(MentorRouletteConfig cfg)
        {
            if (IsInsideDuty)
            {
                OnEnteredDuty();
                return;
            }

            if (DateTime.UtcNow < CooldownUntil)
                return;

            BeginQueueing();
        }

        private static void UpdateRunning()
        {
            if (!IsInsideDuty)
            {
                if (Plugin.dutyState == DutyState.DutyComplete)
                    Config.CompletedCount++;

                if (Plugin.Stage is not (Stage.Stopped or Stage.Paused))
                    Plugin.Stage = Stage.Stopped;

                ConsecutiveLeaves   = 0;
                lastLeaveWasAbandon = false;
                dutyCompletedAt     = null;
                EnterCooldown("副本结束，准备重新排队");
                return;
            }

            // 插件停在本里超过 2 分钟又没打完（多半是路径失败/被卡住），退本重排
            if (Plugin.Stage is Stage.Stopped && Plugin.dutyState != DutyState.DutyComplete)
            {
                stoppedInsideSince ??= DateTime.UtcNow;

                if ((DateTime.UtcNow - stoppedInsideSince.Value).TotalSeconds > 120 &&
                    ExitDutyHelper.State != ActionState.Running)
                {
                    Notify("AutoDuty 停在本里没在跑（可能路径失败），退出这个副本重新排队。");
                    ExitDutyHelper.Invoke();
                }
            }
            else
                stoppedInsideSince = null;

            // 副本打完了但还站在里面（AutoExitDuty 关闭时），替用户退本
            if (Plugin.dutyState == DutyState.DutyComplete)
            {
                dutyCompletedAt ??= DateTime.UtcNow;

                if ((DateTime.UtcNow - dutyCompletedAt.Value).TotalSeconds > 15 &&
                    Plugin.Stage is Stage.Stopped &&
                    ExitDutyHelper.State != ActionState.Running)
                {
                    Svc.Log.Debug("[MentorRoulette] duty completed but still inside, exiting duty");
                    ExitDutyHelper.Invoke();
                }
            }
        }

        private static void UpdateKeeping()
        {
            if (IsInsideDuty)
                return;

            if (Plugin.dutyState == DutyState.DutyComplete)
                Config.CompletedCount++;

            ConsecutiveLeaves = 0;
            lastLeaveWasAbandon = false;
            EnterCooldown("离开副本，准备重新排队");
        }

        internal static string StateText =>
            State switch
            {
                MentorRouletteState.Stopped  => Loc.Get("MentorTab.State.Stopped"),
                MentorRouletteState.Queueing => Loc.Get("MentorTab.State.Queueing"),
                MentorRouletteState.Judging  => Loc.Get("MentorTab.State.Judging"),
                MentorRouletteState.Grace    => Loc.Get("MentorTab.State.Grace"),
                MentorRouletteState.Leaving  => Loc.Get("MentorTab.State.Leaving"),
                MentorRouletteState.Cooldown => Loc.Get("MentorTab.State.Cooldown"),
                MentorRouletteState.Running  => Loc.Get("MentorTab.State.Running"),
                MentorRouletteState.Keeping  => Loc.Get("MentorTab.State.Keeping"),
                _                            => State.ToString()
            };
    }
}
