using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows.Media.Imaging;
using BusinessOS.Restaurant.Authentication;
using BusinessOS.Restaurant.Licensing;
using BusinessOS.Restaurant.LocalServer;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QRCoder;

namespace BusinessOS.Restaurant.Desktop;

public sealed class MobilePairingViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WindowsActivationStore _activationStore = new();
    private readonly WindowsSessionStore _sessionStore = new();
    private readonly ConnectionSettingsStore _settingsStore = new();
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    private BitmapImage? _qrImage;
    private string _status = "Generate a QR code to pair a waiter phone.";
    private string _localAddress = "Not available";
    private string _cloudAddress = "Not configured";
    private string _expires = string.Empty;
    private bool _isBusy;

    public MobilePairingViewModel()
    {
        GenerateCommand = new AsyncRelayCommand(GenerateAsync, () => !IsBusy);
    }

    public IAsyncRelayCommand GenerateCommand { get; }

    public BitmapImage? QrImage
    {
        get => _qrImage;
        private set => SetProperty(ref _qrImage, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string LocalAddress
    {
        get => _localAddress;
        private set => SetProperty(ref _localAddress, value);
    }

    public string CloudAddress
    {
        get => _cloudAddress;
        private set => SetProperty(ref _cloudAddress, value);
    }

    public string Expires
    {
        get => _expires;
        private set => SetProperty(ref _expires, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                GenerateCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private async Task GenerateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        Status = "Creating a secure one-time pairing code...";

        try
        {
            var activation = await _activationStore.LoadAsync();
            var session = await _sessionStore.LoadAsync();
            var settings = await _settingsStore.LoadAsync();

            if (activation is null)
            {
                throw new InvalidOperationException("Activate the restaurant desktop before pairing mobile devices.");
            }

            if (session is null)
            {
                throw new InvalidOperationException("Sign in as an Owner, Admin or Manager before generating a pairing QR code.");
            }

            if (settings is null)
            {
                throw new InvalidOperationException("Restaurant connection settings are not configured.");
            }

            CloudAddress = settings.TenantBaseUrl.TrimEnd('/');

            var descriptor = LocalServerDescriptor.Create(new LocalServerOptions(
                activation.Snapshot.TenantId,
                settings.LocalServerPort,
                settings.LocalServerEnabled));

            LocalAddress = descriptor.BaseUrls.FirstOrDefault() ?? "No private LAN address detected";

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                CloudAddress + "/api/v1/mobile/pairings");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
            request.Content = JsonContent.Create(new { });

            using var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(json)
                        ? $"Pairing request failed with HTTP {(int)response.StatusCode}."
                        : json);
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var token = root.GetProperty("pairing_token").GetString()
                ?? throw new InvalidOperationException("Pairing token was missing from the server response.");
            var expiresAt = root.GetProperty("expires_at").GetString() ?? string.Empty;

            var payload = JsonSerializer.Serialize(new
            {
                type = "businessos.restaurant.pair",
                version = 1,
                local_url = descriptor.BaseUrls.FirstOrDefault(),
                cloud_url = CloudAddress,
                pairing_token = token,
                expires_at = expiresAt,
            }, JsonOptions);

            QrImage = CreateQrImage(payload);
            Expires = string.IsNullOrWhiteSpace(expiresAt)
                ? "Valid for about 5 minutes"
                : $"Expires {expiresAt}";
            Status = "Scan this QR code from the BusinessOS Restaurant mobile app. It can be used only once.";
        }
        catch (Exception exception)
        {
            QrImage = null;
            Expires = string.Empty;
            Status = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static BitmapImage CreateQrImage(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(12, drawQuietZones: true);

        var image = new BitmapImage();
        using var stream = new MemoryStream(png);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
