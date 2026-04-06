using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MobilePaymentWeb.Models;
using MobilePaymentWeb.Models.Payment;
using MobilePaymentWeb.Services;
using MobilePaymentWeb.Services.IPay;
using MobilePaymentWeb.Services.IPay.Models;
using MobilePaymentWeb.Services.SkidataMobilePay;
using SwebMobileService;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text;
using static MobilePaymentWeb.Helpers.Helpers;

namespace MobilePaymentWeb.Controllers
{
    public class ParkingSessionController : Controller
    {
        private readonly SkidataOptions _skidata;
        private readonly IPaymentProvider _paymentProvider;

        public ParkingSessionController(IOptions<SkidataOptions> skidataOptions, IPaymentProvider paymentProvider)
        {
            _skidata = skidataOptions.Value;
            _paymentProvider = paymentProvider;
        }

        /// <summary>
        /// Retrieves and displays information about a parking session based on a ticket number or license plate.
        /// </summary>
        /// <remarks>If both parameters are null or empty, the method displays a form for entering a
        /// license plate. If the parking session cannot be found or an error occurs while retrieving data, an error
        /// view is returned. The method communicates with an external parking management service and may display
        /// different views depending on the session state (active or exited).</remarks>
        /// <param name="ticket">The parking ticket number used to identify the parking session. If specified, this value takes precedence
        /// over the license plate. Can be null or empty.</param>
        /// <param name="licensePlate">The vehicle's license plate number used to identify the parking session if no ticket is provided. Can be
        /// null or empty.</param>
        /// <returns>An IActionResult that renders the parking session view with session details, an error view if the session
        /// cannot be found, or a form view if no identification is provided.</returns>
        [HttpGet]
        public async Task<IActionResult> ParkingSession(string? ticket, string? licensePlate)
        {

            Identification ticketId;

            if (!string.IsNullOrEmpty(ticket))
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(ticket, @"^\d{1,20}$"))
                {
                    ViewBag.Error = "Невірний формат талону";
                    return View("LicensePlateForm");
                }
                ticketId = new GenericIdentification { Identificator = ticket, Type = "PARK" };
            }
            else if (!string.IsNullOrEmpty(licensePlate))
            {
                licensePlate = ConvertToLatin(licensePlate.Trim().ToUpper());
                if (!System.Text.RegularExpressions.Regex.IsMatch(licensePlate, @"^[A-Z0-9]{3,10}$"))
                {
                    ViewBag.Error = "Невірний формат номеру авто";
                    return View("LicensePlateForm");
                }
                ticketId = new LicensePlateIdentification { LicenseNo = licensePlate, Country = CountryCodeEnum.UA };
            }
            else
            {    
                return View("LicensePlateForm");
            }

            var binding = new CustomBinding(
                    new TextMessageEncodingBindingElement(MessageVersion.Soap11, Encoding.UTF8),
                    new HttpsTransportBindingElement
                    {
                        AuthenticationScheme = System.Net.AuthenticationSchemes.Basic
                    }
            );
            var endpoint = new EndpointAddress(_skidata.Endpoint);
            var client = new ParkingTicketManagementServiceV4Client(binding, endpoint);

            client.Endpoint.EndpointBehaviors.Add(new MessageLogger());
            client.ClientCredentials.UserName.UserName = _skidata.Login;
            client.ClientCredentials.UserName.Password = _skidata.Password;

            ParkingSessionViewModel model;
            try
            {
                var cardInfo = await client.GetCardInfoAsync($"APT.FAC.{_skidata.FacilityID}", ticketId, _skidata.ExternalDeviceID.ToString(), _skidata.ParkingDeviceID.ToString());
                if (cardInfo.CardInfo == null)
                {
                    ViewBag.Error = "Картка не знайдена або не активна.";
                    return View("ParkingSessionError");
                }
                var duration = DateTime.Now - cardInfo.CardInfo.EntryDateTime;
                if (cardInfo.CardInfo.ExitDateTime > new DateTime(1994, 1, 1))
                {
                    duration = cardInfo.CardInfo.ExitDateTime - cardInfo.CardInfo.EntryDateTime;
                    model = new ParkingSessionViewModel
                    {
                        ExitTime = cardInfo.CardInfo.ExitDateTime.ToString("dd.MM.yyyy HH:mm"),
                        EntryTime = cardInfo.CardInfo.EntryDateTime.ToString("dd.MM.yyyy HH:mm"),
                        CarLP = cardInfo.CardInfo.LicenseNumber,
                        ParkDuration = $"{(int)duration.TotalHours:D2}:{duration.Minutes:D2}"
                        
                    };
                    return View("ParkingSessionExit", model);
                }
                var rateInfo = await client.GetRateInfoAsync($"APT.FAC.{_skidata.FacilityID}", ticketId, _skidata.ExternalDeviceID.ToString(), _skidata.ParkingDeviceID.ToString());
                
                model = new ParkingSessionViewModel
                {
                    Price = rateInfo.RateInfo.Price.Amount,
                    EntryTime = cardInfo.CardInfo.EntryDateTime.ToString("dd.MM.yyyy HH:mm"),
                    CarLP = cardInfo.CardInfo.LicenseNumber,
                    ParkDuration = $"{(int)duration.TotalHours:D2}:{duration.Minutes:D2}"
                };
            }
            catch (Exception ex)
            {
                // Обробка помилок при виклику сервісу
                ViewBag.Error = "Помилка при отриманні даних: " + ex.Message;
                return View("Error");
            }
            finally
            {
                if (client.State == System.ServiceModel.CommunicationState.Faulted)
                    client.Abort();
                else
                    client.Close();
            }

            return View(model);
        }

        /// <summary>
        /// Initiates a payment process for a parking session using the provided session details.
        /// </summary>
        /// <remarks>If the payment request is successful, the method stores the payment identifier in the
        /// session and redirects the user to the payment provider. If the request fails, an error message is displayed.
        /// The method is intended to be used as an HTTP POST action.</remarks>
        /// <param name="model">The parking session information, including price, car license plate, and entry time, used to create the
        /// payment request. Cannot be null.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains an IActionResult that redirects
        /// the user to the payment page if the payment request is successful; otherwise, displays an error view.</returns>
        [HttpPost]
        public async Task<IActionResult> Pay(ParkingSessionViewModel model)
        {
            
            var response = await _paymentProvider.CreatePaymentAsync(new PaymentRequest
            {
                Amount = model.Price * 100,
                Description = $"Послуга паркування",
                SuccessUrl = "https://mobilepaymentweb-production.up.railway.app/success",
                FailUrl = "https://mobilepaymentweb-production.up.railway.app/fail",
                Lifetime = 0.5,
                Lang = "uk",
                Currency = "UAH",
                Info = new { model.CarLP, model.EntryTime }
            });
            if (response.IsSuccess)
            {
                HttpContext.Session.SetString("ipay_pid", response.Pid!);
                return Redirect(response.PaymentUrl!);
            }
            ViewBag.Error = "Помилка при створенні платежу: " + response.ErrorMessage;
            return View("Error");
        }

        /// <summary>
        /// Retrieves the current payment status for the session's payment identifier.
        /// </summary>
        /// <remarks>This method expects a valid payment identifier to be present in the session under the
        /// key "ipay_pid". If the session does not contain this key or the payment identifier is invalid, the returned
        /// status code may indicate an error or unknown status. This endpoint is typically used to poll for payment
        /// status updates after initiating a payment process.</remarks>
        /// <returns>An <see cref="JsonResult"/> containing an object with the payment status code for the current session.</returns>
        [HttpGet("/status")]
        public async Task<IActionResult> CheckPaymentStatus()
        {
            var pid = HttpContext.Session.GetString("ipay_pid");
            var status = await _paymentProvider.GetPaymentStatusAsync(pid);
            return Json(new { statusCode = (int)status.Status });
        }

        /// <summary>
        /// Handles the callback for a successful payment and displays the payment success view to the user.
        /// </summary>
        /// <remarks>This action is typically invoked after a payment process completes successfully. It
        /// can be used as a redirect target from payment gateways or internal payment status checks.</remarks>
        /// <returns>A view that confirms the successful completion of the payment.</returns>
        [HttpGet("/success")]
        public IActionResult PaymentSuccess()
        {
            var pid = HttpContext.Session.GetString("ipay_pid");
            if (string.IsNullOrEmpty(pid))
                return RedirectToAction("ParkingSession");

            return View();
        }

        /// <summary>
        /// Returns the view that informs the user of a failed payment attempt.
        /// </summary>
        /// <returns>An <see cref="IActionResult"/> that renders the payment failure view to the client.</returns>
        [HttpGet("/fail")]
        public IActionResult PaymentFail()
        {
            var pid = HttpContext.Session.GetString("ipay_pid");
            if (string.IsNullOrEmpty(pid))
                return RedirectToAction("ParkingSession");

            return View();
        }
    }
}
