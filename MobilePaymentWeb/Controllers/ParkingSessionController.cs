using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MobilePaymentWeb.Models;
using MobilePaymentWeb.Models.Fiscal;
using MobilePaymentWeb.Models.Payment;
using MobilePaymentWeb.Services;
using MobilePaymentWeb.Services.SkidataMobilePay;
using SwebMobileService;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text;
using static MobilePaymentWeb.Helpers.Helpers;

namespace MobilePaymentWeb.Controllers
{
    [Route("{facilityId}")]
    public class ParkingSessionController : Controller
    {
        private readonly Dictionary<string, FacilityOptions> _facilities;
        private readonly IPaymentProviderFactory _paymentProviderFactory;
        private readonly IFiscalProviderFactory _fiscalProviderFactory;
        private readonly IMemoryCache _cache;
        private readonly ILogger<ParkingSessionController> _logger;

        public ParkingSessionController(
            Dictionary<string, FacilityOptions> facilities,
            IPaymentProviderFactory paymentProviderFactory,
            IFiscalProviderFactory fiscalProviderFactory,
            IMemoryCache cache,
            ILogger<ParkingSessionController> logger)
        {
            _facilities = facilities;
            _paymentProviderFactory = paymentProviderFactory;
            _fiscalProviderFactory = fiscalProviderFactory;
            _cache = cache;
            _logger = logger;
        }

        [HttpGet("")]
        [HttpGet("{ticket}")]
        public async Task<IActionResult> ParkingSession(string facilityId, string? ticket, string? licensePlate)
        {
            if (!_facilities.TryGetValue(facilityId, out var facility))
                return View("InvalidFacility");

            var skidata = facility.Skidata;
            ViewBag.FacilityId = facilityId;
            ViewBag.Logo = facility.Display.Logo;
            ViewBag.FacilityName = facility.Display.Name;

            Identification ticketId;
            string? identifier = null;

            if (!string.IsNullOrEmpty(ticket))
            {
                identifier = ticket;
                if (!System.Text.RegularExpressions.Regex.IsMatch(identifier, @"^\d{1,30}$"))
                {
                    ViewBag.Error = "Невірний формат талону";
                    return View("LicensePlateForm");
                }
                ticketId = new GenericIdentification { Identificator = identifier, Type = "PARK" };
            }
            else if (!string.IsNullOrEmpty(licensePlate))
            {
                identifier = licensePlate;
                licensePlate = ConvertToLatin(identifier.Trim().ToUpper());
                if (!System.Text.RegularExpressions.Regex.IsMatch(identifier, @"^[A-Z0-9]{3,10}$"))
                {
                    ViewBag.Error = "Невірний формат номеру авто";
                    return View("LicensePlateForm");
                }
                ticketId = new LicensePlateIdentification { LicenseNo = identifier, Country = CountryCodeEnum.UA };
            }
            else
            {
                return View("LicensePlateForm");
            }

            var client = CreateSkidataClient(skidata);
            ParkingSessionViewModel model;
            try
            {
                var cardInfo = await client.GetCardInfoAsync($"APT.FAC.{skidata.FacilityID}", ticketId, skidata.ExternalDeviceID.ToString(), skidata.ParkingDeviceID.ToString());
                if (cardInfo.CardInfo == null)
                {
                    ViewBag.Error = "Картка не знайдена або не активна.";
                    return View("ParkingSessionError");
                }
                var kyivZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kiev");
                var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, kyivZone);
                var duration = now - cardInfo.CardInfo.EntryDateTime;
                if (cardInfo.CardInfo.ExitDateTime > new DateTime(1994, 1, 1))
                {
                    duration = cardInfo.CardInfo.ExitDateTime - cardInfo.CardInfo.EntryDateTime;
                    model = new ParkingSessionViewModel
                    {
                        FacilityId = facilityId,
                        ExitTime = cardInfo.CardInfo.ExitDateTime.ToString("dd.MM.yyyy HH:mm"),
                        EntryTime = cardInfo.CardInfo.EntryDateTime.ToString("dd.MM.yyyy HH:mm"),
                        CarLP = cardInfo.CardInfo.LicenseNumber,
                        ParkDuration = $"{(int)duration.TotalHours:D2}:{duration.Minutes:D2}"
                    };
                    return View("ParkingSessionExit", model);
                }
                var rateInfo = await client.GetRateInfoAsync($"APT.FAC.{skidata.FacilityID}", ticketId, skidata.ExternalDeviceID.ToString(), skidata.ParkingDeviceID.ToString());

                model = new ParkingSessionViewModel
                {
                    FacilityId = facilityId,
                    Price = rateInfo.RateInfo.Price.Amount,
                    EntryTime = cardInfo.CardInfo.EntryDateTime.ToString("dd.MM.yyyy HH:mm"),
                    CarLP = cardInfo.CardInfo.LicenseNumber,
                    ParkDuration = $"{(int)duration.TotalHours:D2}:{duration.Minutes:D2}",
                    TicketIdentifier = identifier!,
                    IdentifierType = !string.IsNullOrEmpty(ticket) ? "PARK" : "LP",
                    RateEnd = rateInfo.RateInfo.RateEndSpecified ? rateInfo.RateInfo.RateEnd : null
                };
            }
            catch (FaultException ex)
            {
                var fault = SkidataFault.Parse(ex.Message);
                var error = new ParkingSessionErrorModel
                {
                    Identifier = identifier,
                    ErrorMessage = fault.FriendlyMessage,
                };
                return View("ParkingSessionError", error);
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Помилка при отриманні даних: " + ex.Message;
                return View("Error");
            }
            finally
            {
                if (client.State == CommunicationState.Faulted)
                    client.Abort();
                else
                    client.Close();
            }

            return View(model);
        }

        [HttpPost("Pay")]
        public async Task<IActionResult> Pay(string facilityId, ParkingSessionViewModel model)
        {
            if (!_facilities.ContainsKey(facilityId))
                return View("InvalidFacility");

            var paymentProvider = _paymentProviderFactory.GetProvider(facilityId);
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var response = await paymentProvider.CreatePaymentAsync(new PaymentRequest
            {
                Amount = model.Price * 100,
                Description = "Послуга паркування",
                SuccessUrl = $"{baseUrl}/success",
                FailUrl = $"{baseUrl}/fail",
                Lifetime = 0.5,
                Lang = "uk",
                Currency = "UAH",
                Info = new { model.CarLP, model.EntryTime }
            });
            if (response.IsSuccess)
            {
                HttpContext.Session.SetString("payment_pid", response.Pid!);
                HttpContext.Session.SetString("facilityId", facilityId);
                HttpContext.Session.SetString("payment_amount", model.Price.ToString());
                HttpContext.Session.SetString("ticket_id", model.TicketIdentifier);
                HttpContext.Session.SetString("ticket_type", model.IdentifierType);
                if (model.RateEnd.HasValue)
                    HttpContext.Session.SetString("rate_end", model.RateEnd.Value.ToString("O"));
                return Redirect(response.PaymentUrl!);
            }
            ViewBag.Error = "Помилка при створенні платежу: " + response.ErrorMessage;
            return View("Error");
        }

        [HttpGet("/status")]
        public async Task<IActionResult> CheckPaymentStatus()
        {
            var pid = HttpContext.Session.GetString("payment_pid");
            var facilityId = HttpContext.Session.GetString("facilityId") ?? string.Empty;
            var paymentProvider = _paymentProviderFactory.GetProvider(facilityId);
            var status = await paymentProvider.GetPaymentStatusAsync(pid);

            if (status.Status == Models.Payment.PaymentStatus.Authorized)
            {
                var bookedKey = $"booked_{pid}";
                if (!_cache.TryGetValue(bookedKey, out bool _))
                {
                    var bookingOk = await BookPaymentAsync(pid, facilityId);
                    if (bookingOk)
                    {
                        await paymentProvider.CompletePaymentAsync(pid ?? string.Empty);
                        _cache.Set(bookedKey, true, TimeSpan.FromHours(1));
                    }
                    else
                    {
                        await paymentProvider.ReversePaymentAsync(pid ?? string.Empty);
                        return Json(new { statusCode = (int)Models.Payment.PaymentStatus.Cancelled, hasFiscal = false });
                    }
                }
                // iPay transitions to status 5 after completion — client polls again, fiscal handled there
                return Json(new { statusCode = (int)Models.Payment.PaymentStatus.Authorized, hasFiscal = false });
            }

            if (status.Status == Models.Payment.PaymentStatus.Success)
            {
                var bookedKey = $"booked_{pid}";
                if (!_cache.TryGetValue(bookedKey, out bool _))
                {
                    await BookPaymentAsync(pid, facilityId);
                    _cache.Set(bookedKey, true, TimeSpan.FromHours(1));
                }

                var cacheKey = $"receipt_png_{pid}";
                var hasFiscal = _cache.TryGetValue(cacheKey, out byte[]? _);

                if (!hasFiscal)
                {
                    var fiscal = _fiscalProviderFactory.GetProvider(facilityId);
                    if (fiscal != null && decimal.TryParse(HttpContext.Session.GetString("payment_amount"), out var amount))
                    {
                        var fiscalResult = await fiscal.CreateReceiptAsync(new FiscalRequest
                        {
                            Amount = amount,
                            Description = "Послуга паркування",
                            PaymentType = "CARD",
                            ExternalId = pid ?? string.Empty
                        });
                        if (fiscalResult.IsSuccess && fiscalResult.ReceiptPng != null)
                        {
                            _cache.Set(cacheKey, fiscalResult.ReceiptPng, TimeSpan.FromMinutes(5));
                            hasFiscal = true;
                        }
                    }
                }

                return Json(new { statusCode = (int)status.Status, hasFiscal });
            }

            return Json(new { statusCode = (int)status.Status, hasFiscal = false });
        }

        [HttpGet("/receipt-image")]
        public IActionResult ReceiptImage()
        {
            var pid = HttpContext.Session.GetString("payment_pid");
            if (!string.IsNullOrEmpty(pid) && _cache.TryGetValue($"receipt_png_{pid}", out byte[]? png) && png != null)
                return File(png, "image/png");
            return NotFound();
        }

        [HttpGet("/success")]
        public IActionResult PaymentSuccess()
        {
            var pid = HttpContext.Session.GetString("payment_pid");
            if (string.IsNullOrEmpty(pid))
                return NotFound();

            return View();
        }

        [HttpGet("/fail")]
        public IActionResult PaymentFail()
        {
            var pid = HttpContext.Session.GetString("payment_pid");
            if (string.IsNullOrEmpty(pid))
                return NotFound();

            return View();
        }

        private async Task<bool> BookPaymentAsync(string? pid, string facilityId)
        {
            if (!_facilities.TryGetValue(facilityId, out var facility)) return false;

            var ticketIdStr = HttpContext.Session.GetString("ticket_id");
            var ticketType = HttpContext.Session.GetString("ticket_type");
            var ticketId = BuildTicketId(ticketIdStr, ticketType);
            if (ticketId == null) return false;

            var skidata = facility.Skidata;
            var client = CreateSkidataClient(skidata);
            try
            {
                Money bookingPrice;

                var rateEndStr = HttpContext.Session.GetString("rate_end");
                var rateStillValid = rateEndStr != null
                    && DateTime.TryParse(rateEndStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var rateEnd)
                    && DateTime.UtcNow < rateEnd;

                if (rateStillValid
                    && decimal.TryParse(HttpContext.Session.GetString("payment_amount"), out var paidAmount))
                {
                    bookingPrice = new Money { Amount = paidAmount, CurrencyCode = CurrencyCodeEnum.UAH };
                }
                else
                {
                    var rateInfo = await client.GetRateInfoAsync(
                        $"APT.FAC.{skidata.FacilityID}", ticketId,
                        skidata.ExternalDeviceID.ToString(), skidata.ParkingDeviceID.ToString());
                    bookingPrice = rateInfo.RateInfo.Price;

                    if (decimal.TryParse(HttpContext.Session.GetString("payment_amount"), out var paid)
                        && paid != bookingPrice.Amount)
                    {
                        _logger.LogWarning(
                            "BookPayment price drift: paid {Paid} UAH, current rate {Current} UAH, pid {Pid}",
                            paid, bookingPrice.Amount, pid);
                    }
                }

                await client.BookPaymentAsync(
                    $"APT.FAC.{skidata.FacilityID}",
                    ticketId,
                    new CreditCardPaymentItem
                    {
                        PaymentItemId = pid ?? string.Empty,
                        Amount = bookingPrice,
                        CreditCardCompany = "ONLINE",
                        ReferenceNumber = pid ?? string.Empty,
                        ReceiptData = string.Empty
                    },
                    skidata.ExternalDeviceID.ToString(),
                    skidata.ParkingDeviceID.ToString());

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BookPayment failed for pid {Pid}", pid);
                return false;
            }
            finally
            {
                if (client.State == CommunicationState.Faulted) client.Abort();
                else client.Close();
            }
        }

        private static Identification? BuildTicketId(string? id, string? type) =>
            (id, type) switch
            {
                ({ } i, "PARK") => new GenericIdentification { Identificator = i, Type = "PARK" },
                ({ } i, "LP") => new LicensePlateIdentification { LicenseNo = i, Country = CountryCodeEnum.UA },
                _ => null
            };

        private static ParkingTicketManagementServiceV4Client CreateSkidataClient(SkidataOptions skidata)
        {
            var binding = new CustomBinding(
                new TextMessageEncodingBindingElement(MessageVersion.Soap11, Encoding.UTF8),
                new HttpsTransportBindingElement
                {
                    AuthenticationScheme = System.Net.AuthenticationSchemes.Basic
                }
            );
            var client = new ParkingTicketManagementServiceV4Client(binding, new EndpointAddress(skidata.Endpoint));
            client.Endpoint.EndpointBehaviors.Add(new MessageLogger());
            client.ClientCredentials.UserName.UserName = skidata.Login;
            client.ClientCredentials.UserName.Password = skidata.Password;
            return client;
        }
    }
}
