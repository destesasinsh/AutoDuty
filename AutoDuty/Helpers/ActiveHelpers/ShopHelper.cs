namespace AutoDuty.Helpers;

using Configurations;
using Dalamud.Plugin.Services;
using IPC;

public class ShopHelper : ActiveHelperBase<ShopHelper, ShopLoopActionConfig>
{
    public override string Name        { get; } = nameof(ShopHelper);
    public override string DisplayName { get; } = "Shop";

    public override string[]? Commands           { get; init; } = ["shop"];
    public override string?   CommandDescription { get; init; } = "Executes shopping";

    internal override void Start()
    {
        if (!Knightshopper_IPCSubscriber.IsEnabled)
            return;

        Knightshopper_IPCSubscriber.ResetPurchaseStatus();
        base.Start();
    }

    protected override void HelperUpdate(IFramework framework)
    {
        if (!base.UpdateBase())
            return;

        switch (Knightshopper_IPCSubscriber.GetStatus())
        {
            case Knightshopper_IPCSubscriber.PurchaseState.Unknown:
                if (!Knightshopper_IPCSubscriber.StartShopping(this.ActionConfig.Currency))
                    this.Stop();
                return;
            case Knightshopper_IPCSubscriber.PurchaseState.Running:
                break;
            case Knightshopper_IPCSubscriber.PurchaseState.Failed:
            case Knightshopper_IPCSubscriber.PurchaseState.Cancelled:
            case Knightshopper_IPCSubscriber.PurchaseState.CancellationRequested:
            case Knightshopper_IPCSubscriber.PurchaseState.Succeeded:
                this.Stop();
                return;
            default:
                break;
        }
    }
}