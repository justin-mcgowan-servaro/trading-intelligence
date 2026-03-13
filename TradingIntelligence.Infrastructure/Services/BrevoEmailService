using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TradingIntelligence.Infrastructure.Services;

public class BrevoEmailService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<BrevoEmailService> _logger;

    public BrevoEmailService(
        IHttpClientFactory factory,
        IConfiguration config,
        ILogger<BrevoEmailService> logger)
    {
        _http = factory.CreateClient("Brevo");
        _config = config;
        _logger = logger;
    }

    public async Task<bool> SendOtpAsync(
        string toEmail,
        string otp,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _config["Brevo:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("Brevo API key not configured");
            return false;
        }

        var payload = new
        {
            sender = new { name = "Servaro", email = _config["Brevo:SenderEmail"] ?? "noreply@servaro.co.za" },
            to = new[] { new { email = toEmail } },
            subject = $"{otp} — Your Servaro login code",
            htmlContent = $"""
                <div style="font-family: -apple-system, sans-serif; max-width: 480px; margin: 0 auto; padding: 40px 24px; background: #0d1117; color: #e6edf3; border-radius: 12px;">
                  <h1 style="color: #00c2ff; font-size: 22px; margin: 0 0 8px;">⚡ Servaro</h1>
                  <p style="color: #8b949e; font-size: 14px; margin: 0 0 32px;">Trading Intelligence Platform</p>
                  <p style="font-size: 15px; margin: 0 0 24px;">Your login code is:</p>
                  <div style="background: #161b22; border: 1px solid #30363d; border-radius: 8px; padding: 24px; text-align: center; margin-bottom: 24px;">
                    <span style="font-size: 36px; font-weight: 800; letter-spacing: 8px; color: #58a6ff; font-family: monospace;">{otp}</span>
                  </div>
                  <p style="color: #8b949e; font-size: 13px; margin: 0 0 8px;">This code expires in <strong style="color: #e6edf3;">10 minutes</strong>.</p>
                  <p style="color: #8b949e; font-size: 13px; margin: 0;">If you didn't request this, you can safely ignore this email.</p>
                </div>
                """
        };

        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://api.brevo.com/v3/smtp/email");
        request.Headers.Add("api-key", apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        try
        {
            var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Brevo send failed {Status}: {Body}",
                    response.StatusCode, body);
                return false;
            }
            _logger.LogInformation("OTP email sent to {Email}", toEmail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Brevo send threw exception for {Email}", toEmail);
            return false;
        }
    }
}
