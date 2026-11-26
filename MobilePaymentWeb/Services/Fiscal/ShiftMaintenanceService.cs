namespace MobilePaymentWeb.Services.Fiscal
{
    /// <summary>
    /// Periodically closes CheckBox fiscal shifts that approach the 24h ПРРО limit,
    /// independent of payment activity — so a shift opened just after a scheduled close
    /// can't be left open past 24h (tax-fine risk). Runs every 30 min and closes shifts
    /// older than 23h, keeping the maximum shift age safely under 24h even with no payments.
    /// </summary>
    public class ShiftMaintenanceService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan MaxShiftAge = TimeSpan.FromHours(23);

        private readonly IFiscalProviderFactory _factory;
        private readonly Dictionary<string, FacilityOptions> _facilities;

        public ShiftMaintenanceService(
            IFiscalProviderFactory factory,
            Dictionary<string, FacilityOptions> facilities)
        {
            _factory = factory;
            _facilities = facilities;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                foreach (var facilityId in _facilities.Keys)
                {
                    var fiscal = _factory.GetProvider(facilityId);
                    if (fiscal != null)
                        await fiscal.CloseShiftIfStaleAsync(MaxShiftAge);
                }

                try { await Task.Delay(Interval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
