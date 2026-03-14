using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using TradingIntelligence.Core.Entities;
using TradingIntelligence.Core.Enums;
using TradingIntelligence.Infrastructure.Data;
using TradingIntelligence.Infrastructure.Services;

namespace TradingIntelligence.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly BrevoEmailService _brevo;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AuthController> _logger;

    // Rate limit constants
    private const int MaxOtpRequestsPerWindow = 3;
    private const int MaxVerifyAttemptsPerCode = 5;
    private static readonly TimeSpan OtpRequestWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan OtpExpiry = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan IpBlockWindow = TimeSpan.FromMinutes(15);
    private const int MaxOtpRequestsPerIp = 10;

    public AuthController(
        AppDbContext db,
        IConfiguration config,
        BrevoEmailService brevo,
        IMemoryCache cache,
        ILogger<AuthController> logger)
    {
        _db = db;
        _config = config;
        _brevo = brevo;
        _cache = cache;
        _logger = logger;
    }

    // POST /api/auth/request-otp
    // Rate limited: 3 per email per 15min, 10 per IP per 15min
    [HttpPost("request-otp")]
    public async Task<IActionResult> RequestOtp(
        [FromBody] OtpRequest request,
        CancellationToken cancellationToken)
    {
        // Honeypot — bots fill this, humans don't
        if (!string.IsNullOrEmpty(request.Website))
            return Ok(new { message = "If that email exists, a code is on its way." });

        if (string.IsNullOrWhiteSpace(request.Email) ||
            !request.Email.Contains('@') ||
            request.Email.Length > 255)
            return BadRequest(new { message = "Valid email required" });

        var email = request.Email.ToLowerInvariant().Trim();
        var ip = GetClientIp();

        // IP rate limit
        var ipKey = $"otp:ip:{ip}";
        if (_cache.TryGetValue(ipKey, out int ipCount) && ipCount >= MaxOtpRequestsPerIp)
        {
            _logger.LogWarning("IP {IP} blocked — OTP rate limit exceeded", ip);
            return StatusCode(429, new { message = "Too many requests. Try again later." });
        }

        // Email rate limit
        var emailKey = $"otp:email:{email}";
        if (_cache.TryGetValue(emailKey, out int emailCount) &&
            emailCount >= MaxOtpRequestsPerWindow)
        {
            _logger.LogWarning("Email {Email} rate limited for OTP requests", email);
            // Return 200 to avoid email enumeration
            return Ok(new { message = "If that email exists, a code is on its way." });
        }

        // Invalidate any existing unused OTPs for this email
        var existing = await _db.OtpCodes
            .Where(o => o.Email == email && !o.Used && o.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(cancellationToken);
        foreach (var old in existing) old.Used = true;

        // Generate OTP
        var otp = GenerateOtp();
        var otpHash = HashOtp(otp);

        var otpEntity = new OtpCode
        {
            Email = email,
            CodeHash = otpHash,
            ExpiresAt = DateTime.UtcNow.Add(OtpExpiry),
            CreatedAt = DateTime.UtcNow,
            IpAddress = ip
        };

        _db.OtpCodes.Add(otpEntity);
        await _db.SaveChangesAsync(cancellationToken);

        // Increment rate limit counters
        _cache.Set(ipKey, (ipCount) + 1, IpBlockWindow);
        _cache.Set(emailKey, emailCount + 1, OtpRequestWindow);

        // Send email — fire and forget the result to avoid timing attacks
        _ = _brevo.SendOtpAsync(email, otp, cancellationToken);

        _logger.LogInformation("OTP requested for {Email} from {IP}", email, ip);

        // Always return same message — prevents email enumeration
        return Ok(new { message = "If that email exists, a code is on its way." });
    }

    // POST /api/auth/verify-otp
    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp(
        [FromBody] VerifyOtpRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new { message = "Email and code required" });

        var email = request.Email.ToLowerInvariant().Trim();
        var ip = GetClientIp();

        // IP rate limit on verify attempts
        var ipVerifyKey = $"verify:ip:{ip}";
        if (_cache.TryGetValue(ipVerifyKey, out int ipVerifyCount) &&
            ipVerifyCount >= 20)
        {
            _logger.LogWarning("IP {IP} blocked — verify rate limit exceeded", ip);
            return StatusCode(429, new { message = "Too many attempts. Try again later." });
        }

        // Find valid OTP
        var otpEntity = await _db.OtpCodes
            .Where(o => o.Email == email &&
                        !o.Used &&
                        o.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        // Constant-time path whether OTP exists or not
        var codeHash = HashOtp(request.Code.Trim());
        bool valid = otpEntity != null &&
                     CryptographicOperations.FixedTimeEquals(
                         Convert.FromHexString(codeHash),
                         Convert.FromHexString(otpEntity.CodeHash));

        if (!valid)
        {
            // Increment attempt counter
            if (otpEntity != null)
            {
                otpEntity.AttemptCount++;
                if (otpEntity.AttemptCount >= MaxVerifyAttemptsPerCode)
                {
                    otpEntity.Used = true; // Burn the code after too many failures
                    _logger.LogWarning(
                        "OTP for {Email} burned after {Count} failed attempts",
                        email, otpEntity.AttemptCount);
                }
                await _db.SaveChangesAsync(cancellationToken);
            }

            _cache.Set(ipVerifyKey, ipVerifyCount + 1, IpBlockWindow);
            return Unauthorized(new { message = "Invalid or expired code" });
        }

        // Mark OTP as used
        otpEntity!.Used = true;
        await _db.SaveChangesAsync(cancellationToken);

        // Upsert user — create if first time, update LastLoginAt if returning
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user == null)
        {
            user = new User
            {
                Email = email,
                PasswordHash = string.Empty, // No longer used
                Tier = UserTier.Free,
                EmailConfirmed = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                LastLoginAt = DateTime.UtcNow
            };
            _db.Users.Add(user);
            _logger.LogInformation("New user registered via OTP: {Email}", email);
        }
        else
        {
            user.LastLoginAt = DateTime.UtcNow;
            user.EmailConfirmed = true;
        }

        await _db.SaveChangesAsync(cancellationToken);

        var token = GenerateJwt(user);

        _logger.LogInformation("User {Email} authenticated via OTP from {IP}",
            email, ip);

        return Ok(new
        {
            token,
            email = user.Email,
            tier = user.Tier.ToString()
        });
    }

    // Background cleanup endpoint — call from a Quartz job
    // DELETE /api/auth/cleanup-otp (internal use)
    [HttpDelete("cleanup-otp")]
    public async Task<IActionResult> CleanupOtp(
        CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow;
        var expired = await _db.OtpCodes
            .Where(o => o.ExpiresAt < cutoff || o.Used)
            .ToListAsync(cancellationToken);

        _db.OtpCodes.RemoveRange(expired);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { removed = expired.Count });
    }

    private string GenerateJwt(User user)
    {
        var secret = _config["Jwt:SecretKey"]
            ?? throw new InvalidOperationException("JWT secret not configured");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim("tier", user.Tier.ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddDays(30), // 30-day sessions
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateOtp()
    {
        // Cryptographically random 6-digit code
        var bytes = RandomNumberGenerator.GetBytes(4);
        var value = BitConverter.ToUInt32(bytes, 0) % 1_000_000;
        return value.ToString("D6");
    }

    private static string HashOtp(string otp)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(otp.Trim()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string GetClientIp()
    {
        // Respect X-Forwarded-For from Nginx proxy
        var forwarded = HttpContext.Request.Headers["X-Forwarded-For"]
            .FirstOrDefault();
        if (!string.IsNullOrEmpty(forwarded))
            return forwarded.Split(',')[0].Trim();

        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}

public record OtpRequest(string Email, string? Website); // Website = honeypot
public record VerifyOtpRequest(string Email, string Code);
