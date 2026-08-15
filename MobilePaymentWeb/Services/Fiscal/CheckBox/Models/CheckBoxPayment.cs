using System.Text.Json.Serialization;

namespace MobilePaymentWeb.Services.Fiscal.CheckBox.Models
{
    internal class CheckBoxPayment
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        public long Value { get; set; }

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;
    }
}
