namespace Infrastructure.Helper
{
    public static class PhoneNumberHelper
    {
        public static string? Normalize(string phoneCode, string? mobile)
        {
            if (string.IsNullOrWhiteSpace(phoneCode) || string.IsNullOrWhiteSpace(mobile))
                return null;

            string countryDigits = DigitsOnly(phoneCode);
            string trimmedMobile = mobile.Trim();
            string digits = DigitsOnly(trimmedMobile);

            if (string.IsNullOrEmpty(countryDigits) || string.IsNullOrEmpty(digits))
                return null;

            bool hasInternationalPrefix = trimmedMobile.StartsWith('+') ||
                                          digits.StartsWith("00");

            if (digits.StartsWith("00", StringComparison.Ordinal))
                digits = digits[2..];

            if (hasInternationalPrefix &&
                !digits.StartsWith(countryDigits, StringComparison.Ordinal))
            {
                return null;
            }

            if (digits.Length > countryDigits.Length &&
                digits.StartsWith(countryDigits, StringComparison.Ordinal))
            {
                digits = digits[countryDigits.Length..];
            }

            if (hasInternationalPrefix && digits.StartsWith('0'))
                return null;

            if (!hasInternationalPrefix && digits.Length > 1 && digits[0] == '0')
                digits = digits[1..];

            int internationalDigitCount = countryDigits.Length + digits.Length;

            return string.IsNullOrEmpty(digits) || internationalDigitCount > 15
                ? null
                : $"+{countryDigits}{digits}";
        }

        public static string DigitsOnly(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return new string(value
                .Where(char.IsDigit)
                .Select(character =>
                    (char)('0' + (int)char.GetNumericValue(character)))
                .ToArray());
        }
    }
}
