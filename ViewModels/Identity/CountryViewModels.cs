namespace ViewModels.Identity
{
    public sealed class CountryLookupDto
    {
        public int Id { get; set; }

        public string Name { get; set; } = null!;

        public string IsoCode { get; set; } = null!;

        public string PhoneCode { get; set; } = null!;

        public string? Flag { get; set; }
    }
}
