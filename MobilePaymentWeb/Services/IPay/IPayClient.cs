using MobilePaymentWeb.Services.IPay.Models;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace MobilePaymentWeb.Services.IPay
{
    /// <summary>
    /// iPay.ua API client for creating payments (.NET 8)
    /// Endpoint: https://sandbox-checkout.ipay.ua/api302
    /// </summary>
    public class IPayClient
    {
        private readonly string _mchId;
        private readonly string _signKey;
        private readonly HttpClient _httpClient;

        // Switch to "https://checkout.ipay.ua/api302" for production
        private const string SandboxUrl = "https://sandbox-checkout.ipay.ua/api302";
        private const string ProductionUrl = "https://checkout.ipay.ua/api302";

        public IPayClient(string mchId, string signKey, bool sandbox = true)
        {
            _mchId = mchId;
            _signKey = signKey;
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri(sandbox ? SandboxUrl : ProductionUrl);
        }

        // -------------------------------------------------------------------------
        // Public API
        // -------------------------------------------------------------------------

        /// <summary>
        /// Creates a payment and returns the response XML from iPay.
        /// </summary>
        public async Task<IPayResponse> CreatePaymentAsync(CreatePaymentRequest request)
        {
            var xml = BuildRequestXml(request);
            var responseXml = await PostAsync(xml);
            return ParseResponse(responseXml);
        }

        // -------------------------------------------------------------------------
        // Signature helpers
        // -------------------------------------------------------------------------

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

        // -------------------------------------------------------------------------
        // XML builder
        // -------------------------------------------------------------------------

        private string BuildRequestXml(CreatePaymentRequest req)
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
                    new XElement("fail", req.FailUrl),
                    new XElement("result", req.ResultUrl)
                ),

                // Transactions
                new XElement("transactions",
                    new XElement("transaction",
                        new XElement("amount", req.Amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)),
                        new XElement("currency", req.Currency),
                        new XElement("order_id", req.OrderId),
                        new XElement("desc", req.Description)
                    )
                ),

                // Optional fields
                req.Lifetime.HasValue
                    ? new XElement("lifetime", req.Lifetime.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
                    : null,

                !string.IsNullOrEmpty(req.Lang)
                    ? new XElement("lang", req.Lang)
                    : null
            );

            return new XDocument(new XDeclaration("1.0", "UTF-8", null), xml).ToString();
        }

        // -------------------------------------------------------------------------
        // HTTP POST
        // -------------------------------------------------------------------------

        private async Task<string> PostAsync(string xml)
        {
            var content = new FormUrlEncodedContent(new[]
            {
            new KeyValuePair<string, string>("data", xml)
        });

            // Encoding is UTF-8 by default for FormUrlEncodedContent
            var response = await _httpClient.PostAsync("", content);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        // -------------------------------------------------------------------------
        // Response parser
        // -------------------------------------------------------------------------

        private static IPayResponse ParseResponse(string xml)
        {
            try
            {
                var doc = XDocument.Parse(xml);
                var root = doc.Root!; // <payment>

                var statusRaw = root.Element("status")?.Value;
                var statusCode = statusRaw != null && int.TryParse(statusRaw, out var s) ? s : (int?)null;

                return new IPayResponse
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
                return new IPayResponse
                {
                    RawXml = xml,
                    ErrorMessage = $"Failed to parse response: {ex.Message}"
                };
            }
        }
    }

}
