using System.Media;

namespace BusinessOS.Restaurant.Desktop;

/// <summary>
/// Dependency-free Windows cues for restaurant floor operations. A new order
/// and a ready-for-pickup dish use distinct tones so staff can tell them apart
/// without looking at the screen. Every call is safe from any thread and never
/// interrupts ordering when the workstation has no audio device or driver.
/// </summary>
internal static class OperationalAlertSounds
{
    /// <summary>An order was sent to the kitchen (a new KOT round).</summary>
    public static void OrderPlaced() => Play(SystemSounds.Exclamation);

    /// <summary>A dish became ready for waiter pickup.</summary>
    public static void OrderReady() => Play(SystemSounds.Asterisk);

    private static void Play(SystemSound sound)
    {
        try
        {
            sound.Play();
        }
        catch
        {
            // Audio is a nicety: a missing device or driver must never block a
            // kitchen tap or an order send.
        }
    }
}
