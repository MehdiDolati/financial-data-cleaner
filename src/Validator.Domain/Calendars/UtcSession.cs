namespace Validator.Domain.Calendars;

public sealed record UtcSession
{
    public UtcSession(DateTimeOffset openUtc, DateTimeOffset closeUtc)
    {
        if (openUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Session open must be UTC.", nameof(openUtc));
        }

        if (closeUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Session close must be UTC.", nameof(closeUtc));
        }

        if (closeUtc <= openUtc)
        {
            throw new ArgumentException("Session close must be after session open.", nameof(closeUtc));
        }

        OpenUtc = openUtc;
        CloseUtc = closeUtc;
    }

    public DateTimeOffset OpenUtc { get; }
    public DateTimeOffset CloseUtc { get; }

    public bool Contains(DateTimeOffset timestampUtc) =>
        OpenUtc <= timestampUtc && timestampUtc < CloseUtc;
}