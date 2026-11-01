namespace AutoDuty.Configurations;

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// 导随（导师随机任务）自动化配置。
/// 独立于 AutoDuty 原本的 Looping / Playlist 模式，由 MentorRouletteManager 驱动。
/// </summary>
[JsonObject(MemberSerialization.OptOut)]
public class MentorRouletteConfig
{
    /// <summary>总开关。关闭时管理器不会做任何事。</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>AllSupported = 有路径就跑；Specified = 只有白名单里的副本才跑。</summary>
    public MentorRouletteMode Mode { get; set; } = MentorRouletteMode.AllSupported;

    /// <summary>模式 Specified 使用的白名单（TerritoryType）。</summary>
    public List<uint> Whitelist { get; set; } = [];

    /// <summary>排到不支持的副本时：退本，或者原地挂着让队友打完。</summary>
    public MentorUnsupportedAction UnsupportedAction { get; set; } = MentorUnsupportedAction.LeaveDuty;

    /// <summary>进本后等多久再做判定（等过场动画 / 加载结束）。</summary>
    public int DelayBeforeJudgeMs { get; set; } = 3000;

    /// <summary>决定退出后，聊天提示到真正退本之间的宽限时间（可以手动取消）。</summary>
    public int LeaveGraceSec { get; set; } = 3;

    /// <summary>等脱战的最长时间，超时也强行退。</summary>
    public int LeaveCombatTimeoutSec { get; set; } = 20;

    /// <summary>整个退本流程的超时。</summary>
    public int LeaveTimeoutSec { get; set; } = 90;

    /// <summary>退本 / 出本后隔多久重新排队。</summary>
    public int RequeueDelaySec { get; set; } = 5;

    /// <summary>退本后检测排本惩罚，处于惩罚中则等待，避免空转。</summary>
    public bool PenaltyGuard { get; set; } = true;

    /// <summary>保守等待时长（分钟），与游戏内“放弃任务”惩罚一致。</summary>
    public int PenaltyWaitMinutes { get; set; } = 30;

    /// <summary>连续退本多少次后自动停止，0 = 不限。</summary>
    public int MaxLeavesBeforeStop { get; set; } = 0;

    /// <summary>跑副本时是否执行“轮次之间”的自动动作（修理 / 分解 / 雇员等）。关闭时用 BareMode。</summary>
    public bool UseLoopActions { get; set; } = false;

    /// <summary>状态变化时在聊天窗口提示。</summary>
    public bool ChatNotify { get; set; } = true;

    /// <summary>要排的随机任务行号（ContentRoulette）。导师随机任务默认 8。</summary>
    public uint RouletteRowId { get; set; } = 8;

    [JsonIgnore] public int QueuedCount    { get; set; } = 0;
    [JsonIgnore] public int JoinedCount    { get; set; } = 0;
    [JsonIgnore] public int CompletedCount { get; set; } = 0;
    [JsonIgnore] public int LeftCount      { get; set; } = 0;

    [JsonIgnore] public DateTime LastLeaveAt { get; set; } = DateTime.MinValue;

    internal void ResetSessionStats()
    {
        this.QueuedCount    = 0;
        this.JoinedCount    = 0;
        this.CompletedCount = 0;
        this.LeftCount      = 0;
    }
}
