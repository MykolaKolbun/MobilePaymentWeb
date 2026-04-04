namespace MobilePaymentWeb.Services.SkidataMobilePay
{
    public class SkidataOptions
    {
        public string Endpoint { get; set; } = string.Empty;
        public string FacilityID { get; set; } = string.Empty;
        public int ExternalDeviceID { get; set; }
        public int ParkingDeviceID { get; set; }
        public string Login { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
