using MobilePaymentWeb.Models.Payment;
using MobilePaymentWeb.Services.IPay.Models;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using PaymentStatus = MobilePaymentWeb.Models.Payment.PaymentStatus;

namespace MobilePaymentWeb.Services.IPay
{
    /// <summary>
    /// iPay.ua API client for creating payments (.NET 8)
    /// Endpoint: https://sandbox-checkout.ipay.ua/api302
    /// </summary>
    public class IPayClient: IPaymentProvider
    {
        private readonly string _mchId;
        private readonly string _signKey;
        private readonly HttpClient _httpClient;
        private readonly ILogger<IPayClient> _logger;
        // Switch to "https://checkout.ipay.ua/api302" for production
        private const string SandboxUrl = "https://sandbox-checkout.ipay.ua/api302";
        private const string ProductionUrl = "https://checkout.ipay.ua/api302";


        #region Constructor
        public IPayClient(string mchId, string signKey, ILogger<IPayClient> logger, bool sandbox = true)
        {
            _mchId = mchId;
            _signKey = signKey;
            _logger = logger;
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(sandbox ? SandboxUrl : ProductionUrl)
            };
        }
        #endregion


        #region Public API
        /// <summary>
        /// Creates a new payment asynchronously using the specified payment request details.
        /// </summary>
        /// <param name="request">An object containing the details required to create the payment. Cannot be null.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains an object representing the
        /// response to the payment request.</returns>
        public async Task<PaymentResponse> CreatePaymentAsync(PaymentRequest request)
        {
            var iPayRequest = new CreatePaymentRequest
            {
                Amount = request.Amount,
                Description = request.Description,
                GoodUrl = request.GoodUrl,
                BadUrl = request.BadUrl,
                Lifetime = request.Lifetime,
                Lang = request.Lang,
                Info = request.Info
            };
            var xml = BuildPaymentRequestXml(iPayRequest);
            var responseXml = await PostAsync(xml);
            var iPayResponse = ParsePaymentResponse(responseXml);

            return new PaymentResponse
            {
                Pid = iPayResponse.Pid,
                PaymentUrl = iPayResponse.PaymentUrl,
                IsSuccess = iPayResponse.IsSuccess,
                ErrorMessage = iPayResponse.ErrorMessage
            };
        }

        /// <summary>
        /// Gets the current status of a payment by its PID. Returns the response XML from iPay, which includes the status code and other details.
        /// </summary>
        /// <param name="pid"></param>
        /// <returns></returns>
        public async Task<PaymentStatusResponse> GetPaymentStatusAsync(string pid)
        {
            var xml = BuildPaymentStatusXml("status", pid);
            var responseXml = await PostAsync(xml);
            var iPayResponse = ParsePaymentResponse(responseXml);

            return new PaymentStatusResponse
            {
                Pid = iPayResponse.Pid,
                Status = (PaymentStatus)iPayResponse.StatusCode,
                ErrorMessage = iPayResponse.ErrorMessage
            };
        }

        /// <summary>
        /// Gets the receipt file for a completed payment. Returns raw bytes of the receipt (PDF or similar).
        /// </summary>
        /// <param name="pid"></param>
        /// <returns></returns>
        public async Task<byte[]> GetReceiptAsync(string pid)
        {
            var xml = BuildPaymentStatusXml("GetReceipt", pid);
            // receipt is returned as a file, not XML
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("data", xml)
            });
            var response = await _httpClient.PostAsync("", content);
            return await response.Content.ReadAsByteArrayAsync();
        }
        #endregion

        #region Signature helpers
        /// <summary>
        /// Stage 1 – Salt: SHA1 of current Unix timestamp (matches PHP sha1(microtime(true)))
        /// </summary>
        private static string GenerateSalt()
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            var timestampStr = timestamp.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
            return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(timestampStr))).ToLower();
        }

        /// <summary>
        /// Stage 2 – Sign: HMAC-SHA512 of salt using sign_key
        /// </summary>
        private static string GenerateSign(string salt, string signKey)
        {
            var keyBytes = Encoding.UTF8.GetBytes(signKey);
            var saltBytes = Encoding.UTF8.GetBytes(salt);
            var hash = HMACSHA512.HashData(keyBytes, saltBytes);
            return Convert.ToHexString(hash).ToLower();
        }
        #endregion


        #region XML builders
        private string BuildPaymentRequestXml(CreatePaymentRequest req)
        {
            var salt = GenerateSalt();
            var sign = GenerateSign(salt, _signKey);

            var xml = new XElement("payment",

                // Auth
                new XElement("auth",
                    new XElement("mch_id", _mchId),
                    new XElement("salt", salt),
                    new XElement("sign", sign)
                ),

                // URLs
                new XElement("urls",
                    new XElement("good", req.GoodUrl),
                    new XElement("bad", req.BadUrl),
                    new XElement("auto_redirect_good", req.AutoRedirectGood),
                    new XElement("auto_redirect_bad", req.AutoRedirectBad)
                ),

                // Transactions
                new XElement("transactions",
                    new XElement("transaction",
                        new XElement("amount", (int)req.Amount),
                        new XElement("currency", req.Currency),
                        new XElement("desc", req.Description),
                        //new XElement("info", new XCData(System.Text.Json.JsonSerializer.Serialize(req.Info)))
                        new XElement("info", new XCData(System.Text.Json.JsonSerializer.Serialize(req.Info)))

                    )
                ),

                req.Lifetime.HasValue
                    ? new XElement("lifetime", req.Lifetime.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
                    : null,

                !string.IsNullOrEmpty(req.Lang)
                    ? new XElement("lang", req.Lang)
                    : null
            );

            var settings = new XmlWriterSettings
            {
                Encoding = Encoding.UTF8,
                Indent = true
            };
            using var ms = new MemoryStream();
            using var xw = XmlWriter.Create(ms, settings);
            new XDocument(new XDeclaration("1.0", "UTF-8", null), xml).Save(xw);
            xw.Flush();
            return Encoding.UTF8.GetString(ms.ToArray());


        }

        private string BuildPaymentStatusXml(string action, string pid)
        {
            var salt = GenerateSalt();
            var sign = GenerateSign(salt, _signKey);

            var xml = new XElement("payment",
                new XElement("auth",
                    new XElement("mch_id", _mchId),
                    new XElement("salt", salt),
                    new XElement("sign", sign)
                ),
                new XElement("action", action),
                new XElement("pid", pid)
            );
            var settings = new XmlWriterSettings
            {
                Encoding = Encoding.UTF8,
                Indent = true
            };
            using var ms = new MemoryStream();
            using var xw = XmlWriter.Create(ms, settings);
            new XDocument(new XDeclaration("1.0", "UTF-8", null), xml).Save(xw);
            xw.Flush();
            return Encoding.UTF8.GetString(ms.ToArray());
        }
        #endregion


        #region HTTP POST

        private async Task<string> PostAsync(string xml)
        {
            _logger.LogInformation("IPay request: {Xml}", xml);

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("data", xml)
            });

            var response = await _httpClient.PostAsync("", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("IPay response ({StatusCode}): {Body}", response.StatusCode, responseBody);

            if (!response.IsSuccessStatusCode)
                throw new Exception($"iPay error {response.StatusCode}: {responseBody}");

            return responseBody;
        }
        #endregion

        #region Response parser
        private static PaymentResponse ParsePaymentResponse(string xml)
        {
            try
            {
                var doc = XDocument.Parse(xml);
                var root = doc.Root!; // <payment>

                var statusRaw = root.Element("status")?.Value;
                var statusCode = statusRaw != null && int.TryParse(statusRaw, out var s) ? s : (int?)null;
                return new PaymentResponse
                {
                    RawXml = xml,
                    Pid = root.Element("pid")?.Value,
                    StatusCode = statusCode,
                    PaymentUrl = root.Element("url")?.Value,
                    SaltResponse = root.Element("auth")?.Element("salt")?.Value,
                    SignResponse = root.Element("auth")?.Element("sign")?.Value,
                };
            }
            catch (Exception ex)
            {
                return new PaymentResponse
                {
                    RawXml = xml,
                    ErrorMessage = $"Failed to parse response: {ex.Message}"
                };
            }
        }
        #endregion
    }

}
