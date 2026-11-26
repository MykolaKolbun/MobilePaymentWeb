using MobilePaymentWeb.Models.Fiscal;
using MobilePaymentWeb.Services.Fiscal.CheckBox.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace MobilePaymentWeb.Services.Fiscal.CheckBox
{
    public class CheckBoxFiscalClient : IFiscalProvider
    {
        private readonly string _facilityId;
        private readonly FiscalOptions _options;
        private readonly ILogger<CheckBoxFiscalClient> _logger;
        private string? _token;

        private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

        public CheckBoxFiscalClient(string facilityId, FiscalOptions options, ILogger<CheckBoxFiscalClient> logger)
        {
            _facilityId = facilityId;
            _options = options;
            _logger = logger;
        }

        public async Task<FiscalResponse> CreateReceiptAsync(FiscalRequest request)
        {
            try
            {
                await EnsureAuthenticatedAsync();
                await EnsureShiftOpenAsync();

                var purchase = BuildPurchase(request);
                var receiptId = await CreateReceiptAsync(purchase);
                var fiscal = await PollUntilDoneAsync(receiptId);
                var png = await FetchPngAsync(receiptId);

                return new FiscalResponse
                {
                    IsSuccess = true,
                    FiscalCode = fiscal.FiscalCode,
                    ReceiptPng = png
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Facility {FacilityId}: CheckBox fiscal receipt failed", _facilityId);
                return new FiscalResponse { IsSuccess = false, ErrorMessage = ex.Message };
            }
        }

        // Called by the background ShiftMaintenanceService, independent of payments.
        public async Task CloseShiftIfStaleAsync(TimeSpan maxAge)
        {
            try
            {
                await EnsureAuthenticatedAsync();
                using var client = CreateClient(includeToken: true, includeLicense: true);
                var response = await client.GetAsync("api/v1/cashier/shift");
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound || !response.IsSuccessStatusCode)
                    return;

                var shift = await response.Content.ReadFromJsonAsync<CheckBoxShift>(_json);
                if (shift?.Status == "OPENED" && shift.OpenedAt.HasValue
                    && DateTimeOffset.UtcNow - shift.OpenedAt.Value > maxAge)
                {
                    _logger.LogInformation(
                        "Facility {FacilityId}: maintenance closing shift opened {OpenedAt}", _facilityId, shift.OpenedAt);
                    await CloseShiftAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Facility {FacilityId}: shift maintenance failed", _facilityId);
            }
        }

        private async Task EnsureAuthenticatedAsync()
        {
            if (!string.IsNullOrEmpty(_token))
                return;

            using var client = CreateClient(includeToken: false, includeLicense: true);
            var body = JsonContent.Create(new CheckBoxPinCodeAuth { PinCode = _options.PinCode });
            var response = await client.PostAsync("api/v1/cashier/signinPinCode", body);

            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // session already active — sign out first and retry
                using var c2 = CreateClient(includeToken: false, includeLicense: true);
                await c2.PostAsync("api/v1/cashier/signout", null);
                response = await client.PostAsync("api/v1/cashier/signinPinCode", body);
            }

            await EnsureOkAsync(response, "signinPinCode");
            var token = await response.Content.ReadFromJsonAsync<CheckBoxAccessToken>(_json);
            _token = token!.AccessToken;
            _logger.LogInformation("CheckBox: authenticated");
        }

        private async Task EnsureShiftOpenAsync()
        {
            using var client = CreateClient(includeToken: true, includeLicense: true);
            var response = await client.GetAsync("api/v1/cashier/shift");

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                await OpenShiftAsync();
                return;
            }

            await EnsureOkAsync(response, "cashier/shift");
            var shift = await response.Content.ReadFromJsonAsync<CheckBoxShift>(_json);

            if (shift?.Status != "OPENED")
            {
                await OpenShiftAsync();
                return;
            }

            // A ПРРО shift must be closed within 24h; selling on a stale one returns 400.
            // A 24/7 facility always ages the shift out, so close + reopen when it's too old.
            if (shift.OpenedAt.HasValue && DateTimeOffset.UtcNow - shift.OpenedAt.Value > TimeSpan.FromHours(23))
            {
                _logger.LogInformation("CheckBox: shift open >23h, closing and reopening");
                await CloseShiftAsync();
                await OpenShiftAsync();
            }
        }

        private async Task CloseShiftAsync()
        {
            using var client = CreateClient(includeToken: true, includeLicense: true);
            var response = await client.PostAsync("api/v1/shifts/close", null);
            await EnsureOkAsync(response, "shifts/close");

            // Closing is async (CLOSING -> CLOSED) and issues the Z-report; wait before reopening.
            for (int i = 0; i < 15; i++)
            {
                await Task.Delay(1000);
                var check = await client.GetAsync("api/v1/cashier/shift");
                if (check.StatusCode == System.Net.HttpStatusCode.NotFound)
                    break;
                var s = check.IsSuccessStatusCode
                    ? await check.Content.ReadFromJsonAsync<CheckBoxShift>(_json)
                    : null;
                if (s?.Status is null or "CLOSED")
                    break;
            }
        }

        private async Task OpenShiftAsync()
        {
            using var client = CreateClient(includeToken: true, includeLicense: true);
            var response = await client.PostAsync("api/v1/shifts", null);
            if (response.StatusCode != System.Net.HttpStatusCode.Conflict)
                await EnsureOkAsync(response, "shifts");

            // Shift opening is async on the fiscal server (CREATED/OPENING -> OPENED).
            // Selling before the shift is OPENED returns 400, so wait for it here.
            for (int i = 0; i < 10; i++)
            {
                var check = await client.GetAsync("api/v1/cashier/shift");
                if (check.IsSuccessStatusCode)
                {
                    var shift = await check.Content.ReadFromJsonAsync<CheckBoxShift>(_json);
                    if (shift?.Status == "OPENED")
                    {
                        _logger.LogInformation("CheckBox: shift opened");
                        return;
                    }
                }
                await Task.Delay(1000);
            }
            throw new TimeoutException("CheckBox shift did not reach OPENED status");
        }

        private async Task<Guid> CreateReceiptAsync(CheckBoxPurchase purchase)
        {
            using var client = CreateClient(includeToken: true, includeLicense: false);
            var response = await client.PostAsJsonAsync("api/v1/receipts/sell", purchase, _json);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _token = null;
                await EnsureAuthenticatedAsync();
                using var c2 = CreateClient(includeToken: true, includeLicense: false);
                response = await c2.PostAsJsonAsync("api/v1/receipts/sell", purchase, _json);
            }

            await EnsureOkAsync(response, "receipts/sell");
            var receipt = await response.Content.ReadFromJsonAsync<CheckBoxPurchaseResponse>(_json);
            _logger.LogInformation("CheckBox: receipt created {Id}", receipt!.Id);
            return receipt.Id;
        }

        private async Task<CheckBoxPurchaseResponse> PollUntilDoneAsync(Guid receiptId)
        {
            using var client = CreateClient(includeToken: true, includeLicense: false);
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(1000);
                var response = await client.GetAsync($"api/v1/receipts/{receiptId}");
                await EnsureOkAsync(response, "receipts/{id}");
                var receipt = await response.Content.ReadFromJsonAsync<CheckBoxPurchaseResponse>(_json);
                if (receipt?.Status == "DONE")
                {
                    _logger.LogInformation("CheckBox: receipt DONE {FiscalCode}", receipt.FiscalCode);
                    return receipt;
                }
                if (receipt?.Status == "ERROR")
                    throw new Exception($"CheckBox receipt error for {receiptId}");
            }
            throw new TimeoutException($"CheckBox receipt {receiptId} did not reach DONE status");
        }

        private async Task<byte[]> FetchPngAsync(Guid receiptId)
        {
            using var client = CreateClient(includeToken: true, includeLicense: false);
            var response = await client.GetAsync($"api/v1/receipts/{receiptId}/png");
            await EnsureOkAsync(response, "receipts/{id}/png");
            return await response.Content.ReadAsByteArrayAsync();
        }

        private CheckBoxPurchase BuildPurchase(FiscalRequest request)
        {
            var priceKopecks = (int)(request.Amount * 100);
            return new CheckBoxPurchase
            {
                Goods =
                [
                    new CheckBoxGoods
                    {
                        Good = new CheckBoxGood
                        {
                            Code = "PARKING",
                            Name = request.Description,
                            Price = priceKopecks,
                            Tax = [_options.TaxCode == 0 ? null : _options.TaxCode]
                        },
                        Quantity = 1000,
                        Sum = priceKopecks
                    }
                ],
                Payments =
                [
                    new CheckBoxPayment
                    {
                        Type = request.PaymentType,
                        Value = priceKopecks,
                        Label = request.PaymentType == "CASH" ? "Готівка" : "Картка"
                    }
                ]
            };
        }

        // Like EnsureSuccessStatusCode, but includes the CheckBox response body in the
        // exception so the real reason (tax/shift/payload error) reaches the logs + alert.
        private static async Task EnsureOkAsync(HttpResponseMessage response, string op)
        {
            if (response.IsSuccessStatusCode)
                return;
            var body = await response.Content.ReadAsStringAsync();
            if (body.Length > 500)
                body = body[..500];
            throw new HttpRequestException($"CheckBox {op} -> {(int)response.StatusCode}: {body}");
        }

        private HttpClient CreateClient(bool includeToken, bool includeLicense)
        {
            var client = new HttpClient { BaseAddress = new Uri(_options.ApiUrl) };
            client.DefaultRequestHeaders.Add("X-Client-Name", "MobilePaymentWeb");
            client.DefaultRequestHeaders.Add("X-Client-Version", "1.0");
            if (includeLicense)
                client.DefaultRequestHeaders.Add("X-License-Key", _options.LicenseKey);
            if (includeToken && !string.IsNullOrEmpty(_token))
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
            return client;
        }
    }
}
