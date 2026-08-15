using System.Text.Json.Serialization;

namespace MobilePaymentWeb.Services.Fiscal.CheckBox.Models
{
    internal class CheckBoxShift
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
    }
}
