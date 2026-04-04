namespace MobilePaymentWeb.Services.IPay.Models
{
    public class IPayResponse
    {
        /// <summary>Payment ID in iPay system</summary>
        public string? Pid { get; set; }

        /// <summary>
        /// Raw status code:
        /// 1 = Registered, 3 = Authorized, 4 = Failed, 5 = Success
        /// </summary>
        public int? StatusCode { get; set; }

        public PaymentStatus Status => StatusCode switch
        {
            1 => PaymentStatus.Registered,
            3 => PaymentStatus.Authorized,
            4 => PaymentStatus.Failed,
            5 => PaymentStatus.Success,
            _ => PaymentStatus.Unknown
        };

        /// <summary>URL to redirect the user to complete payment</summary>
        public string? PaymentUrl { get; set; }

        /// <summary>Salt from iPay response signature</summary>
        public string? SaltResponse { get; set; }

        /// <summary>Sign from iPay response signature</summary>
        public string? SignResponse { get; set; }

        /// <summary>Set only when parsing fails</summary>
        public string? ErrorMessage { get; set; }

        public string? RawXml { get; set; }

        public bool IsSuccess =>
            string.IsNullOrEmpty(ErrorMessage) && !string.IsNullOrEmpty(PaymentUrl);

        public override string ToString() =>
            IsSuccess
                ? $"✅ Payment registered | PID: {Pid} | Status: {Status} | URL: {PaymentUrl}"
                : $"❌ Parse error: {ErrorMessage}";
    }

    public enum PaymentStatus
    {
        Unknown = 0,
        Registered = 1,   // Зареєстрований
        Authorized = 3,   // Авторизований
        Failed = 4,   // Неуспішний
        Success = 5    // Успішний
    }
}
