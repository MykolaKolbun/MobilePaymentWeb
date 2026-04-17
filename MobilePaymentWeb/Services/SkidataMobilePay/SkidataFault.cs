using System.ServiceModel.Channels;

namespace MobilePaymentWeb.Services.SkidataMobilePay
{
    public class SkidataFault
    {
        public string? Type { get; set; }
        public string? Message { get; set; }
        public int? ErrorCode { get; set; }
        public int? RejectionCode { get; set; }
        public string? FaultCode { get; set; }
        public string? Source { get; set; }

        public static SkidataFault Parse(string faultMessage)
        {
            var fault = new SkidataFault();
            var parts = faultMessage.Split('\t');

            fault.Type = parts.FirstOrDefault();

            foreach (var part in parts)
            {
                if (part.StartsWith("Message:"))
                    fault.Message = part.Substring("Message:".Length);
                else if (part.StartsWith("ErrorCode:") && int.TryParse(part.Substring("ErrorCode:".Length), out var ec))
                    fault.ErrorCode = ec;
                else if (part.StartsWith("RejectionCode:") && int.TryParse(part.Substring("RejectionCode:".Length), out var rc))
                    fault.RejectionCode = rc;
                else if (part.StartsWith("FaultCode:"))
                    fault.FaultCode = part.Substring("FaultCode:".Length);
                else if (part.StartsWith("Source:"))
                    fault.Source = part.Substring("Source:".Length);
            }

            return fault;
        }

        private static readonly Dictionary<int, string> RejectionMessages = new()
                {
                    { 1,   "Неможливо зчитати квиток" },
                    { 6,   "Квиток недійсний для в'їзду" },
                    { 7,   "Квиток недійсний для виїзду" },
                    { 8,   "Квиток недійсний для оплати" },
                    { 13,  "Квиток заблоковано" },
                    { 19,  "Квиток ще не дійсний" },
                    { 20,  "Термін дії квитка закінчився" },
                    { 21,  "Квиток вже присутній" },
                    { 22,  "Квиток не використовувався для в'їзду" },
                    { 47,  "Оплата не потрібна" },
                    { 62,  "Недійсний квиток" },
                    { 81,  "Квиток вже використано для виїзду" },
                    { 132, "Перевищено максимальний час паркування" },
                    { 150, "Квиток не знайдено в базі даних" },
                    { 193, "Сума до сплати дорівнює 0" },
                    { 211, "Номерний знак відсутній" },
                    { 229, "Паркінг зачинено" },
                    { 261, "Виїзд з невірним номерним знаком" },
                    { 268, "Номерний знак не отримано" },
                };

        public string FriendlyMessage =>
            RejectionCode.HasValue && RejectionMessages.TryGetValue(RejectionCode.Value, out var msg)
                ? msg
                : Message ?? "Помилка сервісу паркінгу";
    }
}
