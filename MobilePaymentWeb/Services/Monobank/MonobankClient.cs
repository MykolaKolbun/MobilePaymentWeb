using MobilePaymentWeb.Models.Payment;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PaymentStatus = MobilePaymentWeb.Models.Payment.PaymentStatus;

namespace MobilePaymentWeb.Services.Monobank
{
    /// <summary>
    /// Monobank acquiring API client (https://api.monobank.ua).
    /// Uses two-phase <c>hold</c> payments so the charge is only finalized after the
    /// Skidata gate booking succeeds — matching the authorize → complete / reverse
    /// flow the controller already drives for iPay. Sandbox vs production is selected
    /// by the merchant token, so there is no separate base URL.
    /// </summary>
    public class MonobankClient : IPaymentProvider
    {
        private const string BaseUrl = "https://api.monobank.ua";
        private const int UAH = 980; // ISO 4217

        private readonly HttpClient _httpClient;
        private readonly ILogger<MonobankClient> _logger;

        private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public MonobankClient(string token, ILogger<MonobankClient> logger)
        {
            _logger = logger;
            _httpClient = new HttpClient { BaseAddress = new Uri(BaseUrl) };
            _httpClient.DefaultRequestHeaders.Add("X-Token", token);
        }

        /// <summary>
        /// Creates a hold invoice. <see cref="PaymentRequest.Amount"/> is already in
        /// kopecks and <see cref="PaymentRequest.Lifetime"/> is in hours.
        /// </summary>
        public async Task<PaymentResponse> CreatePaymentAsync(PaymentRequest request)
        {
            var body = new
            {
                amount = (int)request.Amount,
                ccy = UAH,
                paymentType = "hold",
                redirectUrl = request.SuccessUrl,
                merchantPaymInfo = new { destination = request.Description },
                validity = request.Lifetime.HasValue ? (int)(request.Lifetime.Value * 3600) : (int?)null
            };

            var response = await _httpClient.PostAsJsonAsync("/api/merchant/invoice/create", body, _json);
            var raw = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("Monobank create ({StatusCode}): {Body}", response.StatusCode, raw);

            if (!response.IsSuccessStatusCode)
                return new PaymentResponse
                {
                    IsSuccess = false,
                    ErrorMessage = $"Monobank error {(int)response.StatusCode}: {raw}"
                };

            var created = JsonSerializer.Deserialize<CreateInvoiceResponse>(raw, _json);
            return new PaymentResponse
            {
                Pid = created?.InvoiceId,
                PaymentUrl = created?.PageUrl,
                IsSuccess = !string.IsNullOrEmpty(created?.InvoiceId)
            };
        }

        public async Task<PaymentStatusResponse> GetPaymentStatusAsync(string pid)
        {
            if (string.IsNullOrEmpty(pid))
                return new PaymentStatusResponse { Status = PaymentStatus.Unknown };

            var response = await _httpClient.GetAsync(
                $"/api/merchant/invoice/status?invoiceId={Uri.EscapeDataString(pid)}");
            var raw = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("Monobank status ({StatusCode}): {Body}", response.StatusCode, raw);

            if (!response.IsSuccessStatusCode)
                return new PaymentStatusResponse { Pid = pid, Status = PaymentStatus.Unknown, ErrorMessage = raw };

            var status = JsonSerializer.Deserialize<InvoiceStatusResponse>(raw, _json);
            return new PaymentStatusResponse
            {
                Pid = pid,
                Status = MapStatus(status?.Status),
                ErrorMessage = status?.FailureReason
            };
        }

        /// <summary>Captures the held amount (finalize). Omitting the amount captures the full hold.</summary>
        public Task<PaymentStatusResponse> CompletePaymentAsync(string pid)
            => PostActionAsync("/api/merchant/invoice/finalize", pid);

        /// <summary>Releases the hold / refunds (cancel).</summary>
        public Task<PaymentStatusResponse> ReversePaymentAsync(string pid)
            => PostActionAsync("/api/merchant/invoice/cancel", pid);

        private async Task<PaymentStatusResponse> PostActionAsync(string path, string pid)
        {
            var response = await _httpClient.PostAsJsonAsync(path, new { invoiceId = pid }, _json);
            var raw = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("Monobank {Path} ({StatusCode}): {Body}", path, response.StatusCode, raw);

            if (!response.IsSuccessStatusCode)
                return new PaymentStatusResponse { Pid = pid, Status = PaymentStatus.Unknown, ErrorMessage = raw };

            var parsed = JsonSerializer.Deserialize<InvoiceStatusResponse>(raw, _json);
            return new PaymentStatusResponse { Pid = pid, Status = MapStatus(parsed?.Status) };
        }

        private static PaymentStatus MapStatus(string? status) => status switch
        {
            "created" => PaymentStatus.Registered,
            "processing" => PaymentStatus.Registered,
            "hold" => PaymentStatus.Authorized,
            "success" => PaymentStatus.Success,
            "failure" => PaymentStatus.Failed,
            "expired" => PaymentStatus.Failed,
            "reversed" => PaymentStatus.Cancelled,
            _ => PaymentStatus.Unknown
        };

        private sealed class CreateInvoiceResponse
        {
            public string? InvoiceId { get; set; }
            public string? PageUrl { get; set; }
        }

        private sealed class InvoiceStatusResponse
        {
            public string? Status { get; set; }
            public string? FailureReason { get; set; }
        }
    }
}
