namespace MobilePaymentWeb.Models
{
    public class ParkingSessionViewModel
    {
        public string FacilityId { get; set; }
        public string ParkDuration { get; set; }
        public string ExitTime { get; set; }
        public decimal Price { get; set; }
        public string EntryTime { get; set; }
        public string CarLP { get; set; }
        public string TicketIdentifier { get; set; } = string.Empty;
        public string IdentifierType { get; set; } = string.Empty;
        public DateTime? RateEnd { get; set; }
    }
}
