# Security Audit: MCP Endpoints for Claude MCP Integration

**Date:** 2026-11-02  
**Scope:** Public MCP endpoint security audit for Traefik reverse proxy deployment  
**Application:** CapybaraHome (.NET 10.0 / ASP.NET Core)  
**MCP Library:** ModelContextProtocol.AspNetCore v2.2.0

---

## Executive Summary

The MCP endpoint (`/mcp`) has **basic authentication** in place but is **missing critical security controls** required for a public-facing API. The current implementation relies on bearer token authentication with good token management practices, but lacks rate limiting, request size limits, audit logging, and several security headers.

**Risk Level: HIGH** — The endpoint is not production-ready for public exposure without addressing the findings below.

---

## Current Security Posture

### Authentication Flow

```
Request → Policy Scheme (ApiSchemeName)
    │
    ├── Has Authorization header? → ApiToken Authentication (Bearer token)
    │       └── Validates SHA-256 hash against database
    │
    └── Has capybarahome.auth cookie? → Cookie Authentication
            └── Validates signed-in cookie
```

### What's Working Well ✅

| Control | Status | Details |
|---------|--------|---------|
| **Authentication Required** | ✅ Good | `/mcp` endpoint uses `RequireAuthorization(AuthenticationSetup.ApiPolicyName)` |
| **Token Hashing** | ✅ Good | API tokens stored as SHA-256 hashes, never plaintext |
| **Token Entropy** | ✅ Good | 32 bytes (256 bits) of randomness via `RandomNumberGenerator` |
| **Token Revocation** | ✅ Good | Tokens can be revoked; revoked tokens rejected during auth |
| **CSRF Protection** | ✅ Good | `UseAntiforgery()` middleware active for browser sessions |
| **HSTS** | ✅ Good | `UseHsts()` enabled in production |
| **Input Validation** | ✅ Good | MCP tools clamp limits (e.g., `Math.Clamp(limit, 1, 200)`) |
| **First-Run Protection** | ✅ Good | `/setup` redirect prevents unconfigured instances from being unusable |
| **Data Protection** | ✅ Good | Keys persisted to volume for container resilience |
| **Status Code Pages** | ✅ Good | API/MCP paths return proper status codes, not HTML redirects |

---

## Critical Findings 🔴

### 1. NO RATE LIMITING — CRITICAL

**Severity:** CRITICAL  
**Location:** `Program.cs`, `CapybaraHome.Web.csproj`

The MCP endpoint has **zero rate limiting**. A public-facing MCP endpoint accessible to Claude can be abused for:
- **Brute force attacks** on API tokens (256-bit entropy helps, but still possible)
- **Resource exhaustion** via expensive tool calls (home search scoring, Jira imports)
- **Denial of service** against the database and external services

**Evidence:**
```csharp
// Program.cs line 102
app.MapMcp("/mcp").RequireAuthorization(AuthenticationSetup.ApiPolicyName);
// No RateLimiter middleware configured anywhere
```

```csharp
// No rate limiting packages in CapybaraHome.Web.csproj
// No UseRateLimiter() call in Program.cs
```

**Recommendation:**
```csharp
// Add to CapybaraHome.Web.csproj:
// <PackageReference Include="Asp.NetCoreRateLimiter" Version="*" />

// Add to Program.cs before app.Build():
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
        context => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() 
                ?? context.Request.Headers["X-Forwarded-For"].ToString(),
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoRecovery = true,
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1)
            }));
    
    options.AddPolicy("mcp-authenticated", rateLimitOptions =>
        rateLimitOptions.PermitPartition()
            .OnRejected(async (context, token) =>
            {
                context.HttpContext.Response.StatusCode = 429;
                await context.HttpContext.Response.WriteAsync(
                    "Rate limit exceeded. Please slow down your requests.");
                return Task.CompletedTask;
            }));
});

app.UseRateLimiter();
```

---

### 2. NO REQUEST SIZE LIMITS — HIGH

**Severity:** HIGH  
**Location:** `Program.cs`, Dockerfile

The MCP endpoint has **no maximum request body size**. An attacker could send extremely large payloads to exhaust memory or cause denial of service.

**Evidence:**
```csharp
// No Kestrel request size limits configured
// No endpoint-level request size constraints
```

**Recommendation:**
```csharp
// In Program.cs, configure Kestrel limits:
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10 MB
    options.Limits.MinRequestBodyDataRate = new MinDataRate(
        bytesPerSecond: 100, 
        gracePeriod: TimeSpan.FromSeconds(10));
    options.Limits.MinResponseDataRate = new MinDataRate(
        bytesPerSecond: 100, 
        gracePeriod: TimeSpan.FromSeconds(10));
});

// Or at endpoint level:
app.MapMcp("/mcp")
    .RequireAuthorization(AuthenticationSetup.ApiPolicyName)
    .ConfigureEndpoint<Microsoft.AspNetCore.Http.Endpoint>(endpoint =>
    {
        endpoint.Metadata.Add(new RequestSizeAttribute(10 * 1024 * 1024));
    });
```

---

### 3. FORWARDED HEADERS TRUSTS ALL PROXIES — HIGH

**Severity:** HIGH  
**Location:** `ForwardedHeadersSetup.cs` lines 29-35

When `KnownProxies` and `KnownNetworks` are empty (the default), the application **trusts any IP address** claiming to be a proxy. This enables **IP spoofing** if the application is ever exposed without Traefik.

**Evidence:**
```csharp
// ForwardedHeadersSetup.cs lines 29-35
if (proxies.Length == 0 && networks.Length == 0)
{
    // A container cannot know the proxy's address ahead of time, and the app is not
    // meant to be reachable except through it, so by default any hop is trusted.
    options.KnownProxies.Clear();
    options.KnownNetworks.Clear();
    return;
}
```

```json
// appsettings.json
"ForwardedHeaders": {
    "KnownProxies": [],
    "KnownNetworks": []
}
```

**Recommendation:**
For Traefik deployment, explicitly configure known proxies:
```json
// appsettings.Production.json
{
    "ForwardedHeaders": {
        "KnownProxies": ["172.18.0.1"],
        "KnownNetworks": ["172.18.0.0/16"]
    }
}
```

Or use Traefik's Docker labels to restrict:
```yaml
# docker-compose.yml or traefik configuration
services:
  capybarahome:
    environment:
      - ForwardedHeaders__KnownProxies=172.18.0.1
      - ForwardedHeaders__KnownNetworks=172.18.0.0/16
```

---

## Important Findings 🟡

### 4. WILDCARD ALLOWEDHOSTS — MEDIUM

**Severity:** MEDIUM  
**Location:** `appsettings.json` line 8

```json
"AllowedHosts": "*"
```

This allows **host header injection** attacks. While low risk behind Traefik, it should be restricted in production.

**Recommendation:**
```json
"AllowedHosts": "capybarahome.example.com"
```

---

### 5. NO SECURITY HEADERS — MEDIUM

**Severity:** MEDIUM  
**Location:** `Program.cs`

Only HSTS is configured. The following security headers are **missing**:

| Header | Purpose |
|--------|---------|
| `X-Content-Type-Options: nosniff` | Prevents MIME-type sniffing |
| `X-Frame-Options: DENY` | Prevents clickjacking |
| `X-XSS-Protection: 1; mode=block` | Legacy XSS filter |
| `Referrer-Policy: strict-origin-when-cross-origin` | Controls referrer info |
| `Permissions-Policy` | Restricts browser features |

**Recommendation:**
```csharp
// In Program.cs, add middleware before app.Run():
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("Permissions-Policy", 
        "geolocation=(), microphone=(), camera=()");
    await next();
});
```

---

### 6. NO AUDIT LOGGING FOR MCP ENDPOINT — MEDIUM

**Severity:** MEDIUM  
**Location:** MCP tools (`TaskTools.cs`, `MealTools.cs`, `GroceryTools.cs`, `HomeSearchTools.cs`)

There is **no logging** of MCP endpoint access or tool invocations. This makes it impossible to:
- Detect brute force attempts
- Audit who accessed what data
- Investigate security incidents

**Evidence:**
```csharp
// TaskTools.cs — No logging of tool calls
[McpServerTool(Name = "search_tasks")]
public static async Task<IReadOnlyList<TaskView>> SearchTasksAsync(
    TaskService tasks,
    string? text = null,  // No validation of input content
    ...)
```

**Recommendation:**
Add structured logging to MCP tools:
```csharp
public class TaskTools
{
    private readonly ILogger<TaskTools> _logger;

    public TaskTools(ILogger<TaskTools> logger)
    {
        _logger = logger;
    }

    [McpServerTool(Name = "search_tasks")]
    public static async Task<IReadOnlyList<TaskView>> SearchTasksAsync(
        TaskService tasks,
        ILogger<TaskTools> logger,
        string? text = null,
        ...)
    {
        logger.LogInformation("MCP tool called: search_tasks by user {UserId}", 
            /* extract from claims */);
        // ... rest of implementation
    }
}
```

---

### 7. NO TOOL-LEVEL AUTHORIZATION — LOW

**Severity:** LOW  
**Location:** MCP tools

All authenticated users have access to **all MCP tools**, including write operations (`create_task`, `update_task`, `create_meal`, etc.). There's no role-based or permission-based access control at the tool level.

**Current MCP Tools Available:**

| Tool | Category | Risk |
|------|----------|------|
| `search_tasks` | Read | Low |
| `get_task` | Read | Low |
| `create_task` | **Write** | Medium |
| `update_task` | **Write** | Medium |
| `list_categories` | Read | Low |
| `search_meals` | Read | Low |
| `get_meal` | Read | Low |
| `create_meal` | **Write** | Medium |
| `update_meal` | **Write** | Medium |
| `create_meal_variant` | **Write** | Medium |
| `list_meal_tags` | Read | Low |
| `get_grocery_list` | Read | Low |
| `add_grocery_item` | **Write** | Low |
| `remove_grocery_item` | **Write** | Low |
| `start_fresh_grocery_list` | **Write** | Low |
| `list_grocery_candidates` | Read | Low |
| `get_home_search` | Read | Low |
| `get_property` | Read | Low |
| `add_property` | **Write** | Medium |
| `add_property_note` | **Write** | Low |

**Recommendation:**
```csharp
// Add authorization attributes to write operations
[McpServerTool(Name = "create_task")]
[Authorize(Policy = "McpWriteAccess")]
public static Task<TaskView> CreateTaskAsync(...)

// Or check claims manually:
var userId = context.HttpContext.User.GetUserId();
var isFirstUser = context.HttpContext.User.IsFirstUser();
```

---

### 8. HEALTH ENDPOINT EXPOSES INFORMATION — LOW

**Severity:** LOW  
**Location:** `Program.cs` line 104

```csharp
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
```

Currently safe (only returns `{"status":"ok"}`), but should be reviewed if extended.

---

## Traefik Configuration Recommendations

Since you're using Traefik to expose the MCP endpoint publicly, ensure the following:

### 1. HTTPS-Only Routing
```yaml
# traefik.yml or docker-compose labels
http:
  routers:
    capybarahome-mcp:
      rule: "Host(`capybarahome.example.com`) && PathPrefix(`/mcp`)"
      service: capybarahome
      tls: {}
      middlewares:
        - security-headers
        - rate-limit
```

### 2. Request Size Limits in Traefik
```yaml
        - name: request-size-limit
          data: |
            [[request.body.limit]]
              size = 10485760  # 10MB
```

### 3. Rate Limiting in Traefik
```yaml
        - name: rate-limit
          data: |
            [rateLimit]
              average = 60
              burst = 10
              period = 1m
```

### 4. IP Whitelisting (Optional but Recommended)
If Claude MCP connections come from known IPs, restrict access:
```yaml
http:
  middlewares:
    ip-whitelist:
      ipWhiteList:
        sourceRange:
          - "claude-mcp-ip-range-1"
          - "claude-mcp-ip-range-2"
```

### 5. Basic Auth as Additional Layer
Consider adding HTTP Basic Auth as a second factor:
```yaml
http:
  middlewares:
    mcp-auth:
      basicAuth:
        users:
          - "mcp-user:$apr1$..."  # Use htpasswd to generate
```

---

## Docker Security Recommendations

### Current Dockerfile Analysis

```dockerfile
# Line 31: Non-root user ✅
USER app

# Line 30: Volume mount for data ✅
VOLUME /data

# Line 23: Production environment ✅
ENV ASPNETCORE_ENVIRONMENT=Production
```

**Recommendations:**
1. Use a non-root user (already done ✅)
2. Add `--read-only` filesystem flag in Docker run:
   ```yaml
   # docker-compose.yml
   services:
     capybarahome:
       read_only: true
       tmpfs:
         - /tmp
       volumes:
         - capybara-data:/data
   ```
3. Add resource limits:
   ```yaml
     capybarahome:
       deploy:
         resources:
           limits:
             memory: 1G
             cpus: '1.0'
   ```
4. Add security options:
   ```yaml
     capybarahome:
       security_opt:
         - no-new-privileges:true
       read_only: true
   ```

---

## Priority Action Items

### Immediate (Before Public Exposure)
1. **[CRITICAL]** Implement rate limiting (application-level or Traefik-level)
2. **[HIGH]** Configure request size limits
3. **[HIGH]** Configure ForwardedHeaders with specific proxy IPs

### Before Production Deployment
4. **[MEDIUM]** Add security headers
5. **[MEDIUM]** Implement audit logging for MCP calls
6. **[MEDIUM]** Restrict `AllowedHosts` to production domain
7. **[LOW]** Add tool-level authorization for write operations

### Nice to Have
8. Implement API versioning for future changes
9. Add health check endpoint with more detail (without exposing sensitive info)
10. Implement circuit breakers for external service calls (Jira, routing APIs)
11. Add request correlation IDs for distributed tracing
12. Consider adding JWT token expiration/rotation for API tokens

---

## MCP Tools Data Access Summary

All MCP tools access the same services as the web UI, meaning they have the **same data access level** as an authenticated user. The following data is accessible via MCP:

| Data Category | Tools | Sensitivity |
|--------------|-------|-------------|
| Tasks | `search_tasks`, `get_task`, `create_task`, `update_task`, `list_categories` | Medium |
| Meals | `search_meal`, `get_meal`, `create_meal`, `update_meal`, `create_meal_variant`, `list_meal_tags` | Low |
| Grocery | `get_grocery_list`, `add_grocery_item`, `remove_grocery_item`, `start_fresh_grocery_list`, `list_grocery_candidates` | Low |
| Home Search | `get_home_search`, `get_property`, `add_property`, `add_property_note` | High (contains addresses, financial data) |

**Note:** Home search data (addresses, rent prices, drive times) should be treated as **sensitive PII** and protected accordingly.

---

## Conclusion

The MCP endpoint has a **solid authentication foundation** but needs **operational security controls** before public exposure. The most critical gaps are rate limiting, request size limits, and proper proxy configuration. With the recommendations above, the endpoint can be securely exposed through Traefik.

**Estimated effort to address critical/high findings:** 4-8 hours  
**Recommended testing before deployment:** Penetration test focusing on authentication bypass and DoS vectors
