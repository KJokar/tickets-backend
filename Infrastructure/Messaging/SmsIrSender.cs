using Microsoft.Extensions.Configuration;

namespace Infrastructure.Messaging
{
    public sealed class SmsIrSender(
        HttpClient httpClient,
        IConfiguration configuration) : ISmsSender
    {
        public async Task SendAsync(
            string mobile,
            string message,
            CancellationToken cancellationToken = default)
        {
            string apiKey = GetRequiredSetting("SmsIr:ApiKey");
            string username = GetRequiredSetting("SmsIr:Username");
            string lineNumber = GetRequiredSetting("SmsIr:LineNumber");
            string baseUrl = GetRequiredSetting("SmsIr:BaseUrl");
            string recipient = NormalizeRecipient(mobile);

            string requestUrl = $"{baseUrl}?" +
                $"username={Uri.EscapeDataString(username)}&" +
                $"password={Uri.EscapeDataString(apiKey)}&" +
                $"line={Uri.EscapeDataString(lineNumber)}&" +
                $"mobile={Uri.EscapeDataString(recipient)}&" +
                $"text={Uri.EscapeDataString(message)}";

            using HttpRequestMessage request = new(HttpMethod.Get, requestUrl);
            request.Headers.Add("X-API-KEY", apiKey);

            using HttpResponseMessage response = await httpClient.SendAsync(
                request,
                cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        private string GetRequiredSetting(string key)
        {
            string? value = configuration[key];

            return string.IsNullOrWhiteSpace(value)
                ? throw new InvalidOperationException(
                    $"Required configuration '{key}' was not provided.")
                : value;
        }

        private static string NormalizeRecipient(string mobile)
        {
            string digits = new(mobile.Where(char.IsDigit).ToArray());

            return digits.StartsWith("98", StringComparison.Ordinal) &&
                   digits.Length == 12
                ? $"0{digits[2..]}"
                : digits;
        }
    }
}
