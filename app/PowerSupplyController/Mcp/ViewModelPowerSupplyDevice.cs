namespace PowerSupplyController.Mcp;

internal sealed class ViewModelPowerSupplyDevice(MainViewModel viewModel) : IPowerSupplyDevice
{
    public bool IsConnected => viewModel.IsConnected;
    public PowerSupplyData Snapshot => viewModel.CurrentData.Clone();
    public bool Refresh() => viewModel.RefreshNow();
    public bool WriteSetpoints(double voltage, double current) => viewModel.WriteMcpSetpoints(voltage, current);
    public bool WriteOutput(bool enabled) => viewModel.WriteMcpOutput(enabled);
}
