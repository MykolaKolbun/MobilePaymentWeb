namespace MobilePaymentWeb.Helpers
{
    public class Helpers
    {
        public static string ConvertToLatin(string input)
        {
            return input
                .Replace('А', 'A')
                .Replace('В', 'B')
                .Replace('Е', 'E')
                .Replace('І', 'I')
                .Replace('К', 'K')
                .Replace('М', 'M')
                .Replace('О', 'O')
                .Replace('Р', 'P')
                .Replace('С', 'C')
                .Replace('Т', 'T')
                .Replace('Х', 'X')
                .Replace('Н', 'H');
        }
    }
}
