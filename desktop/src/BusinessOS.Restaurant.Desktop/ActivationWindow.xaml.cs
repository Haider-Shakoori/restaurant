using System.Diagnostics;
using System.Windows;
using BusinessOS.Restaurant.Licensing;

namespace BusinessOS.Restaurant.Desktop;

public partial class ActivationWindow : Window
{
    private readonly RestaurantLicenseCoordinator _licenses;
    private bool _busy;

    public ActivationWindow(RestaurantLicenseCoordinator? licenses = null)
    {
        InitializeComponent();
        _licenses = licenses ?? new RestaurantLicenseCoordinator();
    }

    private async void OnActivateClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var key = LicenseKeyBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            StatusText.Text = "Enter the Restaurant license key.";
            return;
        }

        _busy = true;
        ActivateButton.IsEnabled = false;
        LicenseKeyBox.IsEnabled = false;
        StatusText.Text = "Verifying license with BusinessOS…";

        try
        {
            await _licenses.ActivateAsync(key);
            LicenseKeyBox.Clear();

            var status = await _licenses.GetStatusAsync();
            StatusText.Text =
                $"Activated • {status.PlanName} • {status.DaysRemaining} day(s) remaining.";

            DialogResult = true;
        }
        catch (LicenseApiException exception)
        {
            StatusText.Text = exception.Message;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            StatusText.Text = "The server license signature could not be verified.";
        }
        catch
        {
            StatusText.Text =
                "Activation could not be completed. Check the license key and internet connection.";
        }
        finally
        {
            _busy = false;
            ActivateButton.IsEnabled = true;
            LicenseKeyBox.IsEnabled = true;
        }
    }

    private void OnTrialClick(object sender, RoutedEventArgs e)
    {
        const string url = "https://restaurant.businessos.af";

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });

            StatusText.Text =
                "restaurant.businessos.af opened. Complete the 7-day trial request, then return with the generated license key.";
        }
        catch
        {
            StatusText.Text = $"Open {url} in your browser to start the 7-day trial.";
        }
    }
}
