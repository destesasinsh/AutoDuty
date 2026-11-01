namespace AutoDuty.External;

using Dalamud.Hooking;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;

public static class InstanceContentCrucible
{
    public unsafe delegate void UseItemDelegate(InstanceContentDirector* thisPtr, uint slot, int beastId);

    private static Hook<UseItemDelegate> useItemDelegate = null!;

    static unsafe InstanceContentCrucible()
    {
        useItemDelegate ??= Svc.Hook.HookFromSignature<UseItemDelegate>("E8 ?? ?? ?? ?? 83 7F 44 00 48 8D 57 44", UseItem);
        useItemDelegate.Enable();
    }

    public static void Dispose()
    {
        useItemDelegate?.Dispose();
    }

    public static unsafe void UseItem(uint slot, int beastId)
    {
        if (!useItemDelegate.IsEnabled)
        {
            InteropGenerator.Runtime.ThrowHelper.ThrowNullAddress("InstanceContentCrucible.UseItem", "E8 ?? ?? ?? ?? 83 7F 44 00 48 8D 57 44");
        }
        UseItem(EventFramework.Instance()->GetInstanceContentDirector(), slot, beastId);
    }

    private static unsafe void UseItem(InstanceContentDirector* instance, uint slot, int beastId)
    {
        useItemDelegate.OriginalDisposeSafe(instance, slot, beastId);
    }
}