namespace Salon.Domain.Entities;

public static class BookingStatuses
{
    public const string Pending = "Pending";
    public const string Confirmed = "Confirmed";
    public const string CheckedIn = "CheckedIn";
    public const string InService = "InService";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
    public const string NoShow = "NoShow";

    public static bool IsKnown(string status) => status is Pending or Confirmed or CheckedIn or InService or Completed or Cancelled or NoShow;
    public static bool IsTerminal(string status) => status is Completed or Cancelled or NoShow;
}

public static class BookingStatusTransitions
{
    private static readonly Dictionary<string, string[]> Allowed = new()
    {
        [BookingStatuses.Pending] = [BookingStatuses.Confirmed, BookingStatuses.Cancelled],
        [BookingStatuses.Confirmed] = [BookingStatuses.CheckedIn, BookingStatuses.Cancelled, BookingStatuses.NoShow],
        [BookingStatuses.CheckedIn] = [BookingStatuses.InService, BookingStatuses.Cancelled, BookingStatuses.NoShow],
        [BookingStatuses.InService] = [BookingStatuses.Completed, BookingStatuses.Cancelled, BookingStatuses.NoShow],
        [BookingStatuses.Completed] = [],
        [BookingStatuses.Cancelled] = [],
        [BookingStatuses.NoShow] = [],
    };

    public static IReadOnlyList<string> Next(string current)
    {
        if (!BookingStatuses.IsKnown(current))
            throw new ArgumentException("Unknown booking status.", nameof(current));
        return Allowed[current];
    }

    public static bool CanTransition(string current, string target) =>
        BookingStatuses.IsKnown(current) && BookingStatuses.IsKnown(target) && Allowed[current].Contains(target);
}
