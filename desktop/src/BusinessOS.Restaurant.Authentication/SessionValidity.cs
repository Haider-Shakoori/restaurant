namespace BusinessOS.Restaurant.Authentication;

public static class SessionValidity
{
    // A protected session may be reused offline for one shift, never indefinitely.
    public static bool IsValid(AuthSession session, DateTimeOffset now) =>
        session.SignedInAt <= now.AddMinutes(5) &&
        session.SignedInAt.AddHours(12) > now &&
        !string.IsNullOrWhiteSpace(session.AccessToken) &&
        !string.IsNullOrWhiteSpace(session.TenantId) && session.User is not null;
}
