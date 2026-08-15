using System.Text.Json.Serialization;

namespace MobilePaymentWeb.Services.Fiscal.CheckBox.Models
{
    internal class CheckBoxAccessToken
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;
    }
}
