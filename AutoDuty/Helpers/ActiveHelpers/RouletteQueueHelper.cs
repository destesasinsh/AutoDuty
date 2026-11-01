using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace AutoDuty.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Configurations;
    using FFXIVClientStructs.Interop;
    using FFXIVClientStructs.STD;

    /// <summary>
    /// 排“随机任务”（例如导师随机任务）的 ActiveHelper。
    ///
    /// 与 QueueHelper 的区别：QueueHelper 排的是具体副本（ContentFinderCondition），
    /// 这里用 AgentContentsFinder.OpenRouletteDuty 直接把某个 ContentRoulette 加载进任务搜索器再点加入。
    /// 原生接口用不了时退回“按名字在列表里选中”的兜底路径。
    /// </summary>
    internal unsafe class RouletteQueueHelper : ActiveHelperBase<RouletteQueueHelper, LoopActionConfigBare>
    {
        public override string Name        => nameof(RouletteQueueHelper);
        public override string DisplayName => "Queueing Roulette";

        private static uint _rouletteRowId = 8;
        private static bool _joinFired     = false;
        private static int  _openAttempts  = 0;

        private AddonContentsFinder* _addon;

        /// <summary>导师随机任务在 ContentRoulette 里的行号。</summary>
        internal const uint MENTOR_ROULETTE_ID = 9;

        internal static uint ResolveRouletteId(uint configured) =>
            configured == 0 ? MENTOR_ROULETTE_ID : configured;

        internal static void Invoke(uint rouletteRowId)
        {
            _rouletteRowId = ResolveRouletteId(rouletteRowId);
            _joinFired     = false;
            _openAttempts  = 0;
            Instance.Start();
        }

        internal override void Stop()
        {
            _joinFired = false;
            base.Stop();
        }

        private static string RouletteName
        {
            get
            {
                try
                {
                    ContentRoulette? row = Svc.Data.GetExcelSheet<ContentRoulette>().GetRowOrDefault(_rouletteRowId);
                    if (row.HasValue)
                        return row.Value.Name.ToString().Trim();
                }
                catch (Exception ex)
                {
                    Svc.Log.Error($"{nameof(RouletteQueueHelper)}: failed to read ContentRoulette {_rouletteRowId}: {ex.Message}");
                }

                return string.Empty;
            }
        }

        private static bool ContentsFinderConfirm()
        {
            if (GenericHelpers.TryGetAddonByName("ContentsFinderConfirm", out AtkUnitBase* addonContentsFinderConfirm) &&
                GenericHelpers.IsAddonReady(addonContentsFinderConfirm))
            {
                Svc.Log.Debug($"{nameof(RouletteQueueHelper)}: confirming duty pop");
                AddonHelper.FireCallBack(addonContentsFinderConfirm, true, 8);
                return true;
            }

            return false;
        }

        protected override void HelperUpdate(IFramework framework)
        {
            if (InDungeon)
            {
                this.Stop();
                return;
            }

            if (!EzThrottler.Throttle(this.Name, 250) || !PlayerHelper.IsReadyFull)
                return;

            if (ContentsFinderConfirm())
                return;

            if (Conditions.Instance()->InDutyQueue)
            {
                _joinFired = true;
                return;
            }

            try
            {
                this.QueueRoulette();
            }
            catch (Exception ex)
            {
                Svc.Log.Error($"{nameof(RouletteQueueHelper)}: {ex}");
            }
        }

        private void QueueRoulette()
        {
            if (!GenericHelpers.TryGetAddonByName("ContentsFinder", out this._addon) ||
                !GenericHelpers.IsAddonReady((AtkUnitBase*)this._addon))
            {
                if (!EzThrottler.Throttle("RouletteQueueHelper-Open", 3000))
                    return;

                if (!AgentHUD.Instance()->IsMainCommandEnabled(33))
                    return;

                _openAttempts++;

                if (_openAttempts <= 4)
                {
                    Svc.Log.Debug($"{nameof(RouletteQueueHelper)}: OpenRouletteDuty({_rouletteRowId}) attempt {_openAttempts}");
                    AgentContentsFinder.Instance()->OpenRouletteDuty((byte)_rouletteRowId);
                }
                else
                {
                    Svc.Log.Debug($"{nameof(RouletteQueueHelper)}: falling back to /dutyfinder command");
                    Svc.Commands.ProcessCommand("/dutyfinder");
                }

                return;
            }

            if (_joinFired)
                return;

            // 首选：原生接口已经把随机任务选好了，直接点“加入”
            if (IsRouletteSelected())
            {
                Svc.Log.Debug($"{nameof(RouletteQueueHelper)}: roulette {_rouletteRowId} selected, joining");
                _joinFired = true;
                AddonHelper.FireCallBack((AtkUnitBase*)this._addon, true, 12, 0);
                return;
            }

            // 兜底：按名字在列表里找条目并选中
            this.SelectByName();
        }

        private static bool IsRouletteSelected()
        {
            try
            {
                AgentContentsFinder* agent = AgentContentsFinder.Instance();
                return agent != null && agent->HasRouletteSelected;
            }
            catch (Exception ex)
            {
                Svc.Log.Debug($"{nameof(RouletteQueueHelper)}: HasRouletteSelected failed: {ex.Message}");
                return false;
            }
        }

        private void SelectByName()
        {
            string rouletteName = RouletteName;
            if (rouletteName.Length == 0)
            {
                Svc.Log.Error($"{nameof(RouletteQueueHelper)}: could not resolve name of ContentRoulette {_rouletteRowId}");
                this.Stop();
                return;
            }

            AtkComponentTreeList* dutyList = this._addon->DutyList;
            if (dutyList == null || dutyList->Items.LongCount == 0)
                return;

            List<AtkComponentTreeListItem> items = [];
            StdVector<Pointer<AtkComponentTreeListItem>> vectorDutyListItems = dutyList->Items;
            vectorDutyListItems.ForEach(p =>
                                        {
                                            if (p.Value != null)
                                                items.Add(*p.Value);
                                        });

            if (items.Count == 0)
                return;

            int index = -1;
            for (int i = 0; i < items.Count; i++)
            {
                string text = GetItemText(items[i]);

                if (text.Length == 0)
                    continue;

                if (text.Contains(rouletteName, StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                if (EzThrottler.Throttle("RouletteQueueHelper-NoMatch", 5000))
                    Svc.Log.Debug($"{nameof(RouletteQueueHelper)}: roulette '{rouletteName}' not found in duty list ({items.Count} items)");

                return;
            }

            string selectedName = GetSelectedName();

            if (selectedName.Length > 0 && selectedName.Contains(rouletteName, StringComparison.Ordinal))
            {
                Svc.Log.Debug($"{nameof(RouletteQueueHelper)}: joining selected '{rouletteName}'");
                _joinFired = true;
                AddonHelper.FireCallBack((AtkUnitBase*)this._addon, true, 12, 0);
                return;
            }

            if (!EzThrottler.Throttle("RouletteQueueHelper-Select", 500))
                return;

            int headers = 0;
            for (int i = 0; i < index; i++)
                if (items[i].UIntValues[0] is 0 or 1)
                    headers++;

            int selectIndex = index + 1 - headers;

            Svc.Log.Debug($"{nameof(RouletteQueueHelper)}: selecting '{rouletteName}' at list index {index} (arg {selectIndex})");
            AddonHelper.FireCallBack((AtkUnitBase*)this._addon, true, 3, selectIndex);
        }

        private string GetSelectedName()
        {
            try
            {
                return this._addon->AtkValues[18].GetValueAsString()
                           .Replace("\u0002\u001a\u0002\u0002\u0003", string.Empty)
                           .Replace("\u0002\u001a\u0002\u0001\u0003", string.Empty)
                           .Replace("\u0002\u001f\u0001\u0003", "\u2013");
            }
            catch (Exception ex)
            {
                Svc.Log.Debug($"{nameof(RouletteQueueHelper)}: reading selected name failed: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>调试用：把任务搜索器里当前的列表条目打印出来。</summary>
        internal static string DumpList()
        {
            if (!GenericHelpers.TryGetAddonByName("ContentsFinder", out AddonContentsFinder* addon) ||
                !GenericHelpers.IsAddonReady((AtkUnitBase*)addon))
                return "任务搜索器没有打开";

            AtkComponentTreeList* dutyList = addon->DutyList;
            if (dutyList == null)
                return "找不到 DutyList";

            List<AtkComponentTreeListItem> items = [];
            dutyList->Items.ForEach(p =>
                                    {
                                        if (p.Value != null)
                                            items.Add(*p.Value);
                                    });

            StringBuilder sb = new();
            sb.AppendLine($"列表条目 {items.Count} 个，SelectedItemIndex={dutyList->SelectedItemIndex}");
            sb.AppendLine($"HasRouletteSelected={IsRouletteSelected()}，RouletteRowId={_rouletteRowId}");

            for (int i = 0; i < items.Count; i++)
                sb.AppendLine($"[{i}] u0={items[i].UIntValues[0]} '{GetItemText(items[i])}'");

            return sb.ToString();
        }

        private static string GetItemText(AtkComponentTreeListItem item)
        {
            try
            {
                AtkComponentListItemRenderer* renderer = item.Renderer;
                if (renderer == null)
                    return string.Empty;

                AtkTextNode* textNode = renderer->GetTextNodeById(5);
                return textNode == null ? string.Empty : textNode->NodeText.ToString().Trim();
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
