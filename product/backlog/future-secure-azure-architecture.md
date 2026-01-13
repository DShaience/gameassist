# Future: Secure Azure Architecture for Desktop App

**Status:** Backlog (not scheduled)  
**Priority:** P2 — Consider for public release  
**Created:** 2026-01-13  
**Context:** Discussion about API key security in friends-only distribution

---

## Problem Statement

The current friends-only distribution includes Azure API keys (Foundry, Speech) in cleartext config files. This is acceptable for trusted friends but not suitable for:
- Public distribution
- Paid users
- Any scenario where the ZIP might be shared widely

**Key insight:** Embedding keys in a desktop app (even encrypted) provides no real security because:
- .NET binaries are trivially decompilable
- Any encryption key shipped with the app can be extracted
- "Security through obscurity" is not security

---

## Recommended Architecture: User Auth + Backend Proxy

The canonical Azure pattern for distributed desktop apps:

```
┌─────────────────┐      ┌─────────────────┐      ┌─────────────────┐
│  Desktop App    │ ──►  │  Your Backend   │ ──►  │  Azure OpenAI   │
│  (C# WinForms)  │      │  (Azure Func/   │      │  Azure Speech   │
│                 │      │   App Service)  │      │                 │
└────────┬────────┘      └────────┬────────┘      └─────────────────┘
         │                        │
         │ 1. User signs in       │ 2. Backend has Managed Identity
         │    (Azure AD B2C)      │    or Key Vault access
         │                        │
         ▼                        ▼
┌─────────────────┐      ┌─────────────────┐
│  Azure AD B2C   │      │  Key Vault      │
│  (User Identity)│      │  (Secrets)      │
└─────────────────┘      └─────────────────┘
```

### How It Works

| Step | What Happens | Azure Service |
|------|--------------|---------------|
| 1 | User signs in with email/social account | **Azure AD B2C** |
| 2 | App receives a user access token (short-lived) | MSAL library |
| 3 | App calls your backend with that token | Azure Functions |
| 4 | Backend validates token, checks user authorization | Azure AD middleware |
| 5 | Backend calls Azure OpenAI/Speech using **Managed Identity** | No keys needed! |
| 6 | Backend returns result to app | — |

### Why This Works

- **Desktop app never has API keys** — only user's auth token
- **User tokens are short-lived** — if stolen, expire in ~1 hour
- **You can revoke specific users** — block accounts in B2C
- **Backend uses Managed Identity** — Azure handles key rotation automatically
- **Full audit trail** — you see which user made which request

---

## Cost Estimate

| Component | Pricing | Notes |
|-----------|---------|-------|
| Azure AD B2C | **Free** up to 50K MAU | Monthly Active Users |
| Azure Functions (Consumption) | **~$0** for low volume | Pay per execution |
| Key Vault | ~$0.03/10K operations | Minimal for this use case |
| Managed Identity | **Free** | Just configuration |

For a small user base (<100 active users), total monthly cost would be **< $1** beyond existing OpenAI/Speech usage.

---

## Implementation Outline

### 1. Azure AD B2C Setup
- Create B2C tenant in Azure portal
- Configure user flows (sign-up, sign-in)
- Register the desktop app as a client
- Configure allowed redirect URIs

### 2. Desktop App Changes
Add MSAL (Microsoft Authentication Library):

```csharp
// NuGet: Microsoft.Identity.Client
private readonly IPublicClientApplication _authApp;

public async Task<string> AcquireTokenAsync()
{
    var result = await _authApp
        .AcquireTokenInteractive(scopes)
        .ExecuteAsync();
    return result.AccessToken;
}
```

Replace direct API calls with backend proxy calls:

```csharp
// Before (keys in app)
var client = new AzureOpenAIClient(endpoint, new ApiKeyCredential(apiKey));
var response = await client.GetChatCompletionsAsync(...);

// After (user auth + backend)
var userToken = await _authService.AcquireTokenAsync();
var response = await _httpClient.PostAsync(
    "https://myfunc.azurewebsites.net/api/translate",
    new { screenshot = base64, question = text },
    new AuthenticationHeaderValue("Bearer", userToken));
```

### 3. Backend (Azure Functions)
Create a simple HTTP-triggered function:

```csharp
[Function("Translate")]
public async Task<IActionResult> Run(
    [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
{
    // Validate user token
    var userId = await ValidateTokenAsync(req.Headers["Authorization"]);
    if (userId == null) return new UnauthorizedResult();
    
    // Call Azure OpenAI with Managed Identity
    var credential = new DefaultAzureCredential(); // Uses MI in Azure
    var client = new AzureOpenAIClient(endpoint, credential);
    
    // ... process request and return response
}
```

### 4. Managed Identity Configuration
- Enable System-Assigned Managed Identity on the Function App
- Grant the MI access to Azure OpenAI resource (Cognitive Services User role)
- Grant the MI access to Azure Speech resource

---

## Migration Path

| Phase | What | Effort |
|-------|------|--------|
| Current | Friends-only with cleartext keys | Done ✅ |
| Phase 1 | Add backend proxy, keep keys in backend | 1-2 days |
| Phase 2 | Add B2C user authentication | 1-2 days |
| Phase 3 | Migrate to Managed Identity (no keys anywhere) | 1 day |
| Phase 4 | Remove keys from desktop app entirely | 0.5 days |

---

## Alternatives Considered

| Approach | Verdict |
|----------|---------|
| Encrypt config with embedded key | ❌ Provides no real security |
| Hardware attestation (TPM) | ❌ Not supported by Azure AI APIs |
| IP allowlisting | ❌ User IPs change constantly |
| Client certificates in app | ❌ Can be extracted from binary |
| Per-user API keys | ⚠️ Complex key management |
| **User auth + backend proxy** | ✅ Recommended |

---

## When to Implement

Trigger conditions for moving to this architecture:
- [ ] Planning public release (App Store, website download)
- [ ] More than ~10 users
- [ ] Any concern about key leakage
- [ ] Want per-user usage tracking/billing

For friends-only distribution, cleartext keys remain acceptable.

---

## References

- [Azure AD B2C documentation](https://docs.microsoft.com/azure/active-directory-b2c/)
- [MSAL for .NET](https://docs.microsoft.com/azure/active-directory/develop/msal-net-initializing-client-applications)
- [Managed Identity overview](https://docs.microsoft.com/azure/active-directory/managed-identities-azure-resources/overview)
- [Azure Functions authentication](https://docs.microsoft.com/azure/azure-functions/functions-bindings-http-webhook-trigger#authorization-keys)
