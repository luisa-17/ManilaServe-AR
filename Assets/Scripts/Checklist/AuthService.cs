using System;
using System.Reflection;            // <-- added for reflection
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using UnityEngine;

public static class AuthService
{
    static bool _initialized;
    static FirebaseAuth _auth;
    static bool _optionsLogged;

    public static bool IsInitialized => _initialized;
    public static FirebaseAuth Auth => _auth;
    public static FirebaseUser CurrentUser => _auth?.CurrentUser;

    public static bool IsSignedIn => _initialized && _auth != null && _auth.CurrentUser != null;
    public static string UserId => _auth?.CurrentUser?.UserId;
    public static bool IsAnonymous => _auth?.CurrentUser?.IsAnonymous ?? false;

    public static async Task<bool> EnsureInitializedAsync()
    {
        if (_initialized && _auth != null) return true;

        // Verbose logs
        FirebaseApp.LogLevel = Firebase.LogLevel.Debug;

        var dep = await FirebaseApp.CheckAndFixDependenciesAsync();
        if (dep != DependencyStatus.Available)
        {
            Debug.LogError("Firebase dependencies not available: " + dep);
            return false;
        }

        _auth = FirebaseAuth.DefaultInstance;

        // Try to connect to the Auth emulator if requested via env vars (reflection-safe)
        TryConfigureAuthEmulatorIfAvailable();

        // Log options once so you can confirm ApiKey/ProjectId/AppId being used
        LogFirebaseOptionsOnce();

        _initialized = true;
        return true;
    }

    public static async Task<(bool ok, string message)> SignInEmailPasswordAsync(string email, string password)
    {
        if (!await EnsureInitializedAsync()) return (false, "Unable to connect to authentication service");
        try
        {
            Debug.Log($"[AuthService] Attempting sign in for: {email}");
            await _auth.SignInWithEmailAndPasswordAsync(email, password);
            Debug.Log("[AuthService] Sign in successful!");
            return (true, "Signed in successfully");
        }
        catch (FirebaseException ex)
        {
            var authErr = (AuthError)ex.ErrorCode;
            Debug.LogError("=== FIREBASE EXCEPTION DETAILS ===");
            Debug.LogError($"Exception Type: {ex.GetType().Name}");
            Debug.LogError($"AuthError enum: {authErr} ({ex.ErrorCode})");
            Debug.LogError($"Message: {ex.Message}");
            Debug.LogError($"Stack Trace: {ex.StackTrace}");
            Debug.LogError("==================================");

            string userMessage = ParseFirebaseError(ex, "sign in");
            Debug.Log($"[AuthService] User will see: '{userMessage}'");
            return (false, userMessage);
        }
        catch (Exception ex)
        {
            Debug.LogError("=== GENERIC EXCEPTION DETAILS ===");
            Debug.LogError($"Exception Type: {ex.GetType().Name}");
            Debug.LogError($"Message: {ex.Message}");
            Debug.LogError($"Stack Trace: {ex.StackTrace}");
            Debug.LogError("=================================");
            return (false, "Unable to sign in. Please try again later");
        }
    }

    public static async Task<(bool ok, string message)> SignUpEmailPasswordAsync(string email, string password, string displayName = null)
    {
        if (!await EnsureInitializedAsync()) return (false, "Unable to connect to authentication service");
        try
        {
            Debug.Log($"[AuthService] Attempting sign up for: {email}");
            var cred = await _auth.CreateUserWithEmailAndPasswordAsync(email, password);
            if (!string.IsNullOrEmpty(displayName))
            {
                var profile = new UserProfile { DisplayName = displayName };
                await cred.User.UpdateUserProfileAsync(profile);
            }
            Debug.Log("[AuthService] Sign up successful!");
            return (true, "Account created successfully");
        }
        catch (FirebaseException ex)
        {
            var authErr = (AuthError)ex.ErrorCode;
            Debug.LogError("=== FIREBASE EXCEPTION DETAILS (SIGNUP) ===");
            Debug.LogError($"Exception Type: {ex.GetType().Name}");
            Debug.LogError($"AuthError enum: {authErr} ({ex.ErrorCode})");
            Debug.LogError($"Message: {ex.Message}");
            Debug.LogError("==========================================");

            string userMessage = ParseFirebaseError(ex, "sign up");
            Debug.Log($"[AuthService] User will see: '{userMessage}'");
            return (false, userMessage);
        }
        catch (Exception ex)
        {
            Debug.LogError("=== GENERIC EXCEPTION DETAILS (SIGNUP) ===");
            Debug.LogError($"Exception Type: {ex.GetType().Name}");
            Debug.LogError($"Message: {ex.Message}");
            Debug.LogError("==========================================");
            return (false, "Unable to create account. Please try again later");
        }
    }

    public static async Task<(bool ok, string message)> SignInAnonymouslyAsync()
    {
        if (!await EnsureInitializedAsync()) return (false, "Unable to connect to authentication service");
        try
        {
            Debug.Log("[AuthService] Attempting guest sign in");
            await _auth.SignInAnonymouslyAsync();
            Debug.Log("[AuthService] Guest sign in successful!");
            return (true, "Signed in as guest");
        }
        catch (FirebaseException ex)
        {
            var authErr = (AuthError)ex.ErrorCode;
            Debug.LogError("=== FIREBASE EXCEPTION DETAILS (GUEST) ===");
            Debug.LogError($"Exception Type: {ex.GetType().Name}");
            Debug.LogError($"AuthError enum: {authErr} ({ex.ErrorCode})");
            Debug.LogError($"Message: {ex.Message}");
            Debug.LogError("=========================================");

            string userMessage = ParseFirebaseError(ex, "guest sign in");
            Debug.Log($"[AuthService] User will see: '{userMessage}'");
            return (false, userMessage);
        }
        catch (Exception ex)
        {
            Debug.LogError("=== GENERIC EXCEPTION DETAILS (GUEST) ===");
            Debug.LogError($"Exception Type: {ex.GetType().Name}");
            Debug.LogError($"Message: {ex.Message}");
            Debug.LogError("========================================");
            return (false, "Unable to continue as guest. Please try again later");
        }
    }

    // Helps avoid showing login UI while Firebase is still restoring a persisted user.
    public static async Task<bool> WaitForAuthRestorationAsync(int timeoutMs = 1500)
    {
        if (!await EnsureInitializedAsync()) return false;
        if (_auth.CurrentUser != null) return true;

        var tcs = new TaskCompletionSource<bool>();

        void Handler(object s, EventArgs e)
        {
            if (_auth.CurrentUser != null) tcs.TrySetResult(true);
        }

        _auth.StateChanged += Handler;
        try
        {
            await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
        }
        finally
        {
            _auth.StateChanged -= Handler;
        }

        return _auth.CurrentUser != null;
    }

    public static void SignOut()
    {
        try
        {
            if (_initialized && _auth != null)
            {
                _auth.SignOut();
            }
            else
            {
                // Fallback if called before EnsureInitializedAsync
                FirebaseAuth.DefaultInstance?.SignOut();
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"AuthService.SignOut exception: {e.Message}");
        }
    }

    // ===================== Helpers =====================

    // Version-safe attempt to use the Auth emulator if requested via env vars.
    // If your SDK doesn't have FirebaseAuth.UseEmulator, this will just log a warning.
    private static void TryConfigureAuthEmulatorIfAvailable()
    {
        try
        {
            var useFlag = Environment.GetEnvironmentVariable("USE_AUTH_EMULATOR");
            var hostEnv = Environment.GetEnvironmentVariable("FIREBASE_AUTH_EMULATOR_HOST"); // e.g., "127.0.0.1:9099"

            bool requested = false;
            string host = "127.0.0.1";
            int port = 9099;

            if (!string.IsNullOrEmpty(hostEnv))
            {
                requested = true;
                var parts = hostEnv.Trim().Split(':');
                if (parts.Length >= 1) host = parts[0];
                if (parts.Length >= 2 && int.TryParse(parts[1], out var p)) port = p;
            }
            else if (!string.IsNullOrEmpty(useFlag) &&
                     (useFlag.Equals("1") || useFlag.Equals("true", StringComparison.OrdinalIgnoreCase)))
            {
                requested = true;
            }

            if (!requested)
            {
                Debug.Log("[AuthService] Auth emulator not requested.");
                return;
            }

            var mi = typeof(FirebaseAuth).GetMethod(
                "UseEmulator",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(string), typeof(int) },
                modifiers: null);

            if (mi != null)
            {
                mi.Invoke(_auth, new object[] { host, port });
                Debug.Log($"[AuthService] Using Auth Emulator at {host}:{port}");
            }
            else
            {
                Debug.LogWarning("[AuthService] Auth emulator requested, but FirebaseAuth.UseEmulator is not available in this Firebase Unity SDK version. Continuing with production Auth.");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[AuthService] Failed to configure Auth emulator: {e.Message}");
        }
    }

    private static void LogFirebaseOptionsOnce()
    {
        if (_optionsLogged) return;
        _optionsLogged = true;

        try
        {
            var opts = FirebaseApp.DefaultInstance?.Options;
            if (opts == null)
            {
                Debug.LogError("[Firebase] Options is null");
                return;
            }

            Debug.Log($"[Firebase Options] ApiKey: {opts.ApiKey}");
            Debug.Log($"[Firebase Options] ProjectId: {opts.ProjectId}");
            Debug.Log($"[Firebase Options] AppId: {opts.AppId}");
            Debug.Log($"[Firebase Options] StorageBucket: {opts.StorageBucket}");
            Debug.Log($"[Firebase Options] MessageSenderId: {opts.MessageSenderId}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Firebase Options] Logging failed: {e.Message}");
        }
    }

    /// <summary>
    /// Parses Firebase errors and returns user-friendly error messages.
    /// Prioritizes AuthError enum (Unity) then falls back to string matching.
    /// </summary>
    private static string ParseFirebaseError(Exception exception, string operation)
    {
        string lower = exception.Message?.ToLower() ?? "";

        Debug.Log($"[ParseFirebaseError] Starting to parse error for operation: {operation}");
        Debug.Log($"[ParseFirebaseError] Error message (lowercase): {lower}");

        if (exception is FirebaseException fbe)
        {
            var authError = (AuthError)fbe.ErrorCode;
            Debug.Log($"[ParseFirebaseError] AuthError enum: {authError} ({fbe.ErrorCode})");

            switch (authError)
            {
                case AuthError.InvalidEmail:
                case AuthError.MissingEmail:
                    return "Please enter a valid email address";

                case AuthError.WrongPassword:
                case AuthError.UserNotFound:
                case AuthError.InvalidCredential:
                    return "Incorrect email or password. Please try again";

                case AuthError.EmailAlreadyInUse:
                    return "This email address is already registered";

                case AuthError.WeakPassword:
                    return "Password is too weak. Please use a stronger password";

                case AuthError.UserDisabled:
                    return "This account has been disabled. Please contact support";

                case AuthError.OperationNotAllowed:
                    return "Email/password sign-in is disabled for this project";

                case AuthError.TooManyRequests:
                    return "Too many attempts. Please try again later";

                case AuthError.NetworkRequestFailed:
                    return "Network error. Please check your internet connection";

                case AuthError.InvalidApiKey:
                case AuthError.AppNotAuthorized:
                    return "App configuration error. Please contact support";

                case AuthError.RequiresRecentLogin:
                    return "Please sign in again to complete this action";

                    // AuthError.Unspecified falls through to string matching
            }
        }

        // String matching fallback
        if (lower.Contains("user-not-found") || lower.Contains("no user record") ||
            lower.Contains("wrong-password") || lower.Contains("password is invalid") ||
            lower.Contains("user-mismatch") || lower.Contains("invalid-credential"))
        {
            Debug.Log("[ParseFirebaseError] Matched: user-not-found / wrong-password patterns");
            return "Incorrect email or password. Please try again";
        }

        if (lower.Contains("email-already-in-use") || lower.Contains("already in use"))
            return "This email address is already registered";

        if (lower.Contains("invalid-email") || lower.Contains("badly formatted"))
            return "Please enter a valid email address";

        if (lower.Contains("weak-password") || lower.Contains("password should be at least"))
            return "Password is too weak. Please use a stronger password";

        if (lower.Contains("network") || lower.Contains("connection") ||
            lower.Contains("unreachable") || lower.Contains("offline"))
            return "Network error. Please check your internet connection";

        if (lower.Contains("timeout") || lower.Contains("deadline exceeded"))
            return "Connection timeout. Please try again";

        if (lower.Contains("user-disabled") || lower.Contains("account has been disabled"))
            return "This account has been disabled. Please contact support";

        if (lower.Contains("too-many-requests") || lower.Contains("quota exceeded"))
            return "Too many attempts. Please try again later";

        if (lower.Contains("requires-recent-login") || lower.Contains("token expired"))
            return "Please sign in again to complete this action";

        if (lower.Contains("internal error") || lower.Contains("an error has occurred") ||
            lower.Contains("error occurred internally"))
            return "Service temporarily unavailable. Please try again";

        Debug.LogWarning($"[ParseFirebaseError] NO PATTERN MATCHED! during {operation} | Message: {exception.Message}");
        return $"Unable to {operation}. Please try again later";
    }
}