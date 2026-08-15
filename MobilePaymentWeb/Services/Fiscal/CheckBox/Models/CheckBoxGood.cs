using System.Text.Json.Serialization;

namespace MobilePaymentWeb.Services.Fiscal.CheckBox.Models
{
    internal class CheckBoxGood
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("price")]
        public int Price { get; set; }

        [JsonPropertyName("tax")]
        public List<int?> Tax { get; set; } = new();
    }
}
