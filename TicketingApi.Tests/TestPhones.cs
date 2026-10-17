namespace TicketingApi.Tests;

internal static class TestPhones
{
    private static long sequence;
    public static string Next() => "+98" + (9120000000L + Interlocked.Increment(ref sequence)).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
