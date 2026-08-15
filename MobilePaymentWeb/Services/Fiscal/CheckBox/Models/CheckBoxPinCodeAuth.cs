using System.Text.Json.Serialization;

namespace MobilePaymentWeb.Services.Fiscal.CheckBox.Models
{
    internal class CheckBoxPinCodeAuth
    {
        [JsonPropertyName("pin_code")]
        public string PinCode { get; set; } = string.Empty;
    }
}
