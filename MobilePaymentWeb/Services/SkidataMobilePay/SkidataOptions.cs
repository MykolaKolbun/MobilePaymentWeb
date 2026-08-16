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

        /// <summary>
        /// Name of the manual payment method configured on the Skidata server, used when
        /// booking the payment (sent as ManualPaymentItem.PaymentType). Default "WEB".
        /// </summary>
        public string BookingPaymentType { get; set; } = "WEB";
    }
}
