namespace BusinessOS.Restaurant.Application.Abstractions;

public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}
