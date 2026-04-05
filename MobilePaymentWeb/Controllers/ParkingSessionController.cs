using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MobilePaymentWeb.Models;
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
        private readonly IPayClient _ipay;

        public ParkingSessionController(IOptions<SkidataOptions> skidataOptions, IPayClient ipay)
        {
            _skidata = skidataOptions.Value;
            _ipay = ipay;
        }

        [HttpGet]
        public async Task<IActionResult> ParkingSession(string? ticket, string? licensePlate)
        {

            Identification ticketId;

            if (!string.IsNullOrEmpty(ticket))
                ticketId = new GenericIdentification { Identificator = ticket, Type = "PARK" };
            else if (!string.IsNullOrEmpty(licensePlate))
            {
                licensePlate = ConvertToLatin(licensePlate.Trim().ToUpper());
                ticketId = new LicensePlateIdentification { LicenseNo = licensePlate, Country = CountryCodeEnum.UA };
            }
            else
                return View("LicensePlateForm");

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
                    model = new ParkingSessionViewModel
                    {
                        ExitTime = cardInfo.CardInfo.ExitDateTime.ToString("dd.MM.yyyy HH:mm"),
                        EntryTime = cardInfo.CardInfo.EntryDateTime.ToString("dd.MM.yyyy HH:mm"),
                        CarLP = cardInfo.CardInfo.LicenseNumber,
                        ParkDuration = $"{(int)duration.TotalHours:D2}:{duration.Minutes:D2}"
                        //ParkDuration = $"{(int)(cardInfo.CardInfo.ExitDateTime - cardInfo.CardInfo.EntryDateTime).TotalHours:D2}:{(cardInfo.CardInfo.ExitDateTime - cardInfo.CardInfo.EntryDateTime).Minutes:D2}"
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
                    //ParkDuration = (DateTime.Now - cardInfo.CardInfo.EntryDateTime).ToString(@"hh\:mm")
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

        [HttpPost]
        public async Task<IActionResult> Pay(ParkingSessionViewModel model)
        {
            
            var response = await _ipay.CreatePaymentAsync(new CreatePaymentRequest
            {
                Amount = model.Price * 100,
                //Amount = 1 * 100,
                Description = $"Послуга паркування",
                GoodUrl = "https://mobilepaymentweb-production.up.railway.app/ParkingSession/PaymentSuccess",
                BadUrl = "https://mobilepaymentweb-production.up.railway.app/ParkingSession/PaymentFail",
                AutoRedirectBad = 1,
                AutoRedirectGood = 1,
                Lifetime = 0.5,
                //Info = "{ \"CarLP\": \"" + model.CarLP + "\", \"EntryTime\": \"" + model.EntryTime + "\" }"
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

        [HttpGet]
        public async Task<IActionResult> PaymentSuccess()
        {
            IPayResponse status;
            var attempts = 0;
            const int maxAttempts = 10;
            var pid = HttpContext.Session.GetString("ipay_pid");
            do
            {
                status = await _ipay.GetPaymentStatusAsync(pid);

                if (status.StatusCode == 1)
                    await Task.Delay(2000); // wait 2 seconds before next check

                attempts++;
            }
            while (status.StatusCode == 1 && attempts < maxAttempts);

            if (status.Status == PaymentStatus.Success)
            {
                var receipt = await _ipay.GetReceiptAsync(pid);
                return File(receipt, "application/pdf", "receipt.pdf");
            }

            ViewBag.Error = status.StatusCode == 1
                ? "Час очікування вичерпано. Спробуйте перевірити статус пізніше."
                : $"Платіж неуспішний: {status.ErrorMessage}";
            return View("PaymentFail");
        }

        [HttpGet]
        public IActionResult PaymentFail() => View();
    }
}
